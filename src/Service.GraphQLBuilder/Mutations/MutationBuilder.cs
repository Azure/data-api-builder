// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using HotChocolate.Language;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations
{
    public static class MutationBuilder
    {
        /// <summary>
        /// Within a mutation operation, item represents the field holding the metadata
        /// used to mutate the underlying database object record.
        /// The item field's metadata is of type OperationEntityInput
        /// i.e. CreateBookInput
        /// </summary>
        public const string INPUT_ARGUMENT_NAME = "item";

        /// <summary>
        /// Creates a DocumentNode containing FieldDefinitionNodes representing mutations
        /// </summary>
        /// <param name="root">Root of GraphQL schema</param>
        /// <param name="databaseType">i.e. MSSQL, MySQL, Postgres, Cosmos</param>
        /// <param name="entities">Map of entityName -> EntityMetadata</param>
        /// <returns></returns>
        public static DocumentNode Build(
            DocumentNode root,
            DatabaseType databaseType,
            RuntimeEntities entities,
            Dictionary<string, EntityMetadata>? entityPermissionsMap = null)
        {
            List<FieldDefinitionNode> mutationFields = new();
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;
                    string dbEntityName = ObjectTypeToEntityName(objectTypeDefinitionNode);

                    // For stored procedures, only one mutation is created in the schema
                    // unlike table/views where we create one for each CUD operation.
                    if (entities[dbEntityName].Source.Type is EntityType.StoredProcedure)
                    {
                        // check graphql sp config
                        string entityName = ObjectTypeToEntityName(objectTypeDefinitionNode);
                        Entity entity = entities[entityName];
                        bool isSPDefinedAsMutation = entity.GraphQL.Operation is GraphQLOperation.Mutation;

                        if (isSPDefinedAsMutation)
                        {
                            AddMutationsForStoredProcedure(dbEntityName, entityPermissionsMap, name, entities, mutationFields);
                        }
                    }
                    else
                    {
                        AddMutations(dbEntityName, operation: EntityActionOperation.Create, entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entities, mutationFields);
                        AddMutations(dbEntityName, operation: EntityActionOperation.Update, entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entities, mutationFields);
                        AddMutations(dbEntityName, operation: EntityActionOperation.Delete, entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entities, mutationFields);
                    }
                }
            }

            List<IDefinitionNode> definitionNodes = new();

            // Only add mutation type if we have fields authorized for mutation operations.
            // Per GraphQL Specification (Oct 2021) https://spec.graphql.org/October2021/#sec-Root-Operation-Types
            // "The mutation root operation type is optional; if it is not provided, the service does not support mutations."
            if (mutationFields.Count() > 0)
            {
                definitionNodes.Add(new ObjectTypeDefinitionNode(null, new NameNode("Mutation"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), mutationFields));
                definitionNodes.AddRange(inputs.Values);
            }

            return new(definitionNodes);
        }

        /// <summary>
        /// Helper function to create mutation definitions.
        /// </summary>
        /// <param name="dbEntityName">Represents the top-level entity name in runtime config.</param>
        /// <param name="operation"></param>
        /// <param name="entityPermissionsMap"></param>
        /// <param name="name"></param>
        /// <param name="inputs"></param>
        /// <param name="objectTypeDefinitionNode"></param>
        /// <param name="root"></param>
        /// <param name="databaseType"></param>
        /// <param name="entities"></param>
        /// <param name="mutationFields"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void AddMutations(
            string dbEntityName,
            EntityActionOperation operation,
            Dictionary<string, EntityMetadata>? entityPermissionsMap,
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            DatabaseType databaseType,
            RuntimeEntities entities,
            List<FieldDefinitionNode> mutationFields
            )
        {
            IEnumerable<string> rolesAllowedForMutation = IAuthorizationResolver.GetRolesForOperation(dbEntityName, operation: operation, entityPermissionsMap);
            if (rolesAllowedForMutation.Count() > 0)
            {
                switch (operation)
                {
                    case EntityActionOperation.Create:
                        mutationFields.Add(CreateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, databaseType, entities, dbEntityName, rolesAllowedForMutation));
                        break;
                    case EntityActionOperation.Update:
                        mutationFields.Add(UpdateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, entities, dbEntityName, databaseType, rolesAllowedForMutation));
                        break;
                    case EntityActionOperation.Delete:
                        mutationFields.Add(DeleteMutationBuilder.Build(name, objectTypeDefinitionNode, entities[dbEntityName], databaseType, rolesAllowedForMutation));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(paramName: "action", message: "Invalid argument value provided.");
                }
            }
        }

        /// <summary>
        /// Uses the provided input arguments to add a stored procedure to the GraphQL schema as a mutation field when
        /// at least one role with permission to execute is defined in the stored procedure's entity definition within the runtime config.
        /// </summary>
        private static void AddMutationsForStoredProcedure(
            string dbEntityName,
            Dictionary<string, EntityMetadata>? entityPermissionsMap,
            NameNode name,
            RuntimeEntities entities,
            List<FieldDefinitionNode> mutationFields
            )
        {
            IEnumerable<string> rolesAllowedForMutation = IAuthorizationResolver.GetRolesForOperation(dbEntityName, operation: EntityActionOperation.Execute, entityPermissionsMap);
            if (rolesAllowedForMutation.Count() > 0)
            {
                mutationFields.Add(GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema(name, entities[dbEntityName], rolesAllowedForMutation));
            }
        }

        /// <summary>
        /// Evaluates the provided mutation name to determine the operation type.
        /// e.g. createEntity is resolved to Operation.Create
        /// </summary>
        /// <param name="inputTypeName">Mutation name</param>
        /// <returns>Operation</returns>
        public static EntityActionOperation DetermineMutationOperationTypeBasedOnInputType(string inputTypeName)
        {
            return inputTypeName switch
            {
                string s when s.StartsWith(EntityActionOperation.Execute.ToString(), StringComparison.OrdinalIgnoreCase) => EntityActionOperation.Execute,
                string s when s.StartsWith(EntityActionOperation.Create.ToString(), StringComparison.OrdinalIgnoreCase) => EntityActionOperation.Create,
                string s when s.StartsWith(EntityActionOperation.Update.ToString(), StringComparison.OrdinalIgnoreCase) => EntityActionOperation.UpdateGraphQL,
                _ => EntityActionOperation.Delete
            };
        }
    }
}
