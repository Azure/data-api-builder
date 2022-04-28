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

            if (configEntity.Relationships is not null)
            {
                foreach ((string relationshipKey, Relationship relationship) in configEntity.Relationships)
                {
                    string referencedEntityName = relationship.TargetEntity;
                    Entity referencedEntity = entities[referencedEntityName];

                    INullableTypeNode targetField = relationship.Cardinality switch
                    {
                        Cardinality.One => new NamedTypeNode(FormatNameForObject(relationshipKey, referencedEntity)),
                        Cardinality.Many => new ListTypeNode(new NamedTypeNode(FormatNameForObject(relationshipKey, referencedEntity))),
                        _ => throw new NotImplementedException("Specified cardinality isn't supported"),
                    };

                    FieldDefinitionNode relationshipField = new(
                        location: null,
                        Pluralize(referencedEntityName, referencedEntity),
                        description: null,
                        new List<InputValueDefinitionNode>(),
                        // TODO: Check for whether it should be a nullable relationship based on the relationship fields
                        new NonNullTypeNode(targetField),
                        new List<DirectiveNode>());

                    fields.Add(relationshipField.Name.Value, relationshipField);

                    // The addition of directives is optional but might be useful metadata.
                    string referencedSourceName = referencedEntity.GetSourceName();

                    // Get all the foreign key definitions between the underlying source db object of
                    // the given entity and the underlying source db oject of the referenced entity.
                    IEnumerable<ForeignKeyDefinition>? foreignKeyDefinitions =
                        tableDefinition.ForeignKeys.Values
                        .Where(fk => fk.ReferencedTable.Equals(
                            referencedSourceName,
                            StringComparison.OrdinalIgnoreCase));

                    if (foreignKeyDefinitions is not null)
                    {
                        foreach (ForeignKeyDefinition fk in foreignKeyDefinitions)
                        {
                            foreach (string columnName in fk.ReferencingColumns)
                            {
                                ColumnDefinition column = tableDefinition.Columns[columnName];
                                FieldDefinitionNode field = fields[columnName];

                                fields[columnName] = field.WithDirectives(
                                    new List<DirectiveNode>(field.Directives) {
                                    new(
                                        RelationshipDirective.DirectiveName,
                                        new ArgumentNode("databaseType", column.SystemType.Name),
                                        new ArgumentNode("cardinality", relationship.Cardinality.ToString()),
                                        new ArgumentNode("referencedType", relationship.TargetEntity))
                                    });
                            }
                        }
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
