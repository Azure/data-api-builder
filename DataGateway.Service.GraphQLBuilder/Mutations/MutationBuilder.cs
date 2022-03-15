using System.Collections.Generic;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    public static class MutationBuilder
    {
        public static DocumentNode Build(DocumentNode root)
        {
            List<FieldDefinitionNode> mutationFields = new();
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;

                    mutationFields.Add(CreateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root));
                    mutationFields.Add(UpdateMutationBuilder.Build(name, inputs, objectTypeDefinitionNode, root));
                    mutationFields.Add(DeleteMutationBuilder.Build(name, objectTypeDefinitionNode));
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(null, new NameNode("Mutation"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), mutationFields),
            };
            definitionNodes.AddRange(inputs.Values);
            return new(definitionNodes);
        }
    }
}
