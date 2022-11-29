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
            IDictionary<string, Entity> entities,
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

                    // We will only create a single mutation query for stored procedure
                    // unlike table/views where we create one for each CUD operation.
                    if (entities[dbEntityName].ObjectType is SourceType.StoredProcedure)
                    {
                        // If the role has actions other than READ, a schema for mutation will be generated.
                        Operation storedProcedureOperation = GetOperationTypeForStoredProcedure(dbEntityName, entityPermissionsMap);
                        if (storedProcedureOperation is not Operation.Read)
                        {
                            AddMutationsForStoredProcedure(dbEntityName, storedProcedureOperation, entityPermissionsMap, name, entities, mutationFields);
                        }

                        continue;
                    }

                    AddMutations(dbEntityName, operation: Operation.Create, entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entities, mutationFields);
                    AddMutations(dbEntityName, operation: Operation.Update, entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entities, mutationFields);
                    AddMutations(dbEntityName, operation: Operation.Delete, entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entities, mutationFields);
                }
            }

            List<IDefinitionNode> definitionNodes = new();
            // Only add mutation type if we have fields authorized for mutation operations
            if (mutationFields.Count() > 0)
            {
                definitionNodes.Add(new ObjectTypeDefinitionNode(null, new NameNode("Mutation"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), mutationFields));
                definitionNodes.AddRange(inputs.Values);
            }

            return new(definitionNodes);
        }

        /// <summary>
        /// Tries to fetch the Operation Type for Stored Procedure.
        /// Stored Procedure currently support at most 1 CRUD operation at a time.
        /// This check is done during initialization as part of config validation.
        /// </summary>
        private static Operation GetOperationTypeForStoredProcedure(
            string dbEntityName,
            Dictionary<string, EntityMetadata>? entityPermissionsMap)
        {
            List<Operation> operations = entityPermissionsMap![dbEntityName].OperationToRolesMap.Keys.ToList();

            // Stored Procedure will have only CRUD action.
            return operations.First();
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
            Operation operation,
            Dictionary<string, EntityMetadata>? entityPermissionsMap,
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            DatabaseType databaseType,
            IDictionary<string, Entity> entities,
            List<FieldDefinitionNode> mutationFields
            )
        {
            IEnumerable<string> rolesAllowedForMutation = IAuthorizationResolver.GetRolesForOperation(dbEntityName, operation: operation, entityPermissionsMap);
            if (rolesAllowedForMutation.Count() > 0)
            {
                switch (operation)
                {
                    case Operation.Create:
                        mutationFields.Add(CreateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, databaseType, entities, dbEntityName, rolesAllowedForMutation));
                        break;
                    case Operation.Update:
                        mutationFields.Add(UpdateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, entities, dbEntityName, databaseType, rolesAllowedForMutation));
                        break;
                    case Operation.Delete:
                        mutationFields.Add(DeleteMutationBuilder.Build(name, objectTypeDefinitionNode, entities[dbEntityName], databaseType, rolesAllowedForMutation));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(paramName: "action", message: "Invalid argument value provided.");
                }
            }
        }

        /// <summary>
        /// Helper method to add the new StoredProcedure in the mutation fields
        /// of GraphQL Schema
        /// </summary>
        private static void AddMutationsForStoredProcedure(
            string dbEntityName,
            Operation operation,
            Dictionary<string, EntityMetadata>? entityPermissionsMap,
            NameNode name,
            IDictionary<string, Entity> entities,
            List<FieldDefinitionNode> mutationFields
            )
        {
            IEnumerable<string> rolesAllowedForMutation = IAuthorizationResolver.GetRolesForOperation(dbEntityName, operation: operation, entityPermissionsMap);
            if (rolesAllowedForMutation.Count() > 0)
            {
                mutationFields.Add(GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema(name, entities[dbEntityName], rolesAllowedForMutation));
            }
        }

        public static Operation DetermineMutationOperationTypeBasedOnInputType(string inputTypeName)
        {
            return inputTypeName switch
            {
                string s when s.StartsWith(Operation.Create.ToString(), StringComparison.OrdinalIgnoreCase) => Operation.Create,
                string s when s.StartsWith(Operation.Update.ToString(), StringComparison.OrdinalIgnoreCase) => Operation.UpdateGraphQL,
                _ => Operation.Delete
            };
        }
    }
}
