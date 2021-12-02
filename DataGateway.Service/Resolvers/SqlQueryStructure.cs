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
    /// SqlQueryStructure is an intermediate represtation of a SQL query. This
    /// intermediate structure can be used to generate a Postgres or MSSQL query.
    /// In some sense this is an AST (abstract syntax tree) of a SQL query.
    /// However, it only supports the very limited set of SQL constructs that we
    /// are needed to represent a GraphQL query or REST request as SQL.
    /// </summary>
    public class SqlQueryStructure
    {
        /// <summary>
        /// The columns which the query selects. The keys are the alias of this
        /// column.
        /// </summary>
        public Dictionary<string, string> Columns { get; }
        /// <summary>
        /// Conditions to be added to the query.
        /// </summary>
        public List<string> Conditions { get; }
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
        private readonly TableDefinition _tableDefinition;

        /// <summary>
        /// Generate the structure for a SQL query based on GraphQL query
        /// information.
        /// </summary>
        public SqlQueryStructure(IResolverContext ctx, IMetadataStoreProvider metadataStoreProvider, IQueryBuilder queryBuilder) : this(
            ctx,
            metadataStoreProvider,
            queryBuilder,
            ctx.Selection.Field,
            "table0",
            ctx.Selection.SyntaxNode,
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
            context.Conditions.ForEach(condition =>
            {
                string parameterName = $"param{Counter.Next()}";
                Parameters.Add(parameterName, condition.Value);
                Conditions.Add($"{QualifiedColumn(condition.Field)} = @{parameterName}");
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
            _tableDefinition = _metadataStoreProvider.GetTableDefinition(_typeInfo.Table);
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
            Conditions = new();
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
        /// The code
        /// </summary>
        public string Table(string name, string alias)
        {
            return $"{QuoteIdentifier(name)} AS {QuoteIdentifier(alias)}";
        }

        /// <summary>
        /// Given an unquoted column name, return a quoted qualified column
        /// name for the table that is queried in this query.
        /// </summary>
        public string QualifiedColumn(string columnName)
        {
            return QualifiedColumn(TableAlias, columnName);
        }

        /// <summary>
        /// Given an unquoted table alias and unquoted column name, return a
        /// quoted qualified column name.
        /// </summary>
        public string QualifiedColumn(string tableAlias, string columnName)
        {
            return $"{QuoteIdentifier(tableAlias)}.{QuoteIdentifier(columnName)}";
        }

        /// <summary>
        /// Add a column of the queried table to the columns of the result of
        /// this query.
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
                            leftColumnNames = _tableDefinition.ForeignKeys[fieldInfo.ForeignKey].Columns;
                            rightColumnNames = subTableDefinition.PrimaryKey;
                            break;
                        case GraphqlRelationshipType.OneToMany:
                            leftColumnNames = _tableDefinition.PrimaryKey;
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
                        subquery.Conditions.Add($"{leftColumn} = {rightColumn}");
                    }

                    string subqueryAlias = $"{subtableAlias}_subq";
                    JoinQueries.Add(subqueryAlias, subquery);
                    string column = _queryBuilder.WrapSubqueryColumn($"{QuoteIdentifier(subqueryAlias)}.{_queryBuilder.DataIdent()}", subquery);
                    Columns.Add(fieldName, column);
                }
            }
        }

        /// <summary>
        /// QuoteIdentifier simply forwards to the QuoteIdentifier
        /// implementation of the querybuilder that this query structure uses.
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

    }
}
