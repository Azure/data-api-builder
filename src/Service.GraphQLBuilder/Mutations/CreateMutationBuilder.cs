// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations
{
    public static class CreateMutationBuilder
    {
        private const string INSERT_MULTIPLE_MUTATION_SUFFIX = "Multiple";
        public const string INPUT_ARGUMENT_NAME = "item";
        public const string CREATE_MUTATION_PREFIX = "create";

        /// <summary>
        /// Generate the GraphQL input type from an object type
        /// </summary>
        /// <param name="inputs">Reference table of all known input types.</param>
        /// <param name="objectTypeDefinitionNode">GraphQL object to generate the input type for.</param>
        /// <param name="name">Name of the GraphQL object type.</param>
        /// <param name="baseEntityName">In case when we are creating input type for linking object, baseEntityName is equal to the targetEntityName,
        /// else baseEntityName is equal to the name parameter.</param>
        /// <param name="definitions">All named GraphQL items in the schema (objects, enums, scalars, etc.)</param>
        /// <param name="databaseType">Database type to generate input type for.</param>
        /// <param name="entities">Runtime config information.</param>
        /// <returns>A GraphQL input type with all expected fields mapped as GraphQL inputs.</returns>
        private static InputObjectTypeDefinitionNode GenerateCreateInputType(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            NameNode baseEntityName,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            DatabaseType databaseType)
        {
            NameNode inputName = GenerateInputTypeName(name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            // The input fields for a create object will be a combination of:
            // 1. Scalar input fields corresponding to columns which belong to the table.
            // 2. Complex input fields corresponding to tables having a relationship defined with this table in the config.
            List<InputValueDefinitionNode> inputFields = new();

            // 1. Scalar input fields.
            IEnumerable<InputValueDefinitionNode> simpleInputFields = objectTypeDefinitionNode.Fields
                .Where(f => IsBuiltInType(f.Type))
                .Where(f => IsBuiltInTypeFieldAllowedForCreateInput(f, databaseType))
                .Select(f =>
                {
                    return GenerateScalarInputType(name, f, databaseType);
                });

            // Add simple input fields to list of input fields for current input type.
            foreach (InputValueDefinitionNode simpleInputField in simpleInputFields)
            {
                inputFields.Add(simpleInputField);
            }

            // Create input object for this entity.
            InputObjectTypeDefinitionNode input =
                new(
                    location: null,
                    inputName,
                    new StringValueNode($"Input type for creating {name}"),
                    new List<DirectiveNode>(),
                    inputFields
                );

            // Add input object to the dictionary of entities for which input object has already been created.
            // This input object currently holds only simple fields.
            // The complex fields (for related entities) would be added later when we return from recursion.
            // Adding the input object to the dictionary ensures that we don't go into infinite recursion and return whenever
            // we find that the input object has already been created for the entity.
            inputs.Add(input.Name, input);

            // 2. Complex input fields.
            // Evaluate input objects for related entities.
            IEnumerable<InputValueDefinitionNode> complexInputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => !IsBuiltInType(f.Type))
                .Where(f => IsComplexFieldAllowedForCreateInput(f, databaseType, definitions))
                .Select(f =>
                {
                    string typeName = RelationshipDirectiveType.Target(f);
                    HotChocolate.Language.IHasName? def = definitions.FirstOrDefault(d => d.Name.Value == typeName);

                    if (def is null)
                    {
                        throw new DataApiBuilderException($"The type {typeName} is not a known GraphQL type, and cannot be used in this schema.", HttpStatusCode.InternalServerError, DataApiBuilderException.SubStatusCodes.GraphQLMapping);
                    }

                    if (DoesRelationalDBSupportNestedMutations(databaseType) && IsMToNRelationship(f, (ObjectTypeDefinitionNode)def, baseEntityName))
                    {
                        // The field can represent a related entity with M:N relationship with the parent.
                        NameNode baseEntityNameForField = new(typeName);
                        typeName = GenerateLinkingNodeName(baseEntityName.Value, typeName);
                        def = (ObjectTypeDefinitionNode)definitions.FirstOrDefault(d => d.Name.Value == typeName)!;

                        // Get entity definition for this ObjectTypeDefinitionNode.
                        // Recurse for evaluating input objects for related entities.
                        return GetComplexInputType(inputs, definitions, f, typeName, baseEntityNameForField, (ObjectTypeDefinitionNode)def, databaseType);
                    }

                    // Get entity definition for this ObjectTypeDefinitionNode.
                    // Recurse for evaluating input objects for related entities.
                    return GetComplexInputType(inputs, definitions, f, typeName, new(typeName), (ObjectTypeDefinitionNode)def, databaseType);
                });
            // Append relationship fields to the input fields.
            inputFields.AddRange(complexInputFields);
            return input;
        }

        /// <summary>
        /// This method is used to determine if a field is allowed to be sent from the client in a Create mutation (eg, id field is not settable during create).
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="databaseType">The type of database to generate for</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool IsComplexFieldAllowedForCreateInput(FieldDefinitionNode field, DatabaseType databaseType, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            if (QueryBuilder.IsPaginationType(field.Type.NamedType()))
            {
                // Support for inserting nested entities with relationship cardinalities of 1-N or N-N is only supported for MsSql.
                return databaseType is DatabaseType.MSSQL;
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);
            // When creating, you don't need to provide the data for nested models, but you will for other nested types
            // For cosmos, allow updating nested objects
            if (definition != null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType) && databaseType is not DatabaseType.CosmosDB_NoSQL)
            {
                return databaseType is DatabaseType.MSSQL;
            }

            return true;
        }

        /// <summary>
        /// Helper method to determine whether a built in type (all GQL types supported by DAB) field is allowed to be present
        /// in the input object for a create mutation.
        /// </summary>
        /// <param name="field">Field definition.</param>
        /// <param name="databaseType">Database type.</param>
        private static bool IsBuiltInTypeFieldAllowedForCreateInput(FieldDefinitionNode field, DatabaseType databaseType)
        {
            // cosmosdb_nosql doesn't have the concept of "auto increment" for the ID field, nor does it have "auto generate"
            // fields like timestap/etc. like SQL, so we're assuming that any built-in type will be user-settable
            // during the create mutation
            return databaseType switch
            {
                DatabaseType.CosmosDB_NoSQL => true,
                _ => !IsAutoGeneratedField(field)
            };
        }

        /// <summary>
        /// Helper method to check if a field in an entity(table) is a  referencing field to a referenced field
        /// in another entity.
        /// </summary>
        /// <param name="field">Field definition.</param>
        private static bool IsAReferencingField(FieldDefinitionNode field)
        {
            return field.Directives.Any(d => d.Name.Value.Equals(ReferencingFieldDirectiveType.DirectiveName));
        }

        /// <summary>
        /// Helper method to create input type for a scalar/column field in an entity.
        /// </summary>
        /// <param name="name">Name of the field.</param>
        /// <param name="fieldDefinition">Field definition.</param>
        /// <param name="databaseType">Database type</param>
        private static InputValueDefinitionNode GenerateScalarInputType(NameNode name, FieldDefinitionNode fieldDefinition, DatabaseType databaseType)
        {
            IValueNode? defaultValue = null;

            if (DefaultValueDirectiveType.TryGetDefaultValue(fieldDefinition, out ObjectValueNode? value))
            {
                defaultValue = value.Fields[0].Value;
            }

            return new(
                location: null,
                fieldDefinition.Name,
                new StringValueNode($"Input for field {fieldDefinition.Name} on type {GenerateInputTypeName(name.Value)}"),
                defaultValue is not null || databaseType is DatabaseType.MSSQL && IsAReferencingField(fieldDefinition) ? fieldDefinition.Type.NullableType() : fieldDefinition.Type,
                defaultValue,
                new List<DirectiveNode>()
            );
        }

        /// <summary>
        /// Generates a GraphQL Input Type value for an object type, generally one provided from the database.
        /// </summary>
        /// <param name="inputs">Dictionary of all input types, allowing reuse where possible.</param>
        /// <param name="definitions">All named GraphQL types from the schema (objects, enums, etc.) for referencing.</param>
        /// <param name="field">Field that the input type is being generated for.</param>
        /// <param name="typeName">In case of relationships with M:N cardinality, typeName = type name of linking object, else typeName = type name of target entity.</param>
        /// <param name="baseObjectTypeName">Object type name of the target entity.</param>
        /// <param name="childObjectTypeDefinitionNode">The GraphQL object type to create the input type for.</param>
        /// <param name="databaseType">Database type to generate the input type for.</param>
        /// <param name="entities">Runtime configuration information for entities.</param>
        /// <returns>A GraphQL input type value.</returns>
        private static InputValueDefinitionNode GetComplexInputType(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            FieldDefinitionNode field,
            string typeName,
            NameNode baseObjectTypeName,
            ObjectTypeDefinitionNode childObjectTypeDefinitionNode,
            DatabaseType databaseType)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateCreateInputType(inputs, childObjectTypeDefinitionNode, new NameNode(typeName), baseObjectTypeName, definitions, databaseType);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            ITypeNode type = new NamedTypeNode(node.Name);
            if (databaseType is DatabaseType.MSSQL)
            {
                if (RelationshipDirectiveType.Cardinality(field) is Cardinality.Many)
                {
                    // For *:N relationships, we need to create a list type.
                    type = GenerateListType(type, field.Type.InnerType());
                }

                // Since providing input for a relationship field is optional, the type should be nullable.
                type = (INullableTypeNode)type;
            }
            // For a type like [Bar!]! we have to first unpack the outer non-null
            else if (field.Type.IsNonNullType())
            {
                // The innerType is the raw List, scalar or object type without null settings
                ITypeNode innerType = field.Type.InnerType();

                if (innerType.IsListType())
                {
                    type = GenerateListType(type, innerType);
                }

                // Wrap the input with non-null to match the field definition
                type = new NonNullTypeNode((INullableTypeNode)type);
            }
            else if (field.Type.IsListType())
            {
                type = GenerateListType(type, field.Type);
            }

            return new(
                location: null,
                name: field.Name,
                description: new StringValueNode($"Input for field {field.Name} on type {inputTypeName}"),
                type: type,
                defaultValue: null,
                directives: field.Directives
            );
        }

        /// <summary>
        /// Helper method to determine if there is a M:N relationship between the parent and child node by checking that the relationship
        /// directive's cardinality value is Cardinality.Many for both parent -> child and child -> parent.
        /// </summary>
        /// <param name="childFieldDefinitionNode">FieldDefinition of the child node.</param>
        /// <param name="childObjectTypeDefinitionNode">Object definition of the child node.</param>
        /// <param name="parentNode">Parent node's NameNode.</param>
        /// <returns>true if the relationship between parent and child entities has a cardinality of M:N.</returns>
        private static bool IsMToNRelationship(FieldDefinitionNode childFieldDefinitionNode, ObjectTypeDefinitionNode childObjectTypeDefinitionNode, NameNode parentNode)
        {
            // Determine the cardinality of the relationship from parent -> child, where parent is the entity present at a level
            // higher than the child. Eg. For 1:N relationship from parent -> child, the right cardinality is N.
            Cardinality rightCardinality = RelationshipDirectiveType.Cardinality(childFieldDefinitionNode);
            if (rightCardinality is not Cardinality.Many)
            {
                // Indicates that there is a *:1 relationship from parent -> child.
                return false;
            }

            // We have concluded that there is an *:N relationship from parent -> child.
            // But for a many-to-many relationship, we should have an M:N relationship between parent and child.
            List<FieldDefinitionNode> fieldsInChildNode = childObjectTypeDefinitionNode.Fields.ToList();

            // If the cardinality of relationship from child->parent is N:M, we must find a paginated field for parent in the child
            // object definition's fields.
            int indexOfParentFieldInChildDefinition = fieldsInChildNode.FindIndex(field => field.Type.NamedType().Name.Value.Equals(QueryBuilder.GeneratePaginationTypeName(parentNode.Value)));
            if (indexOfParentFieldInChildDefinition == -1)
            {
                // Indicates that there is a 1:N relationship from parent -> child.
                return false;
            }

            // Indicates an M:N relationship from parent->child.
            return true;
        }

        private static ITypeNode GenerateListType(ITypeNode type, ITypeNode fieldType)
        {
            // Look at the inner type of the list type, eg: [Bar]'s inner type is Bar
            // and if it's nullable, make the input also nullable
            return fieldType.InnerType().IsNonNullType()
                ? new ListTypeNode(new NonNullTypeNode((INullableTypeNode)type))
                : new ListTypeNode(type);
        }

        /// <summary>
        /// Generates a string of the form "Create{EntityName}Input"
        /// </summary>
        /// <param name="typeName">Name of the entity</param>
        /// <returns>InputTypeName</returns>
        public static NameNode GenerateInputTypeName(string typeName)
        {
            return new($"{EntityActionOperation.Create}{typeName}Input");
        }

        /// <summary>
        /// Generate the `create` point/batch mutation fields for the GraphQL mutations for a given Object Definition
        /// ReturnEntityName can be different from dbEntityName in cases where user wants summary results returned (through the DBOperationResult entity)
        /// as opposed to full entity.
        /// </summary>
        /// <param name="name">Name of the GraphQL object to generate the create field for.</param>
        /// <param name="inputs">All known GraphQL input types.</param>
        /// <param name="objectTypeDefinitionNode">The GraphQL object type to generate for.</param>
        /// <param name="root">The GraphQL document root to find GraphQL schema items in.</param>
        /// <param name="databaseType">Type of database we're generating the field for.</param>
        /// <param name="entities">Runtime entities specification from config.</param>
        /// <param name="dbEntityName">Entity name specified in the runtime config.</param>
        /// <param name="returnEntityName">Name of type to be returned by the mutation.</param>
        /// <param name="rolesAllowedForMutation">Collection of role names allowed for action, to be added to authorize directive.</param>
        /// <returns>A GraphQL field definition named <c>create*EntityName*</c> to be attached to the Mutations type in the GraphQL schema.</returns>
        public static IEnumerable<FieldDefinitionNode> Build(
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            DatabaseType databaseType,
            RuntimeEntities entities,
            string dbEntityName,
            string returnEntityName,
            IEnumerable<string>? rolesAllowedForMutation = null)
        {
            List<FieldDefinitionNode> createMutationNodes = new();
            Entity entity = entities[dbEntityName];

            InputObjectTypeDefinitionNode input = GenerateCreateInputType(
                inputs: inputs,
                objectTypeDefinitionNode: objectTypeDefinitionNode,
                name: name,
                baseEntityName: name,
                definitions: root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(),
                databaseType: databaseType);

            // Create authorize directive denoting allowed roles
            List<DirectiveNode> fieldDefinitionNodeDirectives = new() { new(ModelDirectiveType.DirectiveName, new ArgumentNode("name", dbEntityName)) };

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForMutation,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            string singularName = GetPointCreateMutationNodeName(name.Value, entity);

            // Point insertion node.
            FieldDefinitionNode createOneNode = new(
                location: null,
                name: new NameNode(GetPointCreateMutationNodeName(name.Value, entity)),
                description: new StringValueNode($"Creates a new {singularName}"),
                arguments: new List<InputValueDefinitionNode> {
                new(
                    location : null,
                    new NameNode(MutationBuilder.ITEM_INPUT_ARGUMENT_NAME),
                    new StringValueNode($"Input representing all the fields for creating {name}"),
                    new NonNullTypeNode(new NamedTypeNode(input.Name)),
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                type: new NamedTypeNode(returnEntityName),
                directives: fieldDefinitionNodeDirectives
            );
            createMutationNodes.Add(createOneNode);

            // Multiple insertion node.
            FieldDefinitionNode createMultipleNode = new(
                location: null,
                name: new NameNode(GetMultipleCreateMutationNodeName(name.Value, entity)),
                description: new StringValueNode($"Creates multiple new {singularName}"),
                arguments: new List<InputValueDefinitionNode> {
                new(
                    location : null,
                    new NameNode(MutationBuilder.ARRAY_INPUT_ARGUMENT_NAME),
                    new StringValueNode($"Input representing all the fields for creating {name}"),
                    new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(input.Name))),
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                type: new NamedTypeNode(QueryBuilder.GeneratePaginationTypeName(GetDefinedSingularName(dbEntityName, entity))),
                directives: fieldDefinitionNodeDirectives
            );
            createMutationNodes.Add(createMultipleNode);
            return createMutationNodes;
        }

        /// <summary>
        /// Helper method to determine the name of the create one (or point create) mutation.
        /// </summary>
        public static string GetPointCreateMutationNodeName(string entityName, Entity entity)
        {
            string singularName = GetDefinedSingularName(entityName, entity);
            return $"{CREATE_MUTATION_PREFIX}{singularName}";
        }

        /// <summary>
        /// Helper method to determine the name of the create multiple mutation.
        /// If the singular and plural graphql names for the entity match, we suffix the name with 'Multiple' suffix to indicate
        /// that the mutation field is created to support insertion of multiple records in the top level entity.
        /// However if the plural and singular names are different, we use the plural name to construct the mutation.
        /// </summary>
        public static string GetMultipleCreateMutationNodeName(string entityName, Entity entity)
        {
            string singularName = GetDefinedSingularName(entityName, entity);
            string pluralName = GetDefinedPluralName(entityName, entity);
            string mutationName = singularName.Equals(pluralName) ? $"{singularName}{INSERT_MULTIPLE_MUTATION_SUFFIX}" : pluralName;
            return $"{CREATE_MUTATION_PREFIX}{mutationName}";
        }
    }
}
