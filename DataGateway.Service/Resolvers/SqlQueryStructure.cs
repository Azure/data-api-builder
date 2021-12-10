using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
{

    /// <summary>
    /// IncrementingInteger provides a simple API to have an ever incrementing
    /// integer. The main usecase is so we can create aliases that are unique
    /// within a query, this integer serves as a unique part of their name.
    /// </summary>
    public class IncrementingInteger
    {
        private ulong _integer;
        public IncrementingInteger()
        {
            _integer = 1;
        }

        /// <summary>
        /// Get the next integer from this sequence of integers. The first
        /// integer that is returned is 1.
        /// </summary>
        public ulong Next()
        {
            return _integer++;
        }

    }

    /// <summary>
    /// SqlQueryStructure is an intermediate representation of a SQL query.
    /// This intermediate structure can be used to generate a Postgres or MSSQL
    /// query. In some sense this is an AST (abstract syntax tree) of a SQL
    /// query. However, it only supports the very limited set of SQL constructs
    /// that we are needed to represent a GraphQL query or REST request as SQL.
    /// </summary>
    public class SqlQueryStructure
    {
        /// <summary>
        /// The columns which the query selects. The keys are the alias of this
        /// column.
        /// </summary>
        public Dictionary<string, string> Columns { get; }
        /// <summary>
        /// Predicates that should filter the result set of the query.
        /// </summary>
        public List<string> Predicates { get; }
        /// <summary>
        /// The subqueries with which this query should be joined. The key are
        /// the aliases of the query.
        /// </summary>
        public Dictionary<string, SqlQueryStructure> JoinQueries { get; }

        /// <summary>
        /// The name of the table to be queried.
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// The alias of the table to be queried.
        /// </summary>
        public string TableAlias { get; }

        /// <summary>
        /// Counter.Next() can be used to get a unique integer within this
        /// query, which can be used to create unique aliases, parameters or
        /// other identifiers.
        /// </summary>
        public IncrementingInteger Counter { get; }

        /// <summary>
        /// Is the result supposed to be a list or not.
        /// </summary>
        public bool IsListQuery { get; set; }

        /// <summary>
        /// Parameters values required to execute the qeury.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// If this query is built because of a GraphQL query (as opposed to
        /// REST), then this is set to the resolver context of that query.
        /// </summary>
        IResolverContext _ctx;

        /// <summary>
        /// The underlying type of the type returned by this query see, the
        /// comment on UnderlyingType to understand what an underlying type is.
        /// </summary>
        ObjectType _underlyingFieldType;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryBuilder _queryBuilder;

        private readonly GraphqlType _typeInfo;

        // TODO: Remove this once REST uses the schema defined in the config.
        /// <summary>
        /// Wild Card for column selection.
        /// </summary>
        private const string ALL_COLUMNS = "*";

        /// <summary>
        /// Generate the structure for a SQL query based on GraphQL query
        /// information.
        /// </summary>
        public SqlQueryStructure(IResolverContext ctx, IMetadataStoreProvider metadataStoreProvider, IQueryBuilder queryBuilder)
            // This constructor simply forwards to the more general constructor
            // that is used to create GraphQL queries. We give it some values
            // that make sense for the outermost query.
            : this(
                ctx,
                metadataStoreProvider,
                queryBuilder,
                ctx.Selection.Field,
                // IncrementingInteger starts at 1, so we use 0 in our initial
                // table alias. This ensures it's unique.
                "table0",
                ctx.Selection.SyntaxNode,
                // The outermost query is where we start, so this can define
                // create the IncrementingInteger that will be shared between
                // all subqueries in this query.
                new IncrementingInteger()
            )
        { }

        /// <summary>
        /// Generate the structure for a SQL query based on FindRequestContext,
        /// which is created by a FindById or FindMany REST request.
        /// </summary>
        public SqlQueryStructure(FindRequestContext context, IMetadataStoreProvider metadataStoreProvider, IQueryBuilder queryBuilder) : this(metadataStoreProvider, queryBuilder, new IncrementingInteger())
        {
            TableName = context.EntityName;
            TableAlias = TableName;
            IsListQuery = context.IsListQuery;

            context.Fields.ForEach(fieldName => AddColumn(fieldName));
            context.Predicates.ForEach(predicate =>
            {
                string parameterName = $"param{Counter.Next()}";
                Parameters.Add(parameterName, ResolveParamTypeFromField(predicate.Value, predicate.Field));
                Predicates.Add($"{QualifiedColumn(predicate.Field)} = @{parameterName}");
            });
        }

        /// <summary>
        /// Private constructor that is used for recursive query generation,
        /// for each subquery that's necassery to resolve a nested GraphQL
        /// request.
        /// </summary>
        private SqlQueryStructure(
                IResolverContext ctx,
                IMetadataStoreProvider metadataStoreProvider,
                IQueryBuilder queryBuilder,
                IObjectField schemaField,
                string tableAlias,
                FieldNode queryField,
                IncrementingInteger counter
        ) : this(metadataStoreProvider, queryBuilder, counter)
        {
            _ctx = ctx;
            IOutputType outputType = schemaField.Type;
            _underlyingFieldType = UnderlyingType(outputType);

            _typeInfo = _metadataStoreProvider.GetGraphqlType(_underlyingFieldType.Name);
            TableName = _typeInfo.Table;
            TableAlias = tableAlias;
            AddGraphqlFields(queryField.SelectionSet.Selections);

            if (outputType.IsNonNullType())
            {
                IsListQuery = outputType.InnerType().IsListType();
            }
            else
            {
                IsListQuery = outputType.IsListType();
            }
        }

        /// <summary>
        /// Private constructor that is used as a base by all public
        /// constructors.
        /// </summary>
        private SqlQueryStructure(IMetadataStoreProvider metadataStoreProvider, IQueryBuilder queryBuilder, IncrementingInteger counter)
        {
            Columns = new();
            JoinQueries = new();
            Predicates = new();
            Parameters = new();
            _metadataStoreProvider = metadataStoreProvider;
            _queryBuilder = queryBuilder;
            Counter = counter;
        }

        /// <summary>
        /// UnderlyingType is the type main GraphQL type that is described by
        /// this type. This strips all modifiers, such as List and Non-Null.
        /// So the following GraphQL types would all have the underlyingType Book:
        /// - Book
        /// - [Book]
        /// - Book!
        /// - [Book]!
        /// - [Book!]!
        /// </summary>
        private static ObjectType UnderlyingType(IType type)
        {
            ObjectType underlyingType = type as ObjectType;
            if (underlyingType != null)
            {
                return underlyingType;
            }

            return UnderlyingType(type.InnerType());
        }

        /// <summary>
        /// Given an unquoted column name, return a quoted qualified column
        /// name for the table that is queried in this query. Quoting takes
        /// into account the database (double quotes for Postgres and square
        /// brackets for MSSQL).
        /// </summary>
        public string QualifiedColumn(string columnName)
        {
            return QualifiedColumn(TableAlias, columnName);
        }

        /// <summary>
        /// Given an unquoted table alias and unquoted column name, return a
        /// quoted qualified column name. Quoting takes into account the
        /// database (double quotes for Postgres and square brackets for
        /// MSSQL).
        /// </summary>
        public string QualifiedColumn(string tableAlias, string columnName)
        {
            return $"{QuoteIdentifier(tableAlias)}.{QuoteIdentifier(columnName)}";
        }

        /// <summary>
        /// Given an unquoted column, add a column of the queried table to the
        /// columns of the result of this query.
        /// </summary>
        public void AddColumn(string columnName)
        {
            string column = QualifiedColumn(columnName);
            Columns.Add(columnName, column);
        }

        /// <summary>
        /// AddGraphqlFields looks at the fields that are selected in the
        /// GraphQL query and all the necessary elements to the query which are
        /// required to return these fields. This includes adding the columns
        /// to the result set, but also adding any subqueries or joins that are
        /// required to fetch nested data.
        /// </summary>
        void AddGraphqlFields(IReadOnlyList<ISelectionNode> Selections)
        {
            foreach (ISelectionNode node in Selections)
            {
                FieldNode field = node as FieldNode;
                string fieldName = field.Name.Value;

                if (field.SelectionSet == null)
                {
                    AddColumn(fieldName);
                }
                else
                {
                    string subtableAlias = $"table{Counter.Next()}";
                    ObjectField subschemaField = _underlyingFieldType.Fields[fieldName];
                    ObjectType subunderlyingType = UnderlyingType(subschemaField.Type);

                    GraphqlType subTypeInfo = _metadataStoreProvider.GetGraphqlType(subunderlyingType.Name);
                    TableDefinition subTableDefinition = _metadataStoreProvider.GetTableDefinition(subTypeInfo.Table);
                    GraphqlField fieldInfo = _typeInfo.Fields[fieldName];

                    List<string> leftColumnNames;
                    List<string> rightColumnNames;
                    switch (fieldInfo.RelationshipType)
                    {
                        case GraphqlRelationshipType.ManyToOne:
                            leftColumnNames = GetTableDefinition().ForeignKeys[fieldInfo.ForeignKey].Columns;
                            rightColumnNames = subTableDefinition.PrimaryKey;
                            break;
                        case GraphqlRelationshipType.OneToMany:
                            leftColumnNames = GetTableDefinition().PrimaryKey;
                            rightColumnNames = subTableDefinition.ForeignKeys[fieldInfo.ForeignKey].Columns;
                            break;
                        case GraphqlRelationshipType.None:
                            throw new NotSupportedException("Cannot do a join when there is no relationship");
                        default:
                            throw new NotImplementedException("OneToOne and ManyToMany relationships are not yet implemented");
                    }

                    SqlQueryStructure subquery = new(_ctx, _metadataStoreProvider, _queryBuilder, subschemaField, subtableAlias, field, Counter);

                    foreach (var columnName in leftColumnNames.Zip(rightColumnNames, (l, r) => new { Left = l, Right = r }))
                    {
                        string leftColumn = QualifiedColumn(columnName.Left);
                        string rightColumn = QualifiedColumn(subtableAlias, columnName.Right);
                        subquery.Predicates.Add($"{leftColumn} = {rightColumn}");
                    }

                    string subqueryAlias = $"{subtableAlias}_subq";
                    JoinQueries.Add(subqueryAlias, subquery);
                    string column = _queryBuilder.WrapSubqueryColumn($"{QuoteIdentifier(subqueryAlias)}.{_queryBuilder.DataIdent}", subquery);
                    Columns.Add(fieldName, column);
                }
            }
        }

        ///<summary>
        /// Resolves a string parameter to the correct type, by using the type of the field
        /// it is supposed to be compared with
        ///</summary>
        object ResolveParamTypeFromField(string param, string fieldName)
        {
            string type = GetTableDefinition().Columns.GetValueOrDefault(fieldName).Type;
            switch(type){
                case "text":
                    return param;
                case "bigint":
                    return Int64.Parse(param);
                default:
                    throw new Exception("Type of field could not be determined");
            }
        }

        /// <summary>
        /// Returns the TableDefinition for the the table of this query.
        /// </summary>
        private TableDefinition GetTableDefinition()
        {
            return _metadataStoreProvider.GetTableDefinition(TableName);
        }

        /// <summary>
        /// QuoteIdentifier simply forwards to the QuoteIdentifier
        /// implementation of the querybuilder that this query structure uses.
        /// So it wrapse the string in double quotes for Postgres and square
        /// brackets for MSSQL.
        /// </summary>
        public string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Converts the query structure to the actual query string.
        /// </summary>
        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }

        /// <summary>
        /// The maximum number of results this query should return.
        /// </summary>
        public uint Limit()
        {
            if (IsListQuery)
            {
                // TODO: Make this configurable
                return 100;
            }
            else
            {
                return 1;
            }
        }

        /// <summary>
        /// Create the SQL code to select the columns defined in Columns for
        /// selection in the SELECT clause. So the {ColumnsSql} bit in this
        /// example:
        /// SELECT {ColumnsSql} FROM ... WHERE ...
        /// </summary>
        public string ColumnsSql()
        {
            if (Columns.Count == 0)
            {
                return ALL_COLUMNS;
            }

            return string.Join(", ", Columns.Select(
                        x => $"{x.Value} AS {QuoteIdentifier(x.Key)}"));
        }

        /// <summary>
        /// Create the SQL code to define the table in the FROM clause. So the
        /// {TableSql} bit in this example:
        /// SELECT ... FROM {TableSql} WHERE ...
        /// </summary>
        public string TableSql()
        {
            return $"{QuoteIdentifier(TableName)} AS {QuoteIdentifier(TableAlias)}";
        }

        /// <summary>
        /// Create the SQL code to filter the rows in the WHERE clause. So the
        /// {PredicatesSql} bit in this example:
        /// SELECT ... FROM ... WHERE {PredicatesSql}
        /// </summary>
        public string PredicatesSql()
        {
            if (Predicates.Count() == 0)
            {
                return "1 = 1";
            }

            return string.Join(" AND ", Predicates);
        }
        /// <summary>
        /// Create the SQL code to that should be included in the ORDER BY
        /// section to order the results as intended. So the {OrderBySql} bit
        /// in this example:
        /// SELECT ... FROM ... WHERE ... ORDER BY {OrderBySql}
        ///
        /// NOTE: Currently all queries are ordered by their primary key.
        /// </summary>
        public string OrderBySql()
        {
            return string.Join(", ", GetTableDefinition().PrimaryKey.Select(
                        x => QuoteIdentifier(x)));
        }
    }
}
