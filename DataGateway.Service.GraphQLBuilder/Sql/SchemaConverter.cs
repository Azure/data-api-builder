using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.CustomScalars;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

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
        /// <param name="entities">Key/Value Collection mapping entity name to the entity object,
        /// currently used to lookup relationship metadata.</param>
        /// <param name="rolesAllowedForEntity">Roles to add to authorize directive at the object level (applies to query/read ops).</param>
        /// <param name="rolesAllowedForFields">Roles to add to authorize directive at the field level (applies to mutations).</param>
        /// <returns>A GraphQL object type to be provided to a Hot Chocolate GraphQL document.</returns>
        public static ObjectTypeDefinitionNode FromTableDefinition(
            string entityName,
            TableDefinition tableDefinition,
            [NotNull] Entity configEntity,
            Dictionary<string, Entity> entities,
            IEnumerable<string> rolesAllowedForEntity,
            IDictionary<string, IEnumerable<string>> rolesAllowedForFields)
        {
            Dictionary<string, FieldDefinitionNode> fields = new();
            List<DirectiveNode> objectTypeDirectives = new();

            foreach ((string columnName, ColumnDefinition column) in tableDefinition.Columns)
            {
                List<DirectiveNode> directives = new();

                if (tableDefinition.PrimaryKey.Contains(columnName))
                {
                    directives.Add(new DirectiveNode(PrimaryKeyDirectiveType.DirectiveName, new ArgumentNode("databaseType", column.SystemType.Name)));
                }

                if (column.IsAutoGenerated)
                {
                    directives.Add(new DirectiveNode(AutoGeneratedDirectiveType.DirectiveName));
                }

                if (column.DefaultValue is not null)
                {
                    IValueNode arg = column.DefaultValue switch
                    {
                        byte value => new ObjectValueNode(new ObjectFieldNode(BYTE_TYPE, new IntValueNode(value))),
                        short value => new ObjectValueNode(new ObjectFieldNode(SHORT_TYPE, new IntValueNode(value))),
                        int value => new ObjectValueNode(new ObjectFieldNode(INT_TYPE, value)),
                        long value => new ObjectValueNode(new ObjectFieldNode(LONG_TYPE, new IntValueNode(value))),
                        string value => new ObjectValueNode(new ObjectFieldNode(STRING_TYPE, value)),
                        bool value => new ObjectValueNode(new ObjectFieldNode(BOOLEAN_TYPE, value)),
                        float value => new ObjectValueNode(new ObjectFieldNode(SINGLE_TYPE, new SingleType().ParseValue(value))),
                        double value => new ObjectValueNode(new ObjectFieldNode(FLOAT_TYPE, value)),
                        decimal value => new ObjectValueNode(new ObjectFieldNode(DECIMAL_TYPE, new FloatValueNode(value))),
                        DateTime value => new ObjectValueNode(new ObjectFieldNode(DATETIME_TYPE, new DateTimeType().ParseResult(value))),
                        DateTimeOffset value => new ObjectValueNode(new ObjectFieldNode(DATETIME_TYPE, new DateTimeType().ParseValue(value))),
                        byte[] value => new ObjectValueNode(new ObjectFieldNode(BYTEARRAY_TYPE, new ByteArrayType().ParseValue(value))),
                        _ => throw new DataGatewayException(
                            message: $"The type {column.DefaultValue.GetType()} is not supported as a GraphQL default value",
                            statusCode: HttpStatusCode.InternalServerError,
                            subStatusCode: DataGatewayException.SubStatusCodes.GraphQLMapping)
                    };

                    directives.Add(new DirectiveNode(DefaultValueDirectiveType.DirectiveName, new ArgumentNode("value", arg)));
                }

                // If no roles are allowed for the field, we should not include it in the schema.
                // Consequently, the field is only added to schema if this conditional evaluates to TRUE.
                if (rolesAllowedForFields.TryGetValue(key: columnName, out IEnumerable<string>? roles))
                {
                    // Roles will not be null here if TryGetValue evaluates to true, so here we check if there are any roles to process.
                    if (roles.Count() > 0)
                    {

                        if (GraphQLUtils.CreateAuthorizationDirectiveIfNecessary(
                                roles,
                                out DirectiveNode? authZDirective))
                        {
                            directives.Add(authZDirective!);
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
                }
            }

            if (configEntity.Relationships is not null)
            {
                foreach ((string relationshipName, Relationship relationship) in configEntity.Relationships)
                {
                    // Generate the field that represents the relationship to ObjectType, so you can navigate through it
                    // and walk the graph
                    string targetEntityName = relationship.TargetEntity.Split('.').Last();
                    Entity referencedEntity = entities[targetEntityName];

                    INullableTypeNode targetField = relationship.Cardinality switch
                    {
                        Cardinality.One =>
                            new NamedTypeNode(FormatNameForObject(targetEntityName, referencedEntity)),
                        Cardinality.Many =>
                            new NamedTypeNode(QueryBuilder.GeneratePaginationTypeName(FormatNameForObject(targetEntityName, referencedEntity))),
                        _ =>
                            throw new DataGatewayException(
                                message: "Specified cardinality isn't supported",
                                statusCode: HttpStatusCode.InternalServerError,
                                subStatusCode: DataGatewayException.SubStatusCodes.GraphQLMapping),
                    };

                    FieldDefinitionNode relationshipField = new(
                        location: null,
                        new NameNode(FormatNameForField(relationshipName)),
                        description: null,
                        new List<InputValueDefinitionNode>(),
                        // TODO: Check for whether it should be a nullable relationship based on the relationship fields
                        new NonNullTypeNode(targetField),
                        new List<DirectiveNode> {
                            new(RelationshipDirectiveType.DirectiveName,
                                new ArgumentNode("target", targetEntityName),
                                new ArgumentNode("cardinality", relationship.Cardinality.ToString()))
                        });

                    fields.Add(relationshipField.Name.Value, relationshipField);
                }
            }

            objectTypeDirectives.Add(new(ModelDirectiveType.DirectiveName, new ArgumentNode("name", entityName)));

            if (GraphQLUtils.CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForEntity,
                    out DirectiveNode? authorizeDirective))
            {
                objectTypeDirectives.Add(authorizeDirective!);
            }

            // Top-level object type definition name should be singular.
            // The singularPlural.Singular value is used, and if not configured,
            // the top-level entity name value is used. No pluralization occurs.
            return new ObjectTypeDefinitionNode(
                location: null,
                name: new(value: FormatNameForObject(entityName, configEntity)),
                description: null,
                objectTypeDirectives,
                new List<NamedTypeNode>(),
                fields.Values.ToImmutableList());
        }

        /// <summary>
        /// Get the GraphQL type equivalent from ColumnType
        /// </summary>
        public static string GetGraphQLTypeForColumnType(Type type)
        {
            return type.Name switch
            {
                "String" => STRING_TYPE,
                "Byte" => BYTE_TYPE,
                "Int16" => SHORT_TYPE,
                "Int32" => INT_TYPE,
                "Int64" => LONG_TYPE,
                "Single" => SINGLE_TYPE,
                "Double" => FLOAT_TYPE,
                "Decimal" => DECIMAL_TYPE,
                "Boolean" => BOOLEAN_TYPE,
                "DateTime" => DATETIME_TYPE,
                "Byte[]" => BYTEARRAY_TYPE,
                _ => throw new DataGatewayException(
                        message: $"Column type {type} not handled by case. Please add a case resolving {type} to the appropriate GraphQL type",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.GraphQLMapping)
            };
        }
    }
}
