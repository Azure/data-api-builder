using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Sql
{
    public static class SchemaConverter
    {
        public static ObjectTypeDefinitionNode FromTableDefinition(string tableName, TableDefinition tableDefinition)
        {
            List<FieldDefinitionNode> fields = new();

            foreach ((string columnName, ColumnDefinition column) in tableDefinition.Columns)
            {
                if (tableDefinition.ForeignKeys.ContainsKey(columnName))
                {
                    ForeignKeyDefinition foreignKeyDefinition = tableDefinition.ForeignKeys[columnName];

                    FieldDefinitionNode field = new(
                        location: null,
                        Pluralize(foreignKeyDefinition.ReferencedTable),
                        description: null,
                        new List<InputValueDefinitionNode>(),
                        new NonNullTypeNode(new NamedTypeNode(FormatNameForObject(foreignKeyDefinition.ReferencedTable))),
                        new List<DirectiveNode>());
                    fields.Add(field);
                }

                else
                {
                    List<DirectiveNode> directives = new();
                    if (tableDefinition.PrimaryKey.Contains(columnName))
                    {
                        directives.Add(new DirectiveNode(PrimaryKeyDirective.DirectiveName, new ArgumentNode("databaseType", column.SystemType.Name)));
                    }

                    FieldDefinitionNode field = new(
                        location: null,
                        new(FormatNameForField(columnName)),
                        description: null,
                        new List<InputValueDefinitionNode>(),
                        new NamedTypeNode(GetGraphQLTypeForColumnType(column.SystemType)),
                        directives);

                    fields.Add(field);
                }
            }

            return new ObjectTypeDefinitionNode(
                location: null,
                new(FormatNameForObject(tableName)),
                description: null,
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                fields);
        }

        /// <summary>
        /// Get the GraphQL type equivalent from ColumnType
        /// </summary>
        private static string GetGraphQLTypeForColumnType(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.String => "String",
                TypeCode.Int64 => "Int",
                _ => throw new ArgumentException($"ColumnType {type} not handled by case. Please add a case resolving " +
                                                                $"{type} to the appropriate GraphQL type"),
            };
        }
    }
}
