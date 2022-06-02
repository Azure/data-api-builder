using Azure.DataGateway.Config;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    internal static class DeleteMutationBuilder
    {
        public static FieldDefinitionNode Build(NameNode name, ObjectTypeDefinitionNode objectTypeDefinitionNode, Entity configEntity, DatabaseType databaseType)
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

            return new(
                null,
                new NameNode($"delete{FormatNameForObject(name, configEntity)}"),
                new StringValueNode($"Delete a {name}"),
                inputValues,
                new NamedTypeNode(FormatNameForObject(name, configEntity)),
                new List<DirectiveNode>()
            );
        }
    }
}
