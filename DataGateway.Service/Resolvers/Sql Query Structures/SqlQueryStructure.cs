using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Resolvers
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
        /// All tables that should be in the FROM clause of the query. The key
        /// is the alias of the table and the value is the actual table name.
        /// </summary>
        public List<SqlJoinStructure> Joins { get; }

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
        IResolverContext? _ctx;

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
            IResolverContext ctx,
            IDictionary<string, object?> queryParams,
            ISqlMetadataProvider sqlMetadataProvider)
            // This constructor simply forwards to the more general constructor
            // that is used to create GraphQL queries. We give it some values
            // that make sense for the outermost query.
            : this(ctx,
                queryParams,
                sqlMetadataProvider,
                ctx.Selection.Field,
                ctx.Selection.SyntaxNode,
                // The outermost query is where we start, so this can define
                // create the IncrementingInteger that will be shared between
                // all subqueries in this query.
                new IncrementingInteger())
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
            ISqlMetadataProvider sqlMetadataProvider) :
            this(sqlMetadataProvider,
                new IncrementingInteger(),
                entityName: context.EntityName)
        {
            IsListQuery = context.IsMany;
            TableAlias = $"{DatabaseObject.SchemaName}_{DatabaseObject.Name}";
            AddFields(context);
            if (Columns.Count == 0)
            {
                TableDefinition tableDefinition = GetUnderlyingTableDefinition();
                foreach (KeyValuePair<string, ColumnDefinition> column in tableDefinition.Columns)
                {
                    // if mapping is null or doesnt have our column just use column name
                    if (context.MappingFromEntity is null || !context.MappingFromEntity.ContainsKey(column.Key))
                    {
                        AddColumn(column.Key);

                    }
                    // otherwise the mapping must contain the label to use
                    else
                    {
                        AddColumn(column.Key, context.MappingFromEntity[column.Key]);
                    }
                }
            }

            foreach (KeyValuePair<string, object> predicate in context.PrimaryKeyValuePairs)
            {
                PopulateParamsAndPredicates(field: predicate.Key, value: predicate.Value);
            }

            foreach (KeyValuePair<string, object?> predicate in context.FieldValuePairsInBody)
            {
                PopulateParamsAndPredicates(field: predicate.Key, value: predicate.Value);
            }

            // context.OrderByColumnsInUrl will lack TableAlias because it is created in RequestParser
            // which may be called for any type of operation. To avoid coupling the OrderByClauseInUrl
            // to only Find, we populate the TableAlias in this constructor where we know we have a Find operation.
            OrderByColumns = context.OrderByClauseInUrl is not null ? context.OrderByClauseInUrl : PrimaryKeyAsOrderByColumns();
            foreach (OrderByColumn column in OrderByColumns)
            {
                if (string.IsNullOrEmpty(column.TableAlias))
                {
                    column.TableAlias = TableAlias;
                }
            }

            if (context.FilterClauseInUrl is not null)
            {
                // We use the visitor pattern here to traverse the Filter Clause AST
                // AST has Accept method which takes our Visitor class, and then calls
                // our visit functions. Each node in the AST will then automatically
                // call the visit function for that node types, and we process the AST
                // based on what type of node we are currently traversing.
                ODataASTVisitor visitor = new(this);
                try
                {
                    FilterPredicates = context.FilterClauseInUrl.Expression.Accept<string>(visitor);
                }
                catch
                {
                    throw new DataGatewayException(message: "$filter query parameter is not well formed.",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }
            }

            if (!string.IsNullOrWhiteSpace(context.After))
            {
                AddPaginationPredicate(SqlPaginationUtil.ParseAfterFromJsonString(context.After, PaginationMetadata));
            }

            _limit = context.First is not null ? context.First + 1 : DEFAULT_LIST_LIMIT + 1;
            ParametrizeColumns();
        }

        /// <summary>
        /// For fields specifically selected, we check
        /// to see if the field is from the mapping from
        /// the target entity or not, and then add the
        /// column with the appropriate name and label.
        /// </summary>
        /// <param name="context"></param>
        private void AddFields(RestRequestContext context)
        {
            Dictionary<string, string> reverseMapping = context.ReversedMappingFromEntity!;

            foreach (string field in context.FieldsToBeReturned)
            {
                // we know fields to be returned are valid,
                // so if field exists in reverseMapping
                // it must be the case that we have a column
                // name with a mapping, and so we add that column
                // but with the appropriate name and mapped label.
                if (reverseMapping is not null && reverseMapping.ContainsKey(field))
                {
                    AddColumn(reverseMapping[field], field);
                }
                // otherwise this must simply be a column with
                // no associated mapping. Just add the column.
                else
                {
                    AddColumn(field);
                }
            }
        }

        /// <summary>
        /// Private constructor that is used for recursive query generation,
        /// for each subquery that's necessary to resolve a nested GraphQL
        /// request.
        /// </summary>
        private SqlQueryStructure(
                IResolverContext ctx,
                IDictionary<string, object?> queryParams,
                ISqlMetadataProvider sqlMetadataProvider,
                IObjectField schemaField,
                FieldNode? queryField,
                IncrementingInteger counter,
                string entityName = ""
        ) : this(sqlMetadataProvider, counter, entityName: entityName)
        {
            _ctx = ctx;
            IOutputType outputType = schemaField.Type;
            _underlyingFieldType = UnderlyingGraphQLEntityType(outputType);

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
                _underlyingFieldType = UnderlyingGraphQLEntityType(outputType);

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
                PaginationMetadata.Subqueries.Add("items", PaginationMetadata.MakeEmptyPaginationMetadata());
            }

            EntityName = _underlyingFieldType.Name;
            DatabaseObject.SchemaName = sqlMetadataProvider.GetSchemaName(EntityName);
            DatabaseObject.Name = sqlMetadataProvider.GetDatabaseObjectName(EntityName);
            TableAlias = CreateTableAlias();

            if (queryField != null && queryField.SelectionSet != null)
            {
                AddGraphQLFields(queryField.SelectionSet.Selections);
            }

            if (outputType.IsNonNullType())
            {
                IsListQuery = outputType.InnerType().IsListType();
            }
            else
            {
                IsListQuery = outputType.IsListType();
            }

            if (IsListQuery && queryParams.ContainsKey("first"))
            {
                // parse first parameter for all list queries
                object? firstObject = queryParams["first"];

                if (firstObject != null)
                {
                    int first = (int)firstObject;

                    if (first <= 0)
                    {
                        throw new DataGatewayException(
                        message: $"Invalid number of items requested, $first must be an integer greater than 0 for {schemaField.Name}. Actual value: {first.ToString()}",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                    }

                    _limit = (uint)first;
                }
            }

            if (IsListQuery && queryParams.ContainsKey("_filter"))
            {
                object? filterObject = queryParams["_filter"];

                if (filterObject != null)
                {
                    List<ObjectFieldNode> filterFields = (List<ObjectFieldNode>)filterObject;
                    Predicates.Add(GQLFilterParser.Parse(fields: filterFields,
                                                         schemaName: DatabaseObject.SchemaName,
                                                         tableName: DatabaseObject.Name,
                                                         tableAlias: TableAlias,
                                                         table: GetUnderlyingTableDefinition(),
                                                         processLiterals: MakeParamWithValue));
                }
            }

            OrderByColumns = PrimaryKeyAsOrderByColumns();
            if (IsListQuery && queryParams.ContainsKey("orderBy"))
            {
                object? orderByObject = queryParams["orderBy"];

                if (orderByObject != null)
                {
                    OrderByColumns = ProcessGqlOrderByArg((List<ObjectFieldNode>)orderByObject);
                }
            }

            if (IsListQuery && queryParams.ContainsKey("_filterOData"))
            {
                object? whereObject = queryParams["_filterOData"];

                if (whereObject != null)
                {
                    string where = (string)whereObject;

                    ODataASTVisitor visitor = new(this);
                    FilterParser parser = SqlMetadataProvider.ODataFilterParser;
                    FilterClause filterClause = parser.GetFilterClause($"?{RequestParser.FILTER_URL}={where}", $"{DatabaseObject.FullName}");
                    FilterPredicates = filterClause.Expression.Accept<string>(visitor);
                }
            }

            // need to run after the rest of the query has been processed since it relies on
            // TableName, TableAlias, Columns, and _limit
            if (PaginationMetadata.IsPaginated)
            {
                AddPaginationPredicate(SqlPaginationUtil.ParseAfterFromQueryParams(queryParams, PaginationMetadata));

                if (PaginationMetadata.RequestedEndCursor)
                {
                    // add the primary keys in the selected columns if they are missing
                    IEnumerable<string> extraNeededColumns = PrimaryKey().Except(Columns.Select(c => c.Label));

                    foreach (string column in extraNeededColumns)
                    {
                        AddColumn(column);
                    }
                }

                // if the user does a paginated query only requesting hasNextPage
                // there will be no elements in Columns
                if (!Columns.Any())
                {
                    AddColumn(PrimaryKey()[0]);
                }

                if (PaginationMetadata.RequestedHasNextPage)
                {
                    _limit++;
                }
            }

            ParametrizeColumns();
        }

        /// <summary>
        /// Private constructor that is used as a base by all public
        /// constructors.
        /// </summary>
        private SqlQueryStructure(
            ISqlMetadataProvider sqlMetadataProvider,
            IncrementingInteger counter,
            string entityName = "")
            : base(sqlMetadataProvider, entityName: entityName, counter: counter)
        {
            JoinQueries = new();
            Joins = new();
            PaginationMetadata = new(this);
            ColumnLabelToParam = new();
            FilterPredicates = string.Empty;
            OrderByColumns = new();
        }

        ///<summary>
        /// Adds predicates for the primary keys in the paramters of the graphql query
        ///</summary>
        private void AddPrimaryKeyPredicates(IDictionary<string, object?> queryParams)
        {
            foreach (KeyValuePair<string, object?> parameter in queryParams)
            {
                Predicates.Add(new Predicate(
                    new PredicateOperand(new Column(tableSchema: DatabaseObject.SchemaName,
                                                    tableName: DatabaseObject.Name,
                                                    columnName: parameter.Key,
                                                    tableAlias: TableAlias)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(parameter.Value)}")
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

            try
            {
                foreach (PaginationColumn column in afterJsonValues)
                {
                    column.TableAlias = TableAlias;
                    column.ParamName = "@" + MakeParamWithValue(
                            GetParamAsColumnSystemType(column.Value!.ToString()!, column.ColumnName));
                }
            }
            catch (ArgumentException ex)
            {
                throw new DataGatewayException(
                  message: ex.Message,
                  statusCode: HttpStatusCode.BadRequest,
                  subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            PaginationMetadata.PaginationPredicate = new KeysetPaginationPredicate(afterJsonValues.ToList());
        }

        /// <summary>
        ///  Given the predicate key value pair, where value includes the Predicte Operation as well as the value associated with the field,
        ///  populates the Parameters and Predicates properties.
        /// </summary>
        /// <param name="field">The string representing a field.</param>
        /// <param name="value">The value associated with a given field.</param>
        /// <param name="op">The predicate operation representing the comparison between field and value.</param>
        private void PopulateParamsAndPredicates(string field, object? value, PredicateOperation op = PredicateOperation.Equal)
        {
            try
            {
                string parameterName;
                if (value != null)
                {
                    parameterName = MakeParamWithValue(
                        GetParamAsColumnSystemType(value.ToString()!, field));
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(DatabaseObject.SchemaName, DatabaseObject.Name, field, TableAlias)),
                        op,
                        new PredicateOperand($"@{parameterName}")));
                }
                else
                {
                    // This case should not arise. We have issue for this to handle nullable type columns. Issue #146.
                    throw new DataGatewayException(
                        message: $"Unexpected value for column \"{field}\" provided.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }
            }
            catch (ArgumentException ex)
            {
                throw new DataGatewayException(
                  message: ex.Message,
                  statusCode: HttpStatusCode.BadRequest,
                  subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Creates equality predicates between the columns of the left table and
        /// the columns of the right table. The columns are compared in order,
        /// thus the lists should be the same length.
        /// </summary>
        public IEnumerable<Predicate> CreateJoinPredicates(
                string leftTableAlias,
                List<string> leftColumnNames,
                string rightTableAlias,
                List<string> rightColumnNames)
        {
            return leftColumnNames.Zip(rightColumnNames,
                    (leftColumnName, rightColumnName) =>
                    {
                        // no table name or schema here is needed because this is a subquery that joins on table alias
                        Column leftColumn = new(tableSchema: string.Empty, tableName: string.Empty, leftColumnName, leftTableAlias);
                        Column rightColumn = new(tableSchema: string.Empty, tableName: string.Empty, rightColumnName, rightTableAlias);
                        return new Predicate(
                            new PredicateOperand(leftColumn),
                            PredicateOperation.Equal,
                            new PredicateOperand(rightColumn)
                        );
                    }
                );
        }

        /// <summary>
        /// Creates a unique table alias.
        /// </summary>
        public string CreateTableAlias()
        {
            return $"table{Counter.Next()}";
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
        /// to the result set, but also adding any subqueries or joins that are
        /// required to fetch nested data.
        /// </summary>
        private void AddGraphQLFields(IReadOnlyList<ISelectionNode> Selections)
        {
            foreach (ISelectionNode node in Selections)
            {
                FieldNode field = (FieldNode)node;
                string fieldName = field.Name.Value;

                if (field.SelectionSet == null)
                {
                    AddColumn(fieldName);
                }
                else
                {
                    IObjectField? subschemaField = _underlyingFieldType.Fields[fieldName];

                    if (_ctx == null)
                    {
                        throw new DataGatewayException("No GraphQL context exists", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.UnexpectedError);
                    }

                    IDictionary<string, object?> subqueryParams = ResolverMiddleware.GetParametersFromSchemaAndQueryFields(subschemaField, field, _ctx.Variables);

                    SqlQueryStructure subquery = new(_ctx, subqueryParams, SqlMetadataProvider, subschemaField, field, Counter);

                    if (PaginationMetadata.IsPaginated)
                    {
                        // add the subquery metadata as children of items instead of the pagination metadata
                        // object of this structure which is associated with the pagination query itself
                        PaginationMetadata.Subqueries["items"].Subqueries.Add(fieldName, subquery.PaginationMetadata);
                    }
                    else
                    {
                        PaginationMetadata.Subqueries.Add(fieldName, subquery.PaginationMetadata);
                    }

                    // pass the parameters of the subquery to the current query so upmost query has all the
                    // parameters of the query tree and it can pass them to the database query executor
                    foreach (KeyValuePair<string, object?> parameter in subquery.Parameters)
                    {
                        Parameters.Add(parameter.Key, parameter.Value);
                    }

                    // use the _underlyingType from the subquery which will be overridden appropriately if the query is paginated
                    ObjectType subunderlyingType = subquery._underlyingFieldType;
                    string targetEntityName = subunderlyingType.Name;
                    string subtableAlias = subquery.TableAlias;

                    AddJoinPredicatesForSubQuery(targetEntityName, subtableAlias, subquery);

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
        /// Based on the relationship metadata involving foreign key referenced and
        /// referencing columns, add the join predicates to the subquery Query structure
        /// created for the given target entity Name and sub table alias.
        /// There are only a couple of options for the foreign key - we only use the
        /// valid foreign key definition. It is guaranteed at least one fk definition
        /// will be valid since the SqlMetadataProvider.ValidateAllFkHaveBeenInferred.
        /// </summary>
        /// <param name="targetEntityName"></param>
        /// <param name="subtableAlias"></param>
        /// <param name="subQuery"></param>
        private void AddJoinPredicatesForSubQuery(
            string targetEntityName,
            string subtableAlias,
            SqlQueryStructure subQuery)
        {
            TableDefinition tableDefinition = GetUnderlyingTableDefinition();
            if (tableDefinition.SourceEntityRelationshipMap.TryGetValue(
                _underlyingFieldType.Name, out RelationshipMetadata? relationshipMetadata)
                && relationshipMetadata.TargetEntityToFkDefinitionMap.TryGetValue(targetEntityName,
                    out List<ForeignKeyDefinition>? foreignKeyDefinitions))
            {
                Dictionary<DatabaseObject, string> associativeTableAndAliases = new();
                // For One-One and One-Many, not all fk definitions would be valid
                // but at least 1 will be.
                // Identify the side of the relationship first, then check if its valid
                // by ensuring the referencing and referenced column count > 0
                // before adding the predicates.
                foreach (ForeignKeyDefinition foreignKeyDefinition in foreignKeyDefinitions)
                {
                    // First identify which side of the relationship, this fk definition
                    // is looking at.
                    if (foreignKeyDefinition.Pair.ReferencingDbObject.Equals(DatabaseObject))
                    {
                        // Case where fk in parent entity references the nested entity.
                        // Verify this is a valid fk definition before adding the join predicate.
                        if (foreignKeyDefinition.ReferencingColumns.Count() > 0
                            && foreignKeyDefinition.ReferencedColumns.Count() > 0)
                        {
                            subQuery.Predicates.AddRange(CreateJoinPredicates(
                                TableAlias,
                                foreignKeyDefinition.ReferencingColumns,
                                subtableAlias,
                                foreignKeyDefinition.ReferencedColumns));
                        }
                    }
                    else if (foreignKeyDefinition.Pair.ReferencingDbObject.Equals(subQuery.DatabaseObject))
                    {
                        // Case where fk in nested entity references the parent entity.
                        if (foreignKeyDefinition.ReferencingColumns.Count() > 0
                            && foreignKeyDefinition.ReferencedColumns.Count() > 0)
                        {
                            subQuery.Predicates.AddRange(CreateJoinPredicates(
                                subtableAlias,
                                foreignKeyDefinition.ReferencingColumns,
                                TableAlias,
                                foreignKeyDefinition.ReferencedColumns));
                        }
                    }
                    else
                    {
                        DatabaseObject associativeTableDbObject =
                            foreignKeyDefinition.Pair.ReferencingDbObject;
                        // Case when the linking object is the referencing table
                        if (!associativeTableAndAliases.TryGetValue(
                                associativeTableDbObject,
                                out string? associativeTableAlias))
                        {
                            // this is the first fk definition found for this associative table.
                            // create an alias for it and store for later lookup.
                            associativeTableAlias = CreateTableAlias();
                            associativeTableAndAliases.Add(associativeTableDbObject, associativeTableAlias);
                        }

                        if (foreignKeyDefinition.Pair.ReferencedDbObject.Equals(DatabaseObject))
                        {
                            subQuery.Predicates.AddRange(CreateJoinPredicates(
                                associativeTableAlias,
                                foreignKeyDefinition.ReferencingColumns,
                                TableAlias,
                                foreignKeyDefinition.ReferencedColumns));
                        }
                        else
                        {
                            subQuery.Joins.Add(new SqlJoinStructure
                            (
                                associativeTableDbObject,
                                associativeTableAlias,
                                CreateJoinPredicates(
                                    associativeTableAlias,
                                    foreignKeyDefinition.ReferencingColumns,
                                    subtableAlias,
                                    foreignKeyDefinition.ReferencedColumns
                                    ).ToList()
                            ));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Create a list of orderBy columns from the orderBy argument
        /// passed to the gql query
        /// </summary>
        private List<OrderByColumn> ProcessGqlOrderByArg(List<ObjectFieldNode> orderByFields)
        {
            // Create list of primary key columns
            // we always have the primary keys in
            // the order by statement for the case
            // of tie breaking and pagination
            List<OrderByColumn> orderByColumnsList = new();

            List<string> remainingPkCols = new(PrimaryKey());

            foreach (ObjectFieldNode field in orderByFields)
            {
                if (field.Value is NullValueNode)
                {
                    continue;
                }

                string fieldName = field.Name.ToString();

                // remove pk column from list if it was specified as a
                // field in orderBy
                remainingPkCols.Remove(fieldName);

                EnumValueNode enumValue = (EnumValueNode)field.Value;

                if (enumValue.Value == $"{OrderByDir.Desc}")
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                             tableName: DatabaseObject.Name,
                                                             columnName: fieldName,
                                                             tableAlias: TableAlias,
                                                             direction: OrderByDir.Desc));
                }
                else
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                             tableName: DatabaseObject.Name,
                                                             columnName: fieldName,
                                                             tableAlias: TableAlias));
                }
            }

            foreach (string colName in remainingPkCols)
            {
                orderByColumnsList.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                         tableName: DatabaseObject.Name,
                                                         columnName: colName,
                                                         tableAlias: TableAlias));
            }

            return orderByColumnsList;
        }

        /// <summary>
        /// Exposes the primary key of the underlying table of the structure
        /// as a list of OrderByColumn
        /// </summary>
        public List<OrderByColumn> PrimaryKeyAsOrderByColumns()
        {
            if (_primaryKeyAsOrderByColumns == null)
            {
                _primaryKeyAsOrderByColumns = new();

                foreach (string column in PrimaryKey())
                {
                    _primaryKeyAsOrderByColumns.Add(new OrderByColumn(tableSchema: DatabaseObject.SchemaName,
                                                                      tableName: DatabaseObject.Name,
                                                                      columnName: column,
                                                                      tableAlias: TableAlias));
                }
            }

            return _primaryKeyAsOrderByColumns;
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
        /// </summary>
        protected void AddColumn(string columnName, string labelName)
        {
            Columns.Add(new LabelledColumn(DatabaseObject.SchemaName, DatabaseObject.Name, columnName, label: labelName, TableAlias));
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
                ColumnLabelToParam.Add(column.Label, $"@{MakeParamWithValue(column.Label)}");
            }
        }
    }
}
