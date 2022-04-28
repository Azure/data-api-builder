using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    internal static class DeleteMutationBuilder
    {
        public static FieldDefinitionNode Build(NameNode name, ObjectTypeDefinitionNode objectTypeDefinitionNode, Entity configEntity)
        {
            FieldDefinitionNode idField = FindPrimaryKeyField(objectTypeDefinitionNode);
            return new(
                null,
                new NameNode($"delete{FormatNameForObject(name, configEntity)}"),
                new StringValueNode($"Delete a {name}"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    null,
                    idField.Name,
                    new StringValueNode($"Id of the item to delete"),
                    new NonNullTypeNode(idField.Type.NamedType()),
                    null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(FormatNameForObject(name, configEntity)),
                new List<DirectiveNode>()
            );
        }
    }
}
