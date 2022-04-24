using System.Collections.Immutable;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataGateway.Service.GraphQLBuilder.Sql
{
    public static class SchemaConverter
    {
        public static ObjectTypeDefinitionNode FromTableDefinition(string tableName, TableDefinition tableDefinition)
        {
            Dictionary<string, FieldDefinitionNode> fields = new();

            foreach ((string columnName, ColumnDefinition column) in tableDefinition.Columns)
            {
                List<DirectiveNode> directives = new();

                if (tableDefinition.PrimaryKey.Contains(columnName))
                {
                    directives.Add(new DirectiveNode(PrimaryKeyDirective.DirectiveName, new ArgumentNode("databaseType", column.SystemType.Name)));
                }

                NamedTypeNode fieldType = new(GetGraphQLTypeForColumnType(column.SystemType));
                FieldDefinitionNode field = new(
                    location: null,
                    new(FormatNameForField(columnName)),
                    description: null,
                    new List<InputValueDefinitionNode>(),
                    column.IsNullable ? fieldType : new NonNullTypeNode(fieldType),
                    directives);

                fields.Add(columnName, field);
            }

            foreach ((string _, ForeignKeyDefinition fk) in tableDefinition.ForeignKeys)
            {
                // Generate the field that represents the relationship to ObjectType, so you can navigate through it
                // and walk the graph

                // TODO: This will need to be expanded to take care of the query fields that are available
                //       on the relationship, but until we have the work done to generate the right Input
                //       types for the queries, it's not worth trying to do it completely.

                // TODO: Also need to look at the cardinality of the relationship. If it's a 1-M then this
                //       side should be a singular not plural field.
                FieldDefinitionNode relationshipField = new(
                    location: null,
                    Pluralize(fk.ReferencedTable),
                    description: null,
                    new List<InputValueDefinitionNode>(),
                    new NonNullTypeNode(new NamedTypeNode(FormatNameForObject(fk.ReferencedTable))),
                    new List<DirectiveNode>());

                fields.Add(relationshipField.Name.Value, relationshipField);

                foreach (string columnName in fk.ReferencingColumns)
                {
                    ColumnDefinition column = tableDefinition.Columns[columnName];
                    FieldDefinitionNode field = fields[columnName];

                    fields[columnName] = field.WithDirectives(
                        new List<DirectiveNode>(field.Directives) {
                            new(
                                RelationshipDirective.DirectiveName,
                                new ArgumentNode("databaseType", column.SystemType.Name),
                                // TODO: Set cardinality when it's available in config
                                new ArgumentNode("cardinality", ""))
                        });
                }
            }

            return new ObjectTypeDefinitionNode(
                location: null,
                new(FormatNameForObject(tableName)),
                description: null,
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                fields.Values.ToImmutableList());
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
                _ => throw new ArgumentException($"ColumnType {type} not handled by case. Please add a case resolving {type} to the appropriate GraphQL type"),
            };
        }
    }
}
