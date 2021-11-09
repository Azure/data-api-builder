using Azure.DataGateway.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using System.Collections.Generic;

namespace Azure.DataGateway.Service.Resolvers
{
    public class IncrementingInteger
    {
        private int _integer;
        public IncrementingInteger()
        {
            _integer = 1;
        }

        public int Next()
        {
            return _integer++;
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
        IResolverContext _ctx;
        IObjectField _schemaField;
        ObjectType _coreFieldType;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryBuilder _queryBuilder;
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
            this._ctx = ctx;
            this._schemaField = schemaField;
            this._metadataStoreProvider = metadataStoreProvider;
            this._queryBuilder = queryBuilder;
            if (IsList())
            {
                // TODO: Do checking of the Kind here
                _coreFieldType = (ObjectType)schemaField.Type.InnerType();
            }
            else
            {
                // TODO: Do checking of the Kind here
                _coreFieldType = (ObjectType)schemaField.Type;
            }

            DataIdent = QuoteIdentifier("data");

            // TODO: Allow specifying a different table name in the config
            TableName = $"{_coreFieldType.Name.Value.ToLower()}s";
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
            foreach (ISelectionNode node in Selections)
            {
                var field = node as FieldNode;
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
                    Models.TypeMetadata metadata = _metadataStoreProvider.GetTypeMetadata(_coreFieldType.Name);
                    Models.JoinMapping joinMapping = metadata.JoinMappings[fieldName];
                    string leftColumn = QualifiedColumn(TableAlias, joinMapping.LeftColumn);
                    string rightColumn = QualifiedColumn(subtableAlias, joinMapping.RightColumn);

                    ObjectField subSchemaField = _coreFieldType.Fields[fieldName];

                    SqlQueryStructure subquery = new(_ctx, _metadataStoreProvider, _queryBuilder, subSchemaField, subtableAlias, field, Counter);
                    subquery.Conditions.Add($"{leftColumn} = {rightColumn}");
                    string subqueryAlias = $"{subtableAlias}_subq";
                    JoinQueries.Add(subqueryAlias, subquery);
                    string column = _queryBuilder.WrapSubqueryColumn($"{QuoteIdentifier(subqueryAlias)}.{DataIdent}", subquery);
                    Columns.Add(fieldName, column);
                }
            }
        }

        public string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        public bool IsList()
        {
            return _schemaField.Type.Kind == TypeKind.List;
        }

        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }
    }
}
