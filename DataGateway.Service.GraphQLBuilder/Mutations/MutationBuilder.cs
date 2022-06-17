using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    public static class MutationBuilder
    {
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

                    // Get Roles for mutation actionType on Entity, number of roles could be zero.
                    IEnumerable<string> rolesAllowedForMutation = IAuthorizationResolver.GetRolesForAction(dbEntityName, actionName: "create", entityPermissionsMap);
                    mutationFields.Add(CreateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, databaseType, entity, rolesAllowedForMutation));

                    rolesAllowedForMutation = IAuthorizationResolver.GetRolesForAction(dbEntityName, actionName: "update", entityPermissionsMap);
                    mutationFields.Add(UpdateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root, entity, databaseType, rolesAllowedForMutation));

                    rolesAllowedForMutation = IAuthorizationResolver.GetRolesForAction(dbEntityName, actionName: "delete", entityPermissionsMap);
                    mutationFields.Add(DeleteMutationBuilder.Build(name, objectTypeDefinitionNode, entity, rolesAllowedForMutation));
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(null, new NameNode("Mutation"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), mutationFields),
            };

            definitionNodes.AddRange(inputs.Values);
            return new(definitionNodes);
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
