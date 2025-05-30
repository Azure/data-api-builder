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
using static Azure.DataApiBuilder.Service.GraphQLBuilder.Sql.SchemaConverter;
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
        /// The maximum number of results this query should return.
        /// </summary>
        private uint? _limit = PaginationOptions.DEFAULT_PAGE_SIZE;

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
        /// Indicates whether the SqlQueryStructure is constructed for
        /// a multiple create mutation operation.
        /// </summary>
        public bool IsMultipleCreateOperation;

        /// <summary>
        /// Hold the groupBy metadata for the query
        /// </summary>
        public GroupByMetadata GroupByMetadata { get; private set; }

        public virtual string? CacheControlOption { get; set; }

        public const string CACHE_CONTROL = "Cache-Control";

        public const string CACHE_CONTROL_NO_STORE = "no-store";

        public const string CACHE_CONTROL_NO_CACHE = "no-cache";

        public const string CACHE_CONTROL_ONLY_IF_CACHED = "only-if-cached";

        public HashSet<string> cacheControlHeaderOptions = new(
            new[] { CACHE_CONTROL_NO_STORE, CACHE_CONTROL_NO_CACHE, CACHE_CONTROL_ONLY_IF_CACHED },
            StringComparer.OrdinalIgnoreCase);

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
        /// Generate the structure for a SQL query based on GraphQL query
        /// information. This is used to construct the follow-up query
        /// for a many-type multiple create mutation.
        /// This constructor accepts a list of query parameters as opposed to a single query parameter
        /// like the other constructors for SqlQueryStructure.
        /// For constructing the follow-up query of a many-type multiple create mutation, the primary keys
        /// of the created items in the top level entity will be passed as the query parameters.
        /// </summary>
        public SqlQueryStructure(
            IMiddlewareContext ctx,
            List<IDictionary<string, object?>> queryParams,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            RuntimeConfigProvider runtimeConfigProvider,
            GQLFilterParser gQLFilterParser,
            IncrementingInteger counter,
            string entityName = "",
            bool isMultipleCreateOperation = false)
            : this(sqlMetadataProvider,
                  authorizationResolver,
                  gQLFilterParser,
                  gQLFilterParser.GetHttpContextFromMiddlewareContext(ctx).Request.Headers,
                  predicates: null,
                  entityName: entityName,
                  counter: counter)
        {
            _ctx = ctx;
            IsMultipleCreateOperation = isMultipleCreateOperation;

            IObjectField schemaField = _ctx.Selection.Field;
            FieldNode? queryField = _ctx.Selection.SyntaxNode;

            IOutputType outputType = schemaField.Type;
            _underlyingFieldType = outputType.NamedType<ObjectType>();

            PaginationMetadata.IsPaginated = QueryBuilder.IsPaginationType(_underlyingFieldType);

            if (PaginationMetadata.IsPaginated)
            {
                if (queryField != null && queryField.SelectionSet != null)
                {
                    // process pagination fields without overriding them
                    ProcessPaginationFields(queryField.SelectionSet.Selections);

                    // override schemaField and queryField with the schemaField and queryField of *Connection.items
                    queryField = ExtractQueryField(queryField);
                }

                schemaField = ExtractItemsSchemaField(schemaField);

                outputType = schemaField.Type;
                _underlyingFieldType = outputType.NamedType<ObjectType>();

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

            EntityName = _underlyingFieldType.Name;

            if (GraphQLUtils.TryExtractGraphQLFieldModelName(_underlyingFieldType.Directives, out string? modelName))
            {
                EntityName = modelName;
            }

            DatabaseObject.SchemaName = sqlMetadataProvider.GetSchemaName(EntityName);
            DatabaseObject.Name = sqlMetadataProvider.GetDatabaseObjectName(EntityName);
            SourceAlias = CreateTableAlias();

            // support identification of entities by primary key when query is non list type nor paginated
            // only perform this action for the outermost query as subqueries shouldn't provide primary key search
            AddPrimaryKeyPredicates(queryParams);

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

            OrderByColumns = PrimaryKeyAsOrderByColumns();

            // If there are no columns, add the primary key column
            // to prevent failures when executing the database query.
            if (!Columns.Any())
            {
                AddColumn(PrimaryKey()[0]);
            }

            ParametrizeColumns();
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
                httpRequestHeaders: httpContext?.Request.Headers,
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
            runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig);
            _limit = runtimeConfig?.GetPaginationLimit((int?)context.First) + 1;

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
                  gQLFilterParser.GetHttpContextFromMiddlewareContext(ctx).Request.Headers,
                  predicates: null,
                  entityName: entityName,
                  counter: counter)
        {
            _ctx = ctx;
            IOutputType outputType = schemaField.Type;
            _underlyingFieldType = outputType.NamedType<ObjectType>();

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
                    queryField = ExtractQueryField(queryField);
                }

                schemaField = ExtractItemsSchemaField(schemaField);

                outputType = schemaField.Type;
                _underlyingFieldType = outputType.NamedType<ObjectType>();

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
            bool isGroupByQuery = queryField?.Name.Value == QueryBuilder.GROUP_BY_FIELD_NAME;

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
                if (isGroupByQuery)
                {
                    ProcessGroupByField(queryField, ctx);
                }
                else
                {
                    AddGraphQLFields(queryField.SelectionSet.Selections, runtimeConfigProvider);
                }
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

            if (IsListQuery)
            {
                runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig);
                if (queryParams.ContainsKey(QueryBuilder.PAGE_START_ARGUMENT_NAME))
                {
                    // parse first parameter for all list queries
                    object? firstObject = queryParams[QueryBuilder.PAGE_START_ARGUMENT_NAME];
                    _limit = runtimeConfig?.GetPaginationLimit((int?)firstObject);
                }
                else
                {
                    // if first is not passed, we should use the default page size.
                    _limit = runtimeConfig?.DefaultPageSize();
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

            // primary key should only be added to order by for non groupby queries.
            OrderByColumns = isGroupByQuery ? [] : PrimaryKeyAsOrderByColumns();
            if (IsListQuery && queryParams.ContainsKey(QueryBuilder.ORDER_BY_FIELD_NAME))
            {
                object? orderByObject = queryParams[QueryBuilder.ORDER_BY_FIELD_NAME];

                if (orderByObject is not null)
                {
                    OrderByColumns = ProcessGqlOrderByArg((List<ObjectFieldNode>)orderByObject, queryArgumentSchemas[QueryBuilder.ORDER_BY_FIELD_NAME], isGroupByQuery);
                }
            }

            // need to run after the rest of the query has been processed since it relies on
            // TableName, SourceAlias, Columns, and _limit
            if (PaginationMetadata.IsPaginated)
            {
                AddPaginationPredicate(SqlPaginationUtil.ParseAfterFromQueryParams(queryParams, PaginationMetadata, sqlMetadataProvider, EntityName, runtimeConfigProvider));

                if (PaginationMetadata.RequestedEndCursor)
                {
                    AddColumnsForEndCursor(isGroupByQuery);
                }

                if (PaginationMetadata.RequestedHasNextPage || PaginationMetadata.RequestedEndCursor)
                {
                    _limit++;
                }
            }

            // If there are no columns, add the primary key column
            // to prevent failures when executing the database query.
            if (!Columns.Any() && !isGroupByQuery)
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
            IHeaderDictionary? httpRequestHeaders,
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
            GroupByMetadata = new();
            ColumnLabelToParam = new();
            FilterPredicates = string.Empty;
            OrderByColumns = new();
            AddCacheControlOptions(httpRequestHeaders);
        }

        private void AddCacheControlOptions(IHeaderDictionary? httpRequestHeaders)
        {
            // Set the cache control based on the request header if it exists.
            if (httpRequestHeaders is not null && httpRequestHeaders.TryGetValue(CACHE_CONTROL, out Microsoft.Extensions.Primitives.StringValues cacheControlOption))
            {
                CacheControlOption = cacheControlOption;
            }

            if (!string.IsNullOrEmpty(CacheControlOption) &&
                !cacheControlHeaderOptions.Contains(CacheControlOption))
            {
                throw new DataApiBuilderException(
                    message: "Request Header Cache-Control is invalid: " + CacheControlOption,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Adds predicates for the primary keys in the parameters of the GraphQL query
        /// </summary>
        private void AddPrimaryKeyPredicates(List<IDictionary<string, object?>> queryParams)
        {
            foreach (IDictionary<string, object?> queryParam in queryParams)
            {
                AddPrimaryKeyPredicates(queryParam, isMultipleCreateOperation: true);
            }
        }

        ///<summary>
        /// Adds predicates for the primary keys in the parameters of the GraphQL query
        ///</summary>
        private void AddPrimaryKeyPredicates(IDictionary<string, object?> queryParams, bool isMultipleCreateOperation = false)
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
                    new PredicateOperand($"{MakeDbConnectionParam(parameter.Value, columnName)}"),
                    addParenthesis: isMultipleCreateOperation
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
                    case QueryBuilder.GROUP_BY_FIELD_NAME:
                        PaginationMetadata.RequestedGroupBy = true;
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
        // TODO : This is inefficient and could lead to errors. we should rewrite this to use the ISelection API.
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
                    DocumentNode document = _ctx.Operation.Document;
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
                    string subqueryTableAlias = subquery.SourceAlias;
                    EntityRelationshipKey currentEntityRelationshipKey = new(EntityName, relationshipName: fieldName);
                    AddJoinPredicatesForRelationship(
                        fkLookupKey: currentEntityRelationshipKey,
                        targetEntityName,
                        subqueryTargetTableAlias: subqueryTableAlias,
                        subquery);

                    string subqueryAlias = $"{subqueryTableAlias}_subq";
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
        /// Processes the groupBy field and populates GroupByMetadata.
        ///
        /// Steps:
        /// 1. Extract the 'fields' argument.
        ///    - For each field argument, add it as a column in the query and to GroupByMetadata.
        /// 2. Process the selections (fields and aggregations).
        ///
        /// Example:
        /// groupBy(fields: [categoryid]) {
        ///   fields {
        ///     categoryid
        ///   }
        ///   aggregations {
        ///     max(field: price, having: { gt: 43 }, distinct: true)
        ///     max2: sum(field: price, having: { gt: 1 })
        ///   }
        /// }
        /// </summary>
        private void ProcessGroupByField(FieldNode groupByField, IMiddlewareContext ctx)
        {
            // Extract 'fields' argument
            ArgumentNode? fieldsArg = groupByField.Arguments.FirstOrDefault(a => a.Name.Value == QueryBuilder.GROUP_BY_FIELDS_FIELD_NAME);
            HashSet<string> fieldsInArgument = new();

            if (fieldsArg is { Value: ListValueNode fieldsList })
            {
                foreach (EnumValueNode value in fieldsList.Items)
                {
                    string fieldName = value.Value;
                    string columnName = MetadataProvider.TryGetBackingColumn(EntityName, fieldName, out string? backingColumn) ? backingColumn : fieldName;

                    GroupByMetadata.Fields[columnName] = new Column(DatabaseObject.SchemaName, DatabaseObject.Name, columnName, SourceAlias);
                    AddColumn(fieldName, backingColumn ?? fieldName);
                    fieldsInArgument.Add(fieldName);
                }
            }

            // Process selections
            if (groupByField.SelectionSet is null)
            {
                return;
            }

            foreach (FieldNode field in groupByField.SelectionSet.Selections.Cast<FieldNode>())
            {
                switch (field.Name.Value)
                {
                    case QueryBuilder.GROUP_BY_FIELDS_FIELD_NAME:
                        GroupByMetadata.RequestedFields = true;
                        ProcessGroupByFieldSelections(field, fieldsInArgument);
                        break;

                    case QueryBuilder.GROUP_BY_AGGREGATE_FIELD_NAME:
                        GroupByMetadata.RequestedAggregations = true;
                        ProcessAggregations(field, ctx);
                        break;
                }
            }
        }

        private void ProcessGroupByFieldSelections(FieldNode groupByFieldSelection, HashSet<string> fieldsInArgument)
        {
            if (groupByFieldSelection.SelectionSet is null)
            {
                return;
            }

            foreach (ISelectionNode node in groupByFieldSelection.SelectionSet.Selections)
            {
                string fieldName = ((FieldNode)node).Name.Value;
                if (!fieldsInArgument.Contains(fieldName))
                {
                    throw new DataApiBuilderException(
                        "Groupby fields in selection must match the fields in the groupby argument.",
                        HttpStatusCode.BadRequest,
                        DataApiBuilderException.SubStatusCodes.BadRequest
                    );
                }

                string columnName = MetadataProvider.TryGetBackingColumn(EntityName, fieldName, out string? backingColumn) ? backingColumn : fieldName;
                AddColumn(fieldName, columnName);
            }

        }

        /// <summary>
        /// Processes the aggregations field in a GraphQL groupBy query and populates the GroupByMetadata.Aggregations property.
        /// This method extracts the aggregation operations (e.g., SUM, AVG) and their corresponding fields from the GraphQL query,
        /// and constructs AggregationColumn objects to represent these operations. It also handles any HAVING clauses associated
        /// with the aggregations, parsing them into predicates.
        /// 1. Extract the aggregation operation and field name from the GraphQL query.
        /// 2. Construct an AggregationColumn object to represent the aggregation operation.
        /// 3. Parse any HAVING clauses associated with the aggregation into predicates.
        /// 4. Add the AggregationColumn object to the GroupByMetadata.Aggregations property.
        /// Example:
        /// aggregations
        /// {
        ///     max(field: price, having: {
        ///     gt:43}, distinct: true)
        ///     max2: sum(field:price, having: { gt:1 })
        /// }
        /// </summary>
        /// <param name="aggregationsField">The FieldNode representing the aggregations field in the GraphQL query.</param>
        /// <param name="ctx"> middleware context.</param>
        private void ProcessAggregations(FieldNode aggregationsField, IMiddlewareContext ctx)
        {
            // If there are no selections in the aggregation field, exit early
            if (aggregationsField.SelectionSet == null)
            {
                return;
            }

            // Retrieve the schema field from the GraphQL context
            IObjectField schemaField = ctx.Selection.Field;

            // Get the 'group by' field from the schema's entity type
            IObjectField groupByField = schemaField.Type.NamedType<ObjectType>()
                .Fields[QueryBuilder.GROUP_BY_FIELD_NAME];

            // Get the 'aggregations' field from the 'group by' entity type
            IObjectField aggregationsObjectField = groupByField.Type.NamedType<ObjectType>()
                .Fields[QueryBuilder.GROUP_BY_AGGREGATE_FIELD_NAME];

            // Iterate through each selection in the aggregation field
            foreach (ISelectionNode selection in aggregationsField.SelectionSet.Selections)
            {
                FieldNode field = (FieldNode)selection;

                // Find the argument specifying which field to aggregate
                ArgumentNode? fieldArg = field.Arguments
                    .FirstOrDefault(a => a.Name.Value == QueryBuilder.GROUP_BY_AGGREGATE_FIELD_ARG_NAME);

                if (fieldArg != null)
                {
                    // Parse the aggregation type (e.g., max, min, avg, etc.)
                    AggregationType operation = Enum.Parse<AggregationType>(field.Name.Value);

                    // Extract the field name from the argument
                    string fieldName = ((EnumValueNode)fieldArg.Value).Value;

                    // Determine if the aggregation should be distinct
                    bool distinct = field.Arguments
                        .FirstOrDefault(a => a.Name.Value == QueryBuilder.GROUP_BY_AGGREGATE_FIELD_DISTINCT_NAME)
                        ?.Value.Value as bool? ?? false;

                    string columnName = fieldName;

                    // If there is a backing column name, use that instead
                    if (MetadataProvider.TryGetBackingColumn(EntityName, fieldName, out string? backingColumn))
                    {
                        columnName = backingColumn;
                        fieldName = backingColumn;
                    }

                    // Use the field alias if provided, otherwise default to the operation name
                    string alias = field.Alias?.Value ?? operation.ToString();

                    // Construct an aggregation column representation
                    AggregationColumn column = new(
                        DatabaseObject.SchemaName,
                        DatabaseObject.Name,
                        columnName,
                        operation,
                        alias,
                        distinct,
                        SourceAlias
                    );

                    // Check if there is a 'having' clause associated with this aggregation
                    ArgumentNode? havingArg = field.Arguments
                        .FirstOrDefault(a => a.Name.Value == QueryBuilder.GROUP_BY_AGGREGATE_FIELD_HAVING_NAME);

                    List<Predicate> predicates = new();

                    if (havingArg is not null)
                    {
                        // Extract filter conditions from the 'having' argument
                        List<ObjectFieldNode> filterFields = (List<ObjectFieldNode>)havingArg.Value.Value!;

                        // Retrieve the corresponding aggregation operation field from the schema
                        IObjectField operationObjectField = aggregationsObjectField.Type.NamedType<ObjectType>()
                            .Fields[operation.ToString()];

                        // Parse the filtering conditions and apply them to the aggregation
                        predicates.Add(
                            FieldFilterParser.Parse(
                                ctx,
                                operationObjectField.Arguments[QueryBuilder.GROUP_BY_AGGREGATE_FIELD_HAVING_NAME],
                                column,
                                filterFields,
                                this.MakeDbConnectionParam
                            )
                        );
                    }

                    GroupByMetadata.Aggregations.Add(new AggregationOperation(column, having: predicates));
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
        private List<OrderByColumn> ProcessGqlOrderByArg(List<ObjectFieldNode> orderByFields, IInputField orderByArgumentSchema, bool isGroupByQuery = false)
        {
            if (_ctx is null)
            {
                throw new ArgumentNullException("IMiddlewareContext should be initialized before trying to parse the orderBy argument.");
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

                // Validate that the orderBy field is present in the groupBy fields if it's a groupBy query
                if (isGroupByQuery && !GroupByMetadata.Fields.ContainsKey(backingColumnName))
                {
                    throw new DataApiBuilderException(message: $"OrderBy field '{fieldName}' must be present in the groupBy fields.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // remove pk column from list if it was specified as a
                // field in orderBy
                remainingPkCols.Remove(backingColumnName);

                OrderBy direction = fieldValue.ToString() == $"{OrderBy.DESC}" ? OrderBy.DESC : OrderBy.ASC;
                orderByColumnsList.Add(new OrderByColumn(
                    tableSchema: DatabaseObject.SchemaName,
                    tableName: DatabaseObject.Name,
                    columnName: backingColumnName,
                    tableAlias: SourceAlias,
                    direction: direction));
            }

            if (!isGroupByQuery)
            {
                // primary key columns to only be used for pagination if not a groupby query
                // TODO: do we need to include primary keys in order by if not specified by user?
                foreach (string colName in remainingPkCols)
                {
                    orderByColumnsList.Add(new OrderByColumn(
                        tableSchema: DatabaseObject.SchemaName,
                        tableName: DatabaseObject.Name,
                        columnName: colName,
                        tableAlias: SourceAlias));
                }
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
        private void AddColumnsForEndCursor(bool isGroupByQuery = false)
        {
            if (!isGroupByQuery)
            {
                // for groupby queries we cannot order by primary key as primary key may not be selected. has to only be with orderby.
                // add the primary keys in the selected columns if they are missing
                IEnumerable<string> primaryKeyExtraColumns = PrimaryKey().Except(Columns.Select(c => c.ColumnName));

                foreach (string column in primaryKeyExtraColumns)
                {
                    MetadataProvider.TryGetExposedColumnName(EntityName, column, out string? exposedColumnName);
                    AddColumn(column, labelName: exposedColumnName!);
                }
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
