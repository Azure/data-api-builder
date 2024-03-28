// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// SqlQueryStructure is an intermediate representation of a SQL query.
    /// This intermediate structure can be used to generate a Postgres or MSSQL
    /// query. In some sense this is an AST (abstract syntax tree) of a SQL
    /// query. However, it only supports the very limited set of SQL constructs
    /// that we are needed to represent a GraphQL query or REST request as SQL.
    /// </summary>
    public class SqlQueryStructure : BaseSqlQueryStructure
    {
        public const string DATA_IDENT = "data";

        /// <summary>
        /// The subqueries with which this query should be joined. The key are
        /// the aliases of the query.
        /// </summary>
        public Dictionary<string, SqlQueryStructure> JoinQueries { get; }

        /// <summary>
        /// Is the result supposed to be a list or not.
        /// </summary>
        public bool IsListQuery { get; set; }

        /// <summary>
        /// Columns to use for sorting.
        /// </summary>
        public List<OrderByColumn> OrderByColumns { get; private set; }

        /// <summary>
        /// Hold the pagination metadata for the query
        /// </summary>
        public PaginationMetadata PaginationMetadata { get; set; }

        /// <summary>
        /// Map query columns' labels to the parameter representing that
        /// column label as a string literal.
        /// Only used for MySql
        /// </summary>
        public Dictionary<string, string> ColumnLabelToParam { get; }

        /// <summary>
        /// Default limit when no first param is specified for list queries
        /// </summary>
        private const uint DEFAULT_LIST_LIMIT = 100;

        /// <summary>
        /// The maximum number of results this query should return.
        /// </summary>
        private uint? _limit = DEFAULT_LIST_LIMIT;

        /// <summary>
        /// If this query is built because of a GraphQL query (as opposed to
        /// REST), then this is set to the resolver context of that query.
        /// </summary>
        IMiddlewareContext? _ctx;

        /// <summary>
        /// The underlying type of the type returned by this query see, the
        /// comment on UnderlyingGraphQLEntityType to understand what an underlying type is.
        /// </summary>
        ObjectType _underlyingFieldType = null!;

        /// <summary>
        /// Used to cache the primary key as a list of OrderByColumn
        /// </summary>
        private List<OrderByColumn>? _primaryKeyAsOrderByColumns;

        /// <summary>
        /// Generate the structure for a SQL query based on GraphQL query
        /// information.
        /// Only use as constructor for the outermost queries not subqueries
        /// </summary>
        public SqlQueryStructure(
            IMiddlewareContext ctx,
            IDictionary<string, object?> queryParams,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            RuntimeConfigProvider runtimeConfigProvider,
            GQLFilterParser gQLFilterParser)
            // This constructor simply forwards to the more general constructor
            // that is used to create GraphQL queries. We give it some values
            // that make sense for the outermost query.
            : this(ctx,
                queryParams,
                sqlMetadataProvider,
                authorizationResolver,
                ctx.Selection.Field,
                ctx.Selection.SyntaxNode,
                // The outermost query is where we start, so this can define
                // create the IncrementingInteger that will be shared between
                // all subqueries in this query.
                new IncrementingInteger(),
                runtimeConfigProvider,
                gQLFilterParser)
        {
            // support identification of entities by primary key when query is non list type nor paginated
            // only perform this action for the outermost query as subqueries shouldn't provide primary key search
            if (!IsListQuery && !PaginationMetadata.IsPaginated)
            {
                AddPrimaryKeyPredicates(queryParams);
            }
        }

        /// <summary>
        /// Generate the structure for a SQL query based on FindRequestContext,
        /// which is created by a FindById or FindMany REST request.
        /// </summary>
        public SqlQueryStructure(
            RestRequestContext context,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            RuntimeConfigProvider runtimeConfigProvider,
            GQLFilterParser gQLFilterParser,
            HttpContext httpContext)
            : this(sqlMetadataProvider,
                authorizationResolver,
                gQLFilterParser,
                predicates: null,
                entityName: context.EntityName,
                counter: new IncrementingInteger(),
                httpContext: httpContext)
        {
            IsListQuery = context.IsMany;
            SourceAlias = $"{DatabaseObject.SchemaName}_{DatabaseObject.Name}";
            AddFields(context, sqlMetadataProvider);
            foreach (KeyValuePair<string, object> predicate in context.PrimaryKeyValuePairs)
            {
                sqlMetadataProvider.TryGetBackingColumn(EntityName, predicate.Key, out string? backingColumn);
                PopulateParamsAndPredicates(field: predicate.Key,
                                            backingColumn: backingColumn!,
                                            value: predicate.Value);
            }

            // context.OrderByClauseOfBackingColumns will lack SourceAlias because it is created in RequestParser
            // which may be called for any type of operation. To avoid coupling the OrderByClauseOfBackingColumns
            // to only Find, we populate the SourceAlias in this constructor where we know we have a Find operation.
            OrderByColumns = context.OrderByClauseOfBackingColumns is not null ?
                context.OrderByClauseOfBackingColumns : PrimaryKeyAsOrderByColumns();

            foreach (OrderByColumn column in OrderByColumns)
            {
                if (string.IsNullOrEmpty(column.TableAlias))
                {
                    column.TableAlias = SourceAlias;
                }
            }

            if (context.FilterClauseInUrl is not null)
            {
                // We use the visitor pattern here to traverse the Filter Clause AST
                // AST has Accept method which takes our Visitor class, and then calls
                // our visit functions. Each node in the AST will then automatically
                // call the visit function for that node types, and we process the AST
                // based on what type of node we are currently traversing.
                ODataASTVisitor visitor = new(this, sqlMetadataProvider);
                try
                {
                    FilterPredicates = GetFilterPredicatesFromOdataClause(context.FilterClauseInUrl, visitor);
                }
                catch (Exception ex)
                {
                    throw new DataApiBuilderException(
                        message: "$filter query parameter is not well formed.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                        innerException: ex);
                }
            }

            if (!string.IsNullOrWhiteSpace(context.After))
            {
                AddPaginationPredicate(SqlPaginationUtil.ParseAfterFromJsonString(context.After,
                                                                                  PaginationMetadata,
                                                                                  sqlMetadataProvider,
                                                                                  EntityName,
                                                                                  runtimeConfigProvider));
            }

            AddColumnsForEndCursor();
            _limit = context.First is not null ? context.First + 1 : DEFAULT_LIST_LIMIT + 1;
            ParametrizeColumns();
        }

        /// <summary>
        /// Use the mapping of exposed names to
        /// backing columns to add column with
        /// the correct name and label.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="sqlMetadataProvider">Provides the mapping of exposed names to backing columns.</param>
        private void AddFields(RestRequestContext context, ISqlMetadataProvider sqlMetadataProvider)
        {
            foreach (string exposedFieldName in context.FieldsToBeReturned)
            {
                sqlMetadataProvider.TryGetBackingColumn(EntityName, exposedFieldName, out string? backingColumn);
                AddColumn(backingColumn!, exposedFieldName);
            }
        }

        /// <summary>
        /// Exposes the primary key of the underlying table of the structure
        /// as a list of OrderByColumn
        /// </summary>
        private List<OrderByColumn> PrimaryKeyAsOrderByColumns()
        {
            if (_primaryKeyAsOrderByColumns is null)
            {
                _primaryKeyAsOrderByColumns = new();

                foreach (string column in PrimaryKey())
                {
                    _primaryKeyAsOrderByColumns.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                                      tableName: DatabaseObject.Name,
                                                                      columnName: column,
                                                                      tableAlias: SourceAlias));
                }
            }

            return _primaryKeyAsOrderByColumns;
        }

        /// <summary>
        /// Private constructor that is used for recursive query generation,
        /// for each subquery that's necessary to resolve a nested GraphQL
        /// request.
        /// </summary>
        private SqlQueryStructure(
                IMiddlewareContext ctx,
                IDictionary<string, object?> queryParams,
                ISqlMetadataProvider sqlMetadataProvider,
                IAuthorizationResolver authorizationResolver,
                IObjectField schemaField,
                FieldNode? queryField,
                IncrementingInteger counter,
                RuntimeConfigProvider runtimeConfigProvider,
                GQLFilterParser gQLFilterParser,
                string entityName = "")
            : this(sqlMetadataProvider,
                  authorizationResolver,
                  gQLFilterParser,
                  predicates: null,
                  entityName: entityName,
                  counter: counter
                  )
        {
            _ctx = ctx;
            IOutputType outputType = schemaField.Type;
            _underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(outputType);

            // extract the query argument schemas before switching schemaField to point to *Connetion.items
            // since the pagination arguments are not placed on the items, but on the pagination query
            IFieldCollection<IInputField> queryArgumentSchemas = schemaField.Arguments;

            PaginationMetadata.IsPaginated = QueryBuilder.IsPaginationType(_underlyingFieldType);

            if (PaginationMetadata.IsPaginated)
            {
                if (queryField != null && queryField.SelectionSet != null)
                {
                    // process pagination fields without overriding them
                    ProcessPaginationFields(queryField.SelectionSet.Selections);

                    // override schemaField and queryField with the schemaField and queryField of *Connection.items
                    queryField = ExtractItemsQueryField(queryField);
                }

                schemaField = ExtractItemsSchemaField(schemaField);

                outputType = schemaField.Type;
                _underlyingFieldType = GraphQLUtils.UnderlyingGraphQLEntityType(outputType);

                // this is required to correctly keep track of which pagination metadata
                // refers to what section of the json
                // for a paginationless chain:
                //      getbooks > publisher > books > publisher
                //      each new entry in the chain corresponds to a subquery so there will be
                //      a matching pagination metadata object chain
                // for a chain with pagination:
                //      books > items > publisher > books > publisher
                //      items do not have a matching subquery so the line of code below is
                //      required to build a pagination metadata chain matching the json result
                PaginationMetadata.Subqueries.Add(QueryBuilder.PAGINATION_FIELD_NAME, PaginationMetadata.MakeEmptyPaginationMetadata());
            }

            EntityName = sqlMetadataProvider.GetDatabaseType() == DatabaseType.DWSQL ? GraphQLUtils.GetEntityNameFromContext(ctx) : _underlyingFieldType.Name;

            if (GraphQLUtils.TryExtractGraphQLFieldModelName(_underlyingFieldType.Directives, out string? modelName))
            {
                EntityName = modelName;
            }

            DatabaseObject.SchemaName = sqlMetadataProvider.GetSchemaName(EntityName);
            DatabaseObject.Name = sqlMetadataProvider.GetDatabaseObjectName(EntityName);
            SourceAlias = CreateTableAlias();

            // SelectionSet will not be null when a field is not a leaf.
            // There may be another entity to resolve as a sub-query.
            if (queryField != null && queryField.SelectionSet != null)
            {
                AddGraphQLFields(queryField.SelectionSet.Selections, runtimeConfigProvider);
            }

            HttpContext httpContext = GraphQLFilterParser.GetHttpContextFromMiddlewareContext(ctx);
            // Process Authorization Policy of the entity being processed.
            AuthorizationPolicyHelpers.ProcessAuthorizationPolicies(EntityActionOperation.Read, queryStructure: this, httpContext, authorizationResolver, sqlMetadataProvider);

            if (outputType.IsNonNullType())
            {
                IsListQuery = outputType.InnerType().IsListType();
            }
            else
            {
                IsListQuery = outputType.IsListType();
            }

            if (IsListQuery && queryParams.ContainsKey(QueryBuilder.PAGE_START_ARGUMENT_NAME))
            {
                // parse first parameter for all list queries
                object? firstObject = queryParams[QueryBuilder.PAGE_START_ARGUMENT_NAME];

                if (firstObject != null)
                {
                    int first = (int)firstObject;

                    if (first <= 0)
                    {
                        throw new DataApiBuilderException(
                        message: $"Invalid number of items requested, {QueryBuilder.PAGE_START_ARGUMENT_NAME} argument must be an integer greater than 0 for {schemaField.Name}. Actual value: {first.ToString()}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                    }

                    _limit = (uint)first;
                }
            }

            if (IsListQuery && queryParams.ContainsKey(QueryBuilder.FILTER_FIELD_NAME))
            {
                object? filterObject = queryParams[QueryBuilder.FILTER_FIELD_NAME];

                if (filterObject is not null)
                {
                    List<ObjectFieldNode> filterFields = (List<ObjectFieldNode>)filterObject;
                    Predicates.Add(GraphQLFilterParser.Parse(
                                        _ctx,
                                        filterArgumentSchema: queryArgumentSchemas[QueryBuilder.FILTER_FIELD_NAME],
                                        fields: filterFields,
                                        queryStructure: this));
                }
            }

            OrderByColumns = PrimaryKeyAsOrderByColumns();
            if (IsListQuery && queryParams.ContainsKey(QueryBuilder.ORDER_BY_FIELD_NAME))
            {
                object? orderByObject = queryParams[QueryBuilder.ORDER_BY_FIELD_NAME];

                if (orderByObject is not null)
                {
                    OrderByColumns = ProcessGqlOrderByArg((List<ObjectFieldNode>)orderByObject, queryArgumentSchemas[QueryBuilder.ORDER_BY_FIELD_NAME]);
                }
            }

            // need to run after the rest of the query has been processed since it relies on
            // TableName, SourceAlias, Columns, and _limit
            if (PaginationMetadata.IsPaginated)
            {
                AddPaginationPredicate(SqlPaginationUtil.ParseAfterFromQueryParams(queryParams, PaginationMetadata, sqlMetadataProvider, EntityName, runtimeConfigProvider));

                if (PaginationMetadata.RequestedEndCursor)
                {
                    AddColumnsForEndCursor();
                }

                if (PaginationMetadata.RequestedHasNextPage || PaginationMetadata.RequestedEndCursor)
                {
                    _limit++;
                }
            }

            // If there are no columns, add the primary key column
            // to prevent failures when executing the database query.
            if (!Columns.Any())
            {
                AddColumn(PrimaryKey()[0]);
            }

            ParametrizeColumns();
        }

        /// <summary>
        /// Private constructor that is used as a base by all public
        /// constructors.
        /// </summary>
        private SqlQueryStructure(
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            List<Predicate>? predicates = null,
            string entityName = "",
            IncrementingInteger? counter = null,
            HttpContext? httpContext = null)
            : base(metadataProvider,
                  authorizationResolver,
                  gQLFilterParser, predicates,
                  entityName,
                  counter,
                  httpContext,
                  EntityActionOperation.Read)
        {
            JoinQueries = new();
            PaginationMetadata = new(this);
            ColumnLabelToParam = new();
            FilterPredicates = string.Empty;
            OrderByColumns = new();
        }

        ///<summary>
        /// Adds predicates for the primary keys in the parameters of the GraphQL query
        ///</summary>
        private void AddPrimaryKeyPredicates(IDictionary<string, object?> queryParams)
        {
            foreach (KeyValuePair<string, object?> parameter in queryParams)
            {
                string columnName = parameter.Key;

                MetadataProvider.TryGetBackingColumn(EntityName, parameter.Key, out string? backingColumnName);
                if (!string.IsNullOrWhiteSpace(backingColumnName))
                {
                    columnName = backingColumnName;
                }

                Predicates.Add(new Predicate(
                    new PredicateOperand(new Column(tableSchema: DatabaseObject.SchemaName,
                                                    tableName: DatabaseObject.Name,
                                                    columnName: columnName,
                                                    tableAlias: SourceAlias)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"{MakeDbConnectionParam(parameter.Value, columnName)}")
                ));
            }
        }

        /// <summary>
        /// Add the predicates associated with the "after" parameter of paginated queries
        /// </summary>
        public void AddPaginationPredicate(IEnumerable<PaginationColumn> afterJsonValues)
        {
            if (!afterJsonValues.Any())
            {
                // no need to create a predicate for pagination
                return;
            }

            foreach (PaginationColumn column in afterJsonValues)
            {
                column.TableAlias = SourceAlias;
                column.ParamName = column.Value is not null ?
                     MakeDbConnectionParam(GetParamAsSystemType(column.Value!.ToString()!, column.ColumnName, GetColumnSystemType(column.ColumnName)), column.ColumnName) :
                     MakeDbConnectionParam(null, column.ColumnName);
            }

            PaginationMetadata.PaginationPredicate = new KeysetPaginationPredicate(afterJsonValues.ToList());
        }

        /// <summary>
        ///  Given the predicate key value pair, where value includes the Predicte Operation as well as the value associated with the field,
        ///  populates the Parameters and Predicates properties.
        /// </summary>
        /// <param name="field">The string representing a field.</param>
        /// <param name="backingColumn">string represents the backing column of the field.</param>
        /// <param name="value">The value associated with a given field.</param>
        /// <param name="op">The predicate operation representing the comparison between field and value.</param>
        private void PopulateParamsAndPredicates(string field, string backingColumn, object? value, PredicateOperation op = PredicateOperation.Equal)
        {
            try
            {
                string parameterName;
                if (value != null)
                {
                    parameterName = MakeDbConnectionParam(
                        GetParamAsSystemType(value.ToString()!, backingColumn, GetColumnSystemType(backingColumn)), backingColumn);
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(DatabaseObject.SchemaName, DatabaseObject.Name, backingColumn, SourceAlias)),
                        op,
                        new PredicateOperand($"{parameterName}")));
                }
                else
                {
                    throw new DataApiBuilderException(
                        message: $"Unexpected value for column \"{field}\" provided.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }
            }
            catch (ArgumentException ex)
            {
                throw new DataApiBuilderException(
                  message: ex.Message,
                  statusCode: HttpStatusCode.BadRequest,
                  subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                  innerException: ex);
            }
        }

        /// <summary>
        /// Store the requested pagination connection fields and return the fields of the <c>"items"</c> field
        /// </summary>
        /// <returns>
        /// The fields of the <c>**Conneciton.items</c> of the Conneciton type used for pagination.
        /// Empty list if <c>items</c> was not requested as a field
        /// </returns>
        void ProcessPaginationFields(IReadOnlyList<ISelectionNode> paginationSelections)
        {
            foreach (ISelectionNode node in paginationSelections)
            {
                FieldNode field = (FieldNode)node;
                string fieldName = field.Name.Value;

                switch (fieldName)
                {
                    case QueryBuilder.PAGINATION_FIELD_NAME:
                        PaginationMetadata.RequestedItems = true;
                        break;
                    case QueryBuilder.PAGINATION_TOKEN_FIELD_NAME:
                        PaginationMetadata.RequestedEndCursor = true;
                        break;
                    case QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME:
                        PaginationMetadata.RequestedHasNextPage = true;
                        break;
                }
            }
        }

        /// <summary>
        /// AddGraphQLFields looks at the fields that are selected in the
        /// GraphQL query and all the necessary elements to the query which are
        /// required to return these fields. This includes adding the columns
        /// to the result set.
        /// Additionally, if a field has a selection set, sub-query or join processing
        /// takes place which is required to fetch nested data.
        /// </summary>
        /// <param name="selections">Fields selection in the GraphQL Query.</param>
        private void AddGraphQLFields(IReadOnlyList<ISelectionNode> selections, RuntimeConfigProvider runtimeConfigProvider)
        {
            foreach (ISelectionNode node in selections)
            {
                if (node.Kind == SyntaxKind.FragmentSpread)
                {
                    if (_ctx == null)
                    {
                        throw new DataApiBuilderException(
                            message: "No GraphQL context exists",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                    }

                    FragmentSpreadNode fragmentSpread = (FragmentSpreadNode)node;
                    DocumentNode document = _ctx.Document;
                    FragmentDefinitionNode fragmentDocumentNode = document.GetNodes()
                        .Where(n => n.Kind == SyntaxKind.FragmentDefinition)
                        .Cast<FragmentDefinitionNode>()
                        .Where(n => n.Name.Value == fragmentSpread.Name.Value)
                        .First();

                    AddGraphQLFields(fragmentDocumentNode.SelectionSet.Selections, runtimeConfigProvider);
                    return;
                }

                if (node.Kind == SyntaxKind.InlineFragment)
                {
                    InlineFragmentNode inlineFragment = (InlineFragmentNode)node;
                    AddGraphQLFields(inlineFragment.SelectionSet.Selections, runtimeConfigProvider);
                    return;
                }

                if (node.Kind != SyntaxKind.Field)
                {
                    throw new DataApiBuilderException(
                        $"The current node has a SyntaxKind of {node.Kind} which is unsupported.",
                        HttpStatusCode.InternalServerError,
                        DataApiBuilderException.SubStatusCodes.NotSupported);
                }

                FieldNode field = (FieldNode)node;
                string fieldName = field.Name.Value;

                // Do not add reserved introspection fields prefixed with "__" to the SqlQueryStructure because those fields are handled by HotChocolate.
                bool isIntrospectionField = GraphQLNaming.IsIntrospectionField(field.Name.Value);
                if (isIntrospectionField)
                {
                    continue;
                }

                if (field.SelectionSet is null)
                {
                    if (MetadataProvider.TryGetBackingColumn(EntityName, fieldName, out string? name)
                        && !string.IsNullOrWhiteSpace(name))
                    {
                        AddColumn(columnName: name, labelName: fieldName);
                    }
                    else
                    {
                        AddColumn(fieldName);
                    }
                }
                else
                {
                    IObjectField? subschemaField = _underlyingFieldType.Fields[fieldName];

                    if (_ctx == null)
                    {
                        throw new DataApiBuilderException(
                            message: "No GraphQL context exists",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                    }

                    IDictionary<string, object?> subqueryParams = ExecutionHelper.GetParametersFromSchemaAndQueryFields(subschemaField, field, _ctx.Variables);
                    SqlQueryStructure subquery = new(
                        _ctx,
                        subqueryParams,
                        MetadataProvider,
                        AuthorizationResolver,
                        subschemaField,
                        field,
                        Counter,
                        runtimeConfigProvider,
                        GraphQLFilterParser);

                    if (PaginationMetadata.IsPaginated)
                    {
                        // add the subquery metadata as children of items instead of the pagination metadata
                        // object of this structure which is associated with the pagination query itself
                        PaginationMetadata.Subqueries[QueryBuilder.PAGINATION_FIELD_NAME].Subqueries.Add(fieldName, subquery.PaginationMetadata);
                    }
                    else
                    {
                        PaginationMetadata.Subqueries.Add(fieldName, subquery.PaginationMetadata);
                    }

                    // pass the parameters of the subquery to the current query so upmost query has all the
                    // parameters of the query tree and it can pass them to the database query executor
                    foreach (KeyValuePair<string, DbConnectionParam> parameter in subquery.Parameters)
                    {
                        Parameters.Add(parameter.Key, parameter.Value);
                    }

                    // use the _underlyingType from the subquery which will be overridden appropriately if the query is paginated
                    ObjectType subunderlyingType = subquery._underlyingFieldType;
                    string targetEntityName = MetadataProvider.GetEntityName(subunderlyingType.Name);
                    string subtableAlias = subquery.SourceAlias;

                    AddJoinPredicatesForRelatedEntity(targetEntityName, subtableAlias, subquery);

                    string subqueryAlias = $"{subtableAlias}_subq";
                    JoinQueries.Add(subqueryAlias, subquery);
                    Columns.Add(new LabelledColumn(tableSchema: subquery.DatabaseObject.SchemaName,
                              tableName: subquery.DatabaseObject.Name,
                              columnName: DATA_IDENT,
                              label: fieldName,
                              tableAlias: subqueryAlias));
                }
            }
        }

        /// <summary>
        /// The maximum number of results this query should return.
        /// </summary>
        public uint? Limit()
        {
            if (IsListQuery || PaginationMetadata.IsPaginated)
            {
                return _limit;
            }
            else
            {
                return 1;
            }
        }

        /// <summary>
        /// Create a list of orderBy columns from the orderBy argument
        /// passed to the gql query. The orderBy argument could contain mapped field names
        /// so we find their backing column names before creating the orderBy list.
        /// All the remaining primary key columns are also added to ensure there are no tie breaks.
        /// </summary>
        private List<OrderByColumn> ProcessGqlOrderByArg(List<ObjectFieldNode> orderByFields, IInputField orderByArgumentSchema)
        {
            if (_ctx is null)
            {
                throw new ArgumentNullException("IMiddlewareContext should be initialized before " +
                                                "trying to parse the orderBy argument.");
            }

            // Create list of primary key columns
            // we always have the primary keys in
            // the order by statement for the case
            // of tie breaking and pagination
            List<OrderByColumn> orderByColumnsList = new();

            HashSet<string> remainingPkCols = new(PrimaryKey());

            InputObjectType orderByArgumentObject = ExecutionHelper.InputObjectTypeFromIInputField(orderByArgumentSchema);
            foreach (ObjectFieldNode field in orderByFields)
            {
                object? fieldValue = ExecutionHelper.ExtractValueFromIValueNode(
                    value: field.Value,
                    argumentSchema: orderByArgumentObject.Fields[field.Name.Value],
                    variables: _ctx.Variables);

                if (fieldValue is null)
                {
                    continue;
                }

                string fieldName = field.Name.ToString();

                if (!MetadataProvider.TryGetBackingColumn(EntityName, fieldName, out string? backingColumnName))
                {
                    throw new DataApiBuilderException(message: "Mapped fieldname could not be found.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
                }

                // remove pk column from list if it was specified as a
                // field in orderBy
                remainingPkCols.Remove(backingColumnName);

                if (fieldValue.ToString() == $"{OrderBy.DESC}")
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                             tableName: DatabaseObject.Name,
                                                             columnName: backingColumnName,
                                                             tableAlias: SourceAlias,
                                                             direction: OrderBy.DESC));
                }
                else
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                             tableName: DatabaseObject.Name,
                                                             columnName: backingColumnName,
                                                             tableAlias: SourceAlias));
                }
            }

            foreach (string colName in remainingPkCols)
            {
                orderByColumnsList.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                         tableName: DatabaseObject.Name,
                                                         columnName: colName,
                                                         tableAlias: SourceAlias));
            }

            return orderByColumnsList;
        }

        /// <summary>
        /// Adds a labelled column to this query's columns, where
        /// the column name is all that is provided, and we add
        /// a labeled column with a label equal to column name.
        /// </summary>
        protected void AddColumn(string columnName)
        {
            AddColumn(columnName, columnName);
        }

        /// <summary>
        /// Adds a labelled column to this query's columns.
        /// <param name="columnName">The backing column name.</param>
        /// <param name="labelName">The exposed name.</param>
        /// </summary>
        protected void AddColumn(string columnName, string labelName)
        {
            LabelledColumn column = new(DatabaseObject.SchemaName, DatabaseObject.Name, columnName, label: labelName, SourceAlias);
            if (!Columns.Contains(column))
            {
                Columns.Add(column);
            }
        }

        /// <summary>
        /// End Cursor consists of primary keys and order by columns.
        /// It is formed using the column values from the last row returned as a JsonElement.
        /// Hence, in addition to the requested fields, we need to add any extraneous primary keys
        /// and order by columns to the list of columns in the select clause.
        /// When adding to the columns of the select clause, we make sure to use exposed column names as the label.
        /// </summary>
        private void AddColumnsForEndCursor()
        {
            // add the primary keys in the selected columns if they are missing
            IEnumerable<string> primaryKeyExtraColumns = PrimaryKey().Except(Columns.Select(c => c.ColumnName));

            foreach (string column in primaryKeyExtraColumns)
            {
                MetadataProvider.TryGetExposedColumnName(EntityName, column, out string? exposedColumnName);
                AddColumn(column, labelName: exposedColumnName!);
            }

            // Add any other left over orderBy columns to the select clause apart from those
            // already selected and apart from the extra primary keys.
            IEnumerable<string> orderByExtraColumns =
                OrderByColumns.Select(orderBy => orderBy.ColumnName).Except(Columns.Select(c => c.ColumnName));

            foreach (string column in orderByExtraColumns)
            {
                MetadataProvider.TryGetExposedColumnName(EntityName, column, out string? exposedColumnName);
                AddColumn(column, labelName: exposedColumnName!);
            }
        }

        /// <summary>
        /// Check if the column belongs to one of the subqueries
        /// </summary>
        public bool IsSubqueryColumn(Column column)
        {
            return column.TableAlias == null ? false : JoinQueries.ContainsKey(column.TableAlias);
        }

        /// <summary>
        /// Add column label string literals as parameters to the query structure
        /// </summary>
        private void ParametrizeColumns()
        {
            foreach (LabelledColumn column in Columns)
            {
                ColumnLabelToParam.Add(column.Label, $"{MakeDbConnectionParam(column.Label)}");
            }
        }
    }
}
