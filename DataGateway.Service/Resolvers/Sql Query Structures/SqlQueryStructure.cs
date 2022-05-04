using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
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
        public List<OrderByColumn>? OrderByColumns { get; set; }

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
        /// comment on UnderlyingType to understand what an underlying type is.
        /// </summary>
        ObjectType _underlyingFieldType = null!;

        private readonly GraphQLType _typeInfo = null!;
        private List<Column>? _primaryKey;

        /// <summary>
        /// Generate the structure for a SQL query based on GraphQL query
        /// information.
        /// Only use as constructor for the outermost queries not subqueries
        /// </summary>
        public SqlQueryStructure(
            IResolverContext ctx,
            IDictionary<string, object> queryParams,
            IGraphQLMetadataProvider metadataStoreProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            string entityName = "")
            // This constructor simply forwards to the more general constructor
            // that is used to create GraphQL queries. We give it some values
            // that make sense for the outermost query.
            : this(ctx,
                queryParams,
                metadataStoreProvider,
                sqlMetadataProvider,
                ctx.Selection.Field,
                ctx.Selection.SyntaxNode,
                // The outermost query is where we start, so this can define
                // create the IncrementingInteger that will be shared between
                // all subqueries in this query.
                new IncrementingInteger(),
                entityName: entityName)
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
            IGraphQLMetadataProvider metadataStoreProvider,
            ISqlMetadataProvider sqlMetadataProvider) :
            this(metadataStoreProvider,
                sqlMetadataProvider,
                new IncrementingInteger(), entityName: context.EntityName)
        {
            IsListQuery = context.IsMany;
            TableAlias = $"{SchemaName}_{TableName}";
            context.FieldsToBeReturned.ForEach(fieldName => AddColumn(fieldName));
            if (Columns.Count == 0)
            {
                TableDefinition tableDefinition = GetUnderlyingTableDefinition();
                foreach (KeyValuePair<string, ColumnDefinition> column in tableDefinition.Columns)
                {
                    AddColumn(column.Key);
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

            // OrderByColumns may lack TableAlias 
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
        /// Private constructor that is used for recursive query generation,
        /// for each subquery that's necassery to resolve a nested GraphQL
        /// request.
        /// </summary>
        private SqlQueryStructure(
                IResolverContext ctx,
                IDictionary<string, object> queryParams,
                IGraphQLMetadataProvider metadataStoreProvider,
                ISqlMetadataProvider sqlMetadataProvider,
                IObjectField schemaField,
                FieldNode? queryField,
                IncrementingInteger counter,
                string entityName = ""
        ) : this(metadataStoreProvider, sqlMetadataProvider, counter, entityName: entityName)
        {
            _ctx = ctx;
            IOutputType outputType = schemaField.Type;
            _underlyingFieldType = UnderlyingType(outputType);

            _typeInfo = MetadataStoreProvider.GetGraphQLType(_underlyingFieldType.Name);
            PaginationMetadata.IsPaginated = _typeInfo.IsPaginationType;

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
                _underlyingFieldType = UnderlyingType(outputType);
                _typeInfo = MetadataStoreProvider.GetGraphQLType(_underlyingFieldType.Name);

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

            SchemaName = sqlMetadataProvider.GetSchemaName(_typeInfo.Table);
            TableName = _typeInfo.Table;
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
                    // due to the way parameters get resolved,
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
                    Predicates.Add(GQLFilterParser.Parse(filterFields, SchemaName, TableName, TableAlias, GetUnderlyingTableDefinition(), MakeParamWithValue));
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
                    FilterClause filterClause = parser.GetFilterClause($"?{RequestParser.FILTER_URL}={where}", $"{SchemaName}.{TableName}");
                    FilterPredicates = filterClause.Expression.Accept<string>(visitor);
                }
            }

            OrderByColumns = PrimaryKeyAsOrderByColumns();

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
            IGraphQLMetadataProvider metadataStoreProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            IncrementingInteger counter,
            string entityName = "")
            : base(metadataStoreProvider, sqlMetadataProvider, entityName: entityName, counter: counter)
        {
            JoinQueries = new();
            Joins = new();
            PaginationMetadata = new(this);
            ColumnLabelToParam = new();
            FilterPredicates = string.Empty;
        }

        ///<summary>
        /// Adds predicates for the primary keys in the paramters of the graphql query
        ///</summary>
        private void AddPrimaryKeyPredicates(IDictionary<string, object> queryParams)
        {
            foreach (KeyValuePair<string, object> parameter in queryParams)
            {
                Predicates.Add(new Predicate(
                    new PredicateOperand(new Column(SchemaName, TableName, parameter.Key, TableAlias)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(parameter.Value)}")
                ));
            }
        }

        /// <summary>
        /// Add the predicates associated with the "after" parameter of paginated queries
        /// </summary>
        public void AddPaginationPredicate(List<PaginationColumn> afterJsonValues)
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

            PaginationMetadata.PaginationPredicate = new KeysetPaginationPredicate(afterJsonValues);
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
                        new PredicateOperand(new Column(SchemaName, TableName, field, TableAlias)),
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
                        Column leftColumn = new(tableSchema: null, tableName: null, leftColumnName, leftTableAlias);
                        Column rightColumn = new(tableSchema: null, tableName: null, rightColumnName, rightTableAlias);
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
                    case "items":
                        PaginationMetadata.RequestedItems = true;
                        break;
                    case "endCursor":
                        PaginationMetadata.RequestedEndCursor = true;
                        break;
                    case "hasNextPage":
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
        void AddGraphQLFields(IReadOnlyList<ISelectionNode> Selections)
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

                    IDictionary<string, object> subqueryParams = ResolverMiddleware.GetParametersFromSchemaAndQueryFields(subschemaField, field, _ctx.Variables);

                    SqlQueryStructure subquery = new(_ctx, subqueryParams, MetadataStoreProvider, SqlMetadataProvider, subschemaField, field, Counter);

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

                    // explicitly set to null so it is not used later because this value does not reflect the schema of subquery
                    // if the subquery is paginated since it will be overridden with the schema of *Conntion.items
                    subschemaField = null;

                    // use the _underlyingType from the subquery which will be overridden appropriately if the query is paginated
                    ObjectType subunderlyingType = subquery._underlyingFieldType;

                    GraphQLType subTypeInfo = MetadataStoreProvider.GetGraphQLType(subunderlyingType.Name);
                    TableDefinition subTableDefinition = SqlMetadataProvider.GetTableDefinition(subTypeInfo.Table);
                    GraphQLField fieldInfo = _typeInfo.Fields[fieldName];

                    string subtableAlias = subquery.TableAlias;

                    ForeignKeyDefinition fk;
                    List<string> columns;
                    List<string> subTableColumns;

                    switch (fieldInfo.RelationshipType)
                    {
                        case GraphQLRelationshipType.OneToOne:
                            if (!string.IsNullOrEmpty(fieldInfo.LeftForeignKey))
                            {
                                fk = GetUnderlyingTableDefinition().ForeignKeys[fieldInfo.LeftForeignKey];
                                columns = GetFkColumns(fk, GetUnderlyingTableDefinition());
                                subTableColumns = GetFkRefColumns(fk, subTableDefinition);
                            }
                            else
                            {
                                fk = subTableDefinition.ForeignKeys[fieldInfo.RightForeignKey];
                                columns = GetFkRefColumns(fk, GetUnderlyingTableDefinition());
                                subTableColumns = GetFkColumns(fk, subTableDefinition);
                            }

                            subquery.Predicates.AddRange(CreateJoinPredicates(
                                TableAlias,
                                columns,
                                subtableAlias,
                                subTableColumns
                            ));
                            break;
                        case GraphQLRelationshipType.ManyToOne:
                            fk = GetUnderlyingTableDefinition().ForeignKeys[fieldInfo.LeftForeignKey];
                            columns = GetFkColumns(fk, GetUnderlyingTableDefinition());
                            subTableColumns = GetFkRefColumns(fk, subTableDefinition);

                            subquery.Predicates.AddRange(CreateJoinPredicates(
                                TableAlias,
                                columns,
                                subtableAlias,
                                subTableColumns
                            ));
                            break;
                        case GraphQLRelationshipType.OneToMany:
                            fk = subTableDefinition.ForeignKeys[fieldInfo.RightForeignKey];
                            columns = GetFkRefColumns(fk, GetUnderlyingTableDefinition());
                            subTableColumns = GetFkColumns(fk, subTableDefinition);

                            subquery.Predicates.AddRange(CreateJoinPredicates(
                                TableAlias,
                                columns,
                                subtableAlias,
                                subTableColumns
                            ));
                            break;
                        case GraphQLRelationshipType.ManyToMany:
                            string associativeTableName = fieldInfo.AssociativeTable;
                            string associativeTableAlias = CreateTableAlias();
                            TableDefinition associativeTableDefinition = SqlMetadataProvider.GetTableDefinition(associativeTableName);

                            ForeignKeyDefinition fkLeft = associativeTableDefinition.ForeignKeys[fieldInfo.LeftForeignKey];
                            List<string> columnsLeft = GetFkRefColumns(fkLeft, GetUnderlyingTableDefinition());
                            List<string> subTableColumnsLeft = GetFkColumns(fkLeft, associativeTableDefinition);

                            ForeignKeyDefinition fkRight = associativeTableDefinition.ForeignKeys[fieldInfo.RightForeignKey];
                            List<string> columnsRight = GetFkColumns(fkRight, associativeTableDefinition);
                            List<string> subTableColumnsRight = GetFkRefColumns(fkRight, subTableDefinition);

                            subquery.Predicates.AddRange(CreateJoinPredicates(
                                TableAlias,
                                columnsLeft,
                                associativeTableAlias,
                                subTableColumnsLeft
                            ));

                            subquery.Joins.Add(new SqlJoinStructure
                            (
                                associativeTableName,
                                associativeTableAlias,
                                CreateJoinPredicates(
                                        associativeTableAlias,
                                        columnsRight,
                                        subtableAlias,
                                        subTableColumnsRight
                                    ).ToList()
                            ));
                            break;

                        case GraphQLRelationshipType.None:
                            throw new NotSupportedException("Cannot do a join when there is no relationship");
                        default:
                            throw new NotSupportedException("Relationships type ${fieldInfo.RelationshipType} is not supported.");
                    }

                    string subqueryAlias = $"{subtableAlias}_subq";
                    JoinQueries.Add(subqueryAlias, subquery);
                    Columns.Add(new LabelledColumn(tableSchema: null, subqueryAlias, DATA_IDENT, fieldName));
                }
            }
        }

        /// <summary>
        /// Get foreign key columns (if no columns select table pk)
        /// </summary>
        private static List<string> GetFkColumns(ForeignKeyDefinition fk, TableDefinition table)
        {
            return fk.ReferencingColumns.Count > 0 ? fk.ReferencingColumns : table.PrimaryKey;
        }

        /// <summary>
        /// Get foreign key referenced columns (if no referenced columns select referenced table pk)
        /// </summary>
        private static List<string> GetFkRefColumns(ForeignKeyDefinition fk, TableDefinition refTable)
        {
            return fk.ReferencedColumns.Count > 0 ? fk.ReferencedColumns : refTable.PrimaryKey;
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
        /// Exposes the primary key of the underlying table of the structure
        /// as a list of OrderByColumn
        /// </summary>
        public List<OrderByColumn> PrimaryKeyAsOrderByColumns()
        {
            if (_primaryKey == null)
            {
                _primaryKey = new();

                foreach (string column in PrimaryKey())
                {
                    _primaryKey.Add(new Column(SchemaName, TableName, column, TableAlias));
                }
            }

            List<OrderByColumn> orderByList = new();
            foreach (Column column in _primaryKey)
            {
                orderByList.Add(new OrderByColumn(column.TableSchema, column.TableName, column.ColumnName, column.TableAlias!));
            }

            return orderByList;
        }

        /// <summary>
        /// Adds a labelled column to this query's columns
        /// </summary>
        protected void AddColumn(string columnName)
        {
            Columns.Add(new LabelledColumn(SchemaName, TableName, columnName, label: columnName, TableAlias));
        }

        /// <summary>
        /// Check if the column belongs to one of the subqueries
        /// </summary>
        public bool IsSubqueryColumn(Column column)
        {
            return column.TableName == null ? false : JoinQueries.ContainsKey(column.TableName);
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
