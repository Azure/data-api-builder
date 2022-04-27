using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataGateway.Service.GraphQLBuilder.Sql
{
    public static class SchemaConverter
    {
        /// <summary>
        /// Generate a GraphQL object type from a SQL table definition, combined with the runtime config entity information
        /// </summary>
        /// <param name="entityName">Name of the entity in the runtime config to generate the GraphQL object type for.</param>
        /// <param name="tableDefinition">SQL table definition information.</param>
        /// <param name="configEntity">Runtime config information for the table.</param>
        /// <returns>A GraphQL object type to be provided to a Hot Chocolate GraphQL document.</returns>
        public static ObjectTypeDefinitionNode FromTableDefinition(string entityName, TableDefinition tableDefinition, [NotNull] Entity configEntity, Dictionary<string, Entity> entities)
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

            if (configEntity.Relationships != null)
            {
                foreach ((string _, ForeignKeyDefinition fk) in tableDefinition.ForeignKeys)
                {
                    if (!configEntity.Relationships.ContainsKey(fk.ReferencedTable))
                    {
                        // While the table has a fk, it's not defined as a relationship for the runtime
                        // meaning we'll assume the developer doesn't want it exposed, so we'll skip it.

                        // TODO: Log out a message so someone can see why it wasn't generated

                        continue;
                    }

                    Relationship relationship = configEntity.Relationships[fk.ReferencedTable];

                    // Generate the field that represents the relationship to ObjectType, so you can navigate through it
                    // and walk the graph

                    // TODO: This will need to be expanded to take care of the query fields that are available
                    //       on the relationship, but until we have the work done to generate the right Input
                    //       types for the queries, it's not worth trying to do it completely.

                    Entity referencedEntity = entities[fk.ReferencedTable];

                    INullableTypeNode targetField = relationship.Cardinality switch
                    {
                        Cardinality.One => new NamedTypeNode(FormatNameForObject(fk.ReferencedTable, referencedEntity)),
                        Cardinality.Many => new ListTypeNode(new NamedTypeNode(FormatNameForObject(fk.ReferencedTable, referencedEntity))),
                        _ => throw new NotImplementedException("Specified cardinality isn't supported"),
                    };

                    FieldDefinitionNode relationshipField = new(
                        location: null,
                        Pluralize(fk.ReferencedTable, referencedEntity),
                        description: null,
                        new List<InputValueDefinitionNode>(),
                        // TODO: Check for whether it should be a nullable relationship based on the relationship fields
                        new NonNullTypeNode(targetField),
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
                                new ArgumentNode("cardinality", relationship.Cardinality.ToString()))
                            });
                    }
                }
            }

            return new ObjectTypeDefinitionNode(
                location: null,
                new(FormatNameForObject(entityName, configEntity)),
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
