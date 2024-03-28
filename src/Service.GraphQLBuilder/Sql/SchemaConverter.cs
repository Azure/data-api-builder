// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.CustomScalars;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.NodaTime;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLStoredProcedureBuilder;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Sql
{
    public static class SchemaConverter
    {
        /// <summary>
        /// Generate a GraphQL object type from a SQL table/view/stored-procedure definition, combined with the runtime config entity information
        /// </summary>
        /// <param name="entityName">Name of the entity in the runtime config to generate the GraphQL object type for.</param>
        /// <param name="databaseObject">SQL database object information.</param>
        /// <param name="configEntity">Runtime config information for the table.</param>
        /// <param name="entities">Key/Value Collection mapping entity name to the entity object,
        /// currently used to lookup relationship metadata.</param>
        /// <param name="rolesAllowedForEntity">Roles to add to authorize directive at the object level (applies to query/read ops).</param>
        /// <param name="rolesAllowedForFields">Roles to add to authorize directive at the field level (applies to mutations).</param>
        /// <returns>A GraphQL object type to be provided to a Hot Chocolate GraphQL document.</returns>
        public static ObjectTypeDefinitionNode GenerateObjectTypeDefinitionForDatabaseObject(
            string entityName,
            DatabaseObject databaseObject,
            [NotNull] Entity configEntity,
            RuntimeEntities entities,
            IEnumerable<string> rolesAllowedForEntity,
            IDictionary<string, IEnumerable<string>> rolesAllowedForFields)
        {
            ObjectTypeDefinitionNode objectDefinitionNode;
            switch (databaseObject.SourceType)
            {
                case EntitySourceType.StoredProcedure:
                    objectDefinitionNode = CreateObjectTypeDefinitionForStoredProcedure(
                        entityName: entityName,
                        databaseObject: databaseObject,
                        configEntity: configEntity,
                        rolesAllowedForEntity: rolesAllowedForEntity,
                        rolesAllowedForFields: rolesAllowedForFields);
                    break;
                case EntitySourceType.Table:
                case EntitySourceType.View:
                    objectDefinitionNode = CreateObjectTypeDefinitionForTableOrView(
                        entityName: entityName,
                        databaseObject: databaseObject,
                        configEntity: configEntity,
                        entities: entities,
                        rolesAllowedForEntity: rolesAllowedForEntity,
                        rolesAllowedForFields: rolesAllowedForFields);
                    break;
                default:
                    throw new DataApiBuilderException(
                        message: $"The source type of entity: {entityName} is not supported",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }

            return objectDefinitionNode;
        }

        /// <summary>
        /// Helper method to create object type definition for stored procedures.
        /// </summary>
        /// <param name="entityName">Name of the entity in the runtime config to generate the GraphQL object type for.</param>
        /// <param name="databaseObject">SQL database object information.</param>
        /// <param name="configEntity">Runtime config information for the table.</param>
        /// <param name="rolesAllowedForEntity">Roles to add to authorize directive at the object level (applies to query/read ops).</param>
        /// <param name="rolesAllowedForFields">Roles to add to authorize directive at the field level (applies to mutations).</param>
        /// <returns>A GraphQL object type for the table/view to be provided to a Hot Chocolate GraphQL document.</returns>
        private static ObjectTypeDefinitionNode CreateObjectTypeDefinitionForStoredProcedure(
            string entityName,
            DatabaseObject databaseObject,
            Entity configEntity,
            IEnumerable<string> rolesAllowedForEntity,
            IDictionary<string, IEnumerable<string>> rolesAllowedForFields)
        {
            Dictionary<string, FieldDefinitionNode> fields = new();
            SourceDefinition storedProcedureDefinition = databaseObject.SourceDefinition;

            // When the result set is not defined, it could be a mutation operation with no returning columns
            // Here we create a field called result which will be an empty array.
            if (storedProcedureDefinition.Columns.Count == 0)
            {
                FieldDefinitionNode field = GetDefaultResultFieldForStoredProcedure();

                fields.TryAdd("result", field);
            }

            foreach ((string columnName, ColumnDefinition column) in storedProcedureDefinition.Columns)
            {
                List<DirectiveNode> directives = new();
                // A field is added to the schema when there is atleast one role allowed to access the field.
                if (rolesAllowedForFields.TryGetValue(key: columnName, out IEnumerable<string>? roles))
                {
                    // Even if roles is empty, we create a field for columns returned by a stored-procedures since they only support 1 CRUD action,
                    // and it's possible that it might return some values during mutation operation (i.e, containing one of create/update/delete permission).
                    FieldDefinitionNode field = GenerateFieldForColumn(configEntity, columnName, column, directives, roles);
                    fields.Add(columnName, field);
                }
            }

            // Top-level object type definition name should be singular.
            // The singularPlural.Singular value is used, and if not configured,
            // the top-level entity name value is used. No singularization occurs
            // if the top-level entity name is already plural.
            return new ObjectTypeDefinitionNode(
                location: null,
                name: new(value: GetDefinedSingularName(entityName, configEntity)),
                description: null,
                directives: GenerateObjectTypeDirectivesForEntity(entityName, configEntity, rolesAllowedForEntity),
                new List<NamedTypeNode>(),
                fields.Values.ToImmutableList());
        }

        /// <summary>
        /// Helper method to create object type definition for database tables or views.
        /// </summary>
        /// <param name="entityName">Name of the entity in the runtime config to generate the GraphQL object type for.</param>
        /// <param name="databaseObject">SQL database object information.</param>
        /// <param name="configEntity">Runtime config information for the table.</param>
        /// <param name="entities">Key/Value Collection mapping entity name to the entity object,
        /// currently used to lookup relationship metadata.</param>
        /// <param name="rolesAllowedForEntity">Roles to add to authorize directive at the object level (applies to query/read ops).</param>
        /// <param name="rolesAllowedForFields">Roles to add to authorize directive at the field level (applies to mutations).</param>
        /// <returns>A GraphQL object type for the table/view to be provided to a Hot Chocolate GraphQL document.</returns>
        private static ObjectTypeDefinitionNode CreateObjectTypeDefinitionForTableOrView(
            string entityName,
            DatabaseObject databaseObject,
            Entity configEntity,
            RuntimeEntities entities,
            IEnumerable<string> rolesAllowedForEntity,
            IDictionary<string, IEnumerable<string>> rolesAllowedForFields)
        {
            Dictionary<string, FieldDefinitionNode> fieldDefinitionNodes = new();
            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;
            foreach ((string columnName, ColumnDefinition column) in sourceDefinition.Columns)
            {
                List<DirectiveNode> directives = new();
                if (sourceDefinition.PrimaryKey.Contains(columnName))
                {
                    directives.Add(new DirectiveNode(PrimaryKeyDirectiveType.DirectiveName, new ArgumentNode("databaseType", column.SystemType.Name)));
                }

                if (column.IsReadOnly)
                {
                    directives.Add(new DirectiveNode(AutoGeneratedDirectiveType.DirectiveName));
                }

                if (column.DefaultValue is not null)
                {
                    IValueNode arg = CreateValueNodeFromDbObjectMetadata(column.DefaultValue);

                    directives.Add(new DirectiveNode(DefaultValueDirectiveType.DirectiveName, new ArgumentNode("value", arg)));
                }

                // A field is added to the ObjectTypeDefinition when:
                // 1. The entity is a linking entity. A linking entity is not exposed by DAB for query/mutation but the fields are required to generate
                // object definitions of directional linking entities from source to target.
                // 2. The entity is not a linking entity and there is atleast one role allowed to access the field.
                if (rolesAllowedForFields.TryGetValue(key: columnName, out IEnumerable<string>? roles) || configEntity.IsLinkingEntity)
                {
                    // Roles will not be null here if TryGetValue evaluates to true, so here we check if there are any roles to process.
                    // This check is bypassed for linking entities for the same reason explained above.
                    if (configEntity.IsLinkingEntity || roles is not null && roles.Count() > 0)
                    {
                        FieldDefinitionNode field = GenerateFieldForColumn(configEntity, columnName, column, directives, roles);
                        fieldDefinitionNodes.Add(columnName, field);
                    }
                }
            }

            // A linking entity is not exposed in the runtime config file but is used by DAB to support multiple mutations on entities with M:N relationship.
            // Hence we don't need to process relationships for the linking entity itself.
            if (!configEntity.IsLinkingEntity)
            {
                // For an entity exposed in the config, process the relationships (if there are any)
                // sequentially and generate fields for them - to be added to the entity's ObjectTypeDefinition at the end.
                if (configEntity.Relationships is not null)
                {
                    foreach ((string relationshipName, EntityRelationship relationship) in configEntity.Relationships)
                    {
                        FieldDefinitionNode relationshipField = GenerateFieldForRelationship(
                            entityName,
                            databaseObject,
                            entities,
                            relationshipName,
                            relationship);
                        fieldDefinitionNodes.Add(relationshipField.Name.Value, relationshipField);
                    }
                }
            }

            // Top-level object type definition name should be singular.
            // The singularPlural.Singular value is used, and if not configured,
            // the top-level entity name value is used. No singularization occurs
            // if the top-level entity name is already plural.
            return new ObjectTypeDefinitionNode(
                location: null,
                name: new(value: GetDefinedSingularName(entityName, configEntity)),
                description: null,
                directives: GenerateObjectTypeDirectivesForEntity(entityName, configEntity, rolesAllowedForEntity),
                new List<NamedTypeNode>(),
                fieldDefinitionNodes.Values.ToImmutableList());
        }

        /// <summary>
        /// Helper method to generate the FieldDefinitionNode for a column in a table/view or a result set field in a stored-procedure.
        /// </summary>
        /// <param name="configEntity">Entity's definition (to which the column belongs).</param>
        /// <param name="columnName">Backing column name.</param>
        /// <param name="column">Column definition.</param>
        /// <param name="directives">List of directives to be added to the column's field definition.</param>
        /// <param name="roles">List of roles having read permission on the column (for tables/views) or execute permission for stored-procedure.</param>
        /// <returns>Generated field definition node for the column to be used in the entity's object type definition.</returns>
        private static FieldDefinitionNode GenerateFieldForColumn(Entity configEntity, string columnName, ColumnDefinition column, List<DirectiveNode> directives, IEnumerable<string>? roles)
        {
            if (GraphQLUtils.CreateAuthorizationDirectiveIfNecessary(
                                            roles,
                                            out DirectiveNode? authZDirective))
            {
                directives.Add(authZDirective!);
            }

            string exposedColumnName = columnName;
            if (configEntity.Mappings is not null && configEntity.Mappings.TryGetValue(key: columnName, out string? columnAlias))
            {
                exposedColumnName = columnAlias;
            }

            NamedTypeNode fieldType = new(GetGraphQLTypeFromSystemType(column.SystemType));
            FieldDefinitionNode field = new(
                location: null,
                new(exposedColumnName),
                description: null,
                new List<InputValueDefinitionNode>(),
                column.IsNullable ? fieldType : new NonNullTypeNode(fieldType),
                directives);
            return field;
        }

        /// <summary>
        /// Helper method to generate field for a relationship for an entity. These relationship fields are populated with relationship directive
        /// which stores the (cardinality, target entity) for the relationship. This enables nested queries/multiple mutations on the relationship fields.
        ///
        /// While processing the relationship, it helps in keeping track of fields from the source entity which hold foreign key references to the target entity.
        /// </summary>
        /// <param name="entityName">Name of the entity in the runtime config to generate the GraphQL object type for.</param>
        /// <param name="databaseObject">SQL database object information.</param>
        /// <param name="entities">Key/Value Collection mapping entity name to the entity object, currently used to lookup relationship metadata.</param>
        /// <param name="relationshipName">Name of the relationship.</param>
        /// <param name="relationship">Relationship data.</param>
        private static FieldDefinitionNode GenerateFieldForRelationship(
            string entityName,
            DatabaseObject databaseObject,
            RuntimeEntities entities,
            string relationshipName,
            EntityRelationship relationship)
        {
            // Generate the field that represents the relationship to ObjectType, so you can navigate through it
            // and walk the graph.
            string targetEntityName = relationship.TargetEntity.Split('.').Last();
            Entity referencedEntity = entities[targetEntityName];
            bool isNullableRelationship = FindNullabilityOfRelationship(entityName, databaseObject, targetEntityName);

            INullableTypeNode targetField = relationship.Cardinality switch
            {
                Cardinality.One =>
                    new NamedTypeNode(GetDefinedSingularName(targetEntityName, referencedEntity)),
                Cardinality.Many =>
                    new NamedTypeNode(QueryBuilder.GeneratePaginationTypeName(GetDefinedSingularName(targetEntityName, referencedEntity))),
                _ =>
                    throw new DataApiBuilderException(
                        message: "Specified cardinality isn't supported",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping),
            };

            FieldDefinitionNode relationshipField = new(
                location: null,
                new NameNode(relationshipName),
                description: null,
                new List<InputValueDefinitionNode>(),
                isNullableRelationship ? targetField : new NonNullTypeNode(targetField),
                new List<DirectiveNode> {
                            new(RelationshipDirectiveType.DirectiveName,
                                new ArgumentNode("target", GetDefinedSingularName(targetEntityName, referencedEntity)),
                                new ArgumentNode("cardinality", relationship.Cardinality.ToString()))
                });

            return relationshipField;
        }

        /// <summary>
        /// Helper method to generate the list of directives for an entity's object type definition.
        /// Generates and returns the authorize and model directives to be later added to the object's definition. 
        /// </summary>
        /// <param name="entityName">Name of the entity for whose object type definition, the list of directives are to be created.</param>
        /// <param name="configEntity">Entity definition.</param>
        /// <param name="rolesAllowedForEntity">Roles to add to authorize directive at the object level (applies to query/read ops).</param>
        /// <returns>List of directives for the object definition of the entity.</returns>
        private static List<DirectiveNode> GenerateObjectTypeDirectivesForEntity(string entityName, Entity configEntity, IEnumerable<string> rolesAllowedForEntity)
        {
            List<DirectiveNode> objectTypeDirectives = new();
            if (!configEntity.IsLinkingEntity)
            {
                objectTypeDirectives.Add(new(ModelDirectiveType.DirectiveName, new ArgumentNode("name", entityName)));
                if (GraphQLUtils.CreateAuthorizationDirectiveIfNecessary(
                        rolesAllowedForEntity,
                        out DirectiveNode? authorizeDirective))
                {
                    objectTypeDirectives.Add(authorizeDirective!);
                }
            }

            return objectTypeDirectives;
        }

        /// <summary>
        /// Get the GraphQL type equivalent from passed in system Type
        /// </summary>
        /// <param name="type">System type.</param>
        /// <exception cref="DataApiBuilderException">Raised when the provided type does not map to a supported
        /// GraphQL type.</exception>"
        public static string GetGraphQLTypeFromSystemType(Type type)
        {
            return type.Name switch
            {
                "String" => STRING_TYPE,
                "Guid" => UUID_TYPE,
                "Byte" => BYTE_TYPE,
                "Int16" => SHORT_TYPE,
                "Int32" => INT_TYPE,
                "Int64" => LONG_TYPE,
                "Single" => SINGLE_TYPE,
                "Double" => FLOAT_TYPE,
                "Decimal" => DECIMAL_TYPE,
                "Boolean" => BOOLEAN_TYPE,
                "DateTime" => DATETIME_TYPE,
                "DateTimeOffset" => DATETIME_TYPE,
                "Byte[]" => BYTEARRAY_TYPE,
                "TimeOnly" => LOCALTIME_TYPE,
                "TimeSpan" => LOCALTIME_TYPE,
                _ => throw new DataApiBuilderException(
                        message: $"Column type {type} not handled by case. Please add a case resolving {type} to the appropriate GraphQL type",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping)
            };
        }

        /// <summary>
        /// Translates system type objects to HotChocolate ObjectValueNode's of the associated value type used for GraphQL schema creation.
        /// The HotChocolate IntValueNode has contructors for integral numeric types (byte, short, long) to
        /// maintain the precision of the input object's value.
        /// </summary>
        /// <param name="metadataValue">Object to be converted to GraphQL ObjectValueNode</param>
        /// <returns>The resulting IValueNode object converted from the input system type object. </returns>
        /// <seealso cref="https://github.com/ChilliCream/graphql-platform/blob/12.18.0/src/HotChocolate/Language/src/Language.SyntaxTree/IntValueNode.cs"/>
        /// <exception cref="DataApiBuilderException">Raised when the input argument's value type does not map to a supported GraphQL type.</exception>
        public static IValueNode CreateValueNodeFromDbObjectMetadata(object metadataValue)
        {
            IValueNode arg = metadataValue switch
            {
                byte value => new ObjectValueNode(new ObjectFieldNode(BYTE_TYPE, new IntValueNode(value))),
                short value => new ObjectValueNode(new ObjectFieldNode(SHORT_TYPE, new IntValueNode(value))),
                int value => new ObjectValueNode(new ObjectFieldNode(INT_TYPE, value)),
                long value => new ObjectValueNode(new ObjectFieldNode(LONG_TYPE, new IntValueNode(value))),
                Guid value => new ObjectValueNode(new ObjectFieldNode(UUID_TYPE, new UuidType().ParseValue(value))),
                string value => new ObjectValueNode(new ObjectFieldNode(STRING_TYPE, value)),
                bool value => new ObjectValueNode(new ObjectFieldNode(BOOLEAN_TYPE, value)),
                float value => new ObjectValueNode(new ObjectFieldNode(SINGLE_TYPE, new SingleType().ParseValue(value))),
                double value => new ObjectValueNode(new ObjectFieldNode(FLOAT_TYPE, value)),
                decimal value => new ObjectValueNode(new ObjectFieldNode(DECIMAL_TYPE, new FloatValueNode(value))),
                DateTimeOffset value => new ObjectValueNode(new ObjectFieldNode(DATETIME_TYPE, new DateTimeType().ParseValue(value))),
                DateTime value => new ObjectValueNode(new ObjectFieldNode(DATETIME_TYPE, new DateTimeType().ParseResult(value))),
                byte[] value => new ObjectValueNode(new ObjectFieldNode(BYTEARRAY_TYPE, new ByteArrayType().ParseValue(value))),
                TimeOnly value => new ObjectValueNode(new ObjectFieldNode(LOCALTIME_TYPE, new LocalTimeType().ParseResult(value))),
                _ => throw new DataApiBuilderException(
                    message: $"The type {metadataValue.GetType()} is not supported as a GraphQL default value",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping)
            };

            return arg;
        }

        /// <summary>
        /// Given the source entity name, its underlying database object and the targetEntityName,
        /// finds if the relationship field corresponding to the target should be nullable
        /// based on whether the source is the referencing or referenced object or both.
        /// </summary>
        /// <exception cref="DataApiBuilderException">Raised no relationship exists between the source and target
        /// entities.</exception>
        private static bool FindNullabilityOfRelationship(
            string entityName,
            DatabaseObject databaseObject,
            string targetEntityName)
        {
            bool isNullableRelationship = false;
            SourceDefinition sourceDefinition = databaseObject.SourceDefinition;
            if (// Retrieve all the relationship information for the source entity which is backed by this table definition
                sourceDefinition.SourceEntityRelationshipMap.TryGetValue(entityName, out RelationshipMetadata? relationshipInfo)
                &&
                // From the relationship information, obtain the foreign key definition for the given target entity
                relationshipInfo.TargetEntityToFkDefinitionMap.TryGetValue(targetEntityName,
                out List<ForeignKeyDefinition>? listOfForeignKeys))
            {
                // DAB optimistically adds entries to 'listOfForeignKeys' representing each relationship direction
                // between a pair of entities when 1:1 or many:1 relationships are defined in the runtime config.
                // Entries which don't have a matching corresponding foreign key in the database
                // will have 0 referencing/referenced columns. So, we need to filter out these
                // invalid entries. Non-zero referenced columns indicate valid matching foreign key definition in the
                // database and hence only those can be used to determine the directionality.

                // Find the foreignkeys in which the source entity is the referencing object.
                IEnumerable<ForeignKeyDefinition> referencingForeignKeyInfo =
                    listOfForeignKeys.Where(fk =>
                        fk.ReferencingColumns.Count > 0
                        && fk.ReferencedColumns.Count > 0
                        && fk.Pair.ReferencingDbTable.Equals(databaseObject));

                // Find the foreignkeys in which the source entity is the referenced object.
                IEnumerable<ForeignKeyDefinition> referencedForeignKeyInfo =
                    listOfForeignKeys.Where(fk =>
                        fk.ReferencingColumns.Count > 0
                        && fk.ReferencedColumns.Count > 0
                        && fk.Pair.ReferencedDbTable.Equals(databaseObject));

                // The source entity should at least be a referencing or referenced db object or both
                // in the foreign key relationship.
                if (referencingForeignKeyInfo.Count() > 0 || referencedForeignKeyInfo.Count() > 0)
                {
                    // The source entity could be both the referencing and referenced entity
                    // in case of missing foreign keys in the db or self referencing relationships.
                    // Use the nullability of referencing columns to determine
                    // the nullability of the relationship field only if
                    // 1. there is exactly one relationship where source is the referencing entity.
                    // DAB doesn't support multiple relationships at the moment.
                    // and
                    // 2. when the source is not a referenced entity in any of the relationships.
                    if (referencingForeignKeyInfo.Count() == 1 && referencedForeignKeyInfo.Count() == 0)
                    {
                        ForeignKeyDefinition foreignKeyInfo = referencingForeignKeyInfo.First();
                        isNullableRelationship = sourceDefinition.IsAnyColumnNullable(foreignKeyInfo.ReferencingColumns);
                    }
                    else
                    {
                        // a record of the "referenced" entity may or may not have a relationship with
                        // any other record of the referencing entity in the database
                        // (irrespective of nullability of the referenced columns)
                        // Setting the relationship field to nullable ensures even those records
                        // that are not related are considered while querying.
                        isNullableRelationship = true;
                    }
                }
                else
                {
                    throw new DataApiBuilderException(
                        message: $"No relationship exists between {entityName} and {targetEntityName}",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.GraphQLMapping);
                }
            }

            return isNullableRelationship;
        }
    }
}
