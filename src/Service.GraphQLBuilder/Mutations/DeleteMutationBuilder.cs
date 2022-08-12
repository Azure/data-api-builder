using Azure.DataApiBuilder.Config;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations
{
    public static class DeleteMutationBuilder
    {
        /// <summary>
        /// Generate the `delete` mutation field for the GraphQL mutations for a given Object Definition
        /// </summary>
        /// <param name="name">Name of the GraphQL object to generate the delete field for.</param>
        /// <param name="objectTypeDefinitionNode">The GraphQL object type to generate for.</param>
        /// <param name="configEntity">Entity definition</param>
        /// <param name="rolesAllowedForMutation">Collection of role names allowed for action, to be added to authorize directive.</param>
        /// <returns>A GraphQL field definition named <c>delete*EntityName*</c> to be attached to the Mutations type in the GraphQL schema.</returns>
        public static FieldDefinitionNode Build(NameNode name, ObjectTypeDefinitionNode objectTypeDefinitionNode, Entity configEntity, DatabaseType databaseType, IEnumerable<string>? rolesAllowedForMutation = null)
        {
            List<FieldDefinitionNode> idFields = FindPrimaryKeyFields(objectTypeDefinitionNode, databaseType);
            string description;
            if (idFields.Count > 1)
            {
                description = "One of the ids of the item being deleted.";
            }
            else
            {
                description = "The ID of the item being deleted.";
            }

            List<InputValueDefinitionNode> inputValues = new();
            foreach (FieldDefinitionNode idField in idFields)
            {
                inputValues.Add(new InputValueDefinitionNode(
                    location: null,
                    idField.Name,
                    new StringValueNode(description),
                    new NonNullTypeNode(idField.Type.NamedType()),
                    defaultValue: null,
                    new List<DirectiveNode>()));
            }

            // Create authorize directive denoting allowed roles
            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForMutation,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            return new(
                null,
                new NameNode($"delete{FormatNameForObject(name, configEntity)}"),
                new StringValueNode($"Delete a {name}"),
                inputValues,
                new NamedTypeNode(FormatNameForObject(name, configEntity)),
                fieldDefinitionNodeDirectives
            );
        }
    }
}
