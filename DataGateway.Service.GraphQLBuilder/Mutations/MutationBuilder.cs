using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    public static class MutationBuilder
    {
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
                    Entity entity = entities[dbEntityName];

                    AddMutations(dbEntityName, actionName: "create", entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entity, mutationFields);
                    AddMutations(dbEntityName, actionName: "update", entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entity, mutationFields);
                    AddMutations(dbEntityName, actionName: "delete", entityPermissionsMap, name, inputs, objectTypeDefinitionNode, root, databaseType, entity, mutationFields);
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(null, new NameNode("Mutation"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), mutationFields),
            };

            definitionNodes.AddRange(inputs.Values);
            return new(definitionNodes);
        }

        /// <summary>
        /// Helper function to create mutation definitions.
        /// </summary>
        /// <param name="dbEntityName"></param>
        /// <param name="actionName"></param>
        /// <param name="entityPermissionsMap"></param>
        /// <param name="name"></param>
        /// <param name="inputs"></param>
        /// <param name="objectTypeDefinitionNode"></param>
        /// <param name="root"></param>
        /// <param name="databaseType"></param>
        /// <param name="entity"></param>
        /// <param name="mutationFields"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void AddMutations(
            string dbEntityName,
            string actionName,
            Dictionary<string, EntityMetadata>? entityPermissionsMap,
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            DatabaseType databaseType,
            Entity entity,
            List<FieldDefinitionNode> mutationFields
            )
        {
            IEnumerable<string> rolesAllowedForMutation = IAuthorizationResolver.GetRolesForAction(dbEntityName, actionName: actionName, entityPermissionsMap);
            if (rolesAllowedForMutation.Count() > 0)
            {
                switch (actionName)
                {
                    case "create":
                        mutationFields.Add(CreateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, databaseType, entity, rolesAllowedForMutation));
                        break;
                    case "update":
                        mutationFields.Add(UpdateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, entity, databaseType, rolesAllowedForMutation));
                        break;
                    case "delete":
                        mutationFields.Add(DeleteMutationBuilder.Build(name, objectTypeDefinitionNode, entity, databaseType, rolesAllowedForMutation));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(paramName: "actionName", message: "Invalid argument value provided.");
                }
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
