using System.Collections.Generic;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Cosmos.GraphQL.Services;

namespace Cosmos.GraphQL.Service.Resolvers
{

    public class IncrementingInteger
    {
        private int integer;
        public IncrementingInteger()
        {
            integer = 1;
        }

        public int Next()
        {
            return integer++;
        }

    }

    public class SqlQueryStructure
    {
        public Dictionary<string, string> Columns { get; }
        public List<string> Conditions;
        public Dictionary<string, SqlQueryStructure> JoinQueries { get; }
        public string TableName { get; }
        public string TableAlias { get; }
        public IncrementingInteger Counter { get; }
        public string DataIdent { get; }
        IResolverContext ctx;
        IObjectField schemaField;
        ObjectType coreFieldType;
        private readonly IMetadataStoreProvider metadataStoreProvider;
        private readonly IQueryBuilder queryBuilder;
        // public List<string> conditions;
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

        public SqlQueryStructure(
                IResolverContext ctx,
                IMetadataStoreProvider metadataStoreProvider,
                IQueryBuilder queryBuilder,
                IObjectField schemaField,
                string tableAlias,
                FieldNode queryField,
                IncrementingInteger counter
        )
        {
            Columns = new();
            JoinQueries = new();
            Conditions = new();
            Counter = counter;
            this.ctx = ctx;
            this.schemaField = schemaField;
            this.metadataStoreProvider = metadataStoreProvider;
            this.queryBuilder = queryBuilder;
            if (IsList())
            {
                // TODO: Do checking of the Kind here
                coreFieldType = (ObjectType)schemaField.Type.InnerType();
            }
            else
            {
                // TODO: Do checking of the Kind here
                coreFieldType = (ObjectType)schemaField.Type;
            }
            DataIdent = QuoteIdentifier("data");

            // TODO: Allow specifying a different table name in the config
            TableName = $"{coreFieldType.Name.Value.ToLower()}s";
            TableAlias = tableAlias;
            AddFields(queryField.SelectionSet.Selections);
        }
        public string Table(string name, string alias)
        {
            return $"{QuoteIdentifier(name)} AS {QuoteIdentifier(alias)}";
        }

        public string QualifiedColumn(string tableAlias, string columnName)
        {
            return $"{QuoteIdentifier(tableAlias)}.{QuoteIdentifier(columnName)}";
        }

        void AddFields(IReadOnlyList<ISelectionNode> Selections)
        {
            foreach (var node in Selections)
            {
                FieldNode field = node as FieldNode;
                string fieldName = field.Name.Value;

                if (field.SelectionSet == null)
                {
                    // TODO: Get allow configuring a different column name in
                    // the  JSON config
                    string columnName = field.Name.Value;
                    string column = QualifiedColumn(TableAlias, columnName);
                    Columns.Add(fieldName, column);
                }
                else
                {
                    string subtableAlias = $"table{Counter.Next()}";
                    var metadata = metadataStoreProvider.GetTypeMetadata(coreFieldType.Name);
                    var joinMapping = metadata.JoinMappings[fieldName];
                    string leftColumn = QualifiedColumn(TableAlias, joinMapping.LeftColumn);
                    string rightColumn = QualifiedColumn(subtableAlias, joinMapping.RightColumn);

                    var subSchemaField = coreFieldType.Fields[fieldName];

                    SqlQueryStructure subquery = new(ctx, metadataStoreProvider, queryBuilder, subSchemaField, subtableAlias, field, Counter);
                    subquery.Conditions.Add($"{leftColumn} = {rightColumn}");
                    string subqueryAlias = $"{subtableAlias}_subq";
                    JoinQueries.Add(subqueryAlias, subquery);
                    var column = queryBuilder.WrapSubqueryColumn($"{QuoteIdentifier(subqueryAlias)}.{DataIdent}", subquery);
                    Columns.Add(fieldName, column);
                }
            }
        }

        public string QuoteIdentifier(string ident)
        {
            return queryBuilder.QuoteIdentifier(ident);
        }

        public bool IsList()
        {
            return schemaField.Type.Kind == TypeKind.List;
        }

        public override string ToString()
        {
            return queryBuilder.Build(this);
        }
    }
}
