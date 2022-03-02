using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    internal static class CreateMutationBuilder
    {
        private static InputObjectTypeDefinitionNode GenerateCreateInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            NameNode inputName = GenerateInputTypeName(name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => ExcludeFieldFromCreateInput(f))
                .Select(f =>
                {
                    if (!IsBuiltInType(f.Type))
                    {
                        string typeName = f.Type.NamedType().Name.Value;
                        HotChocolate.Language.IHasName def = definitions.First(d => d.Name.Value == typeName);
                        if (def is ObjectTypeDefinitionNode otdn)
                        {
                            return GetComplexInputType(inputs, definitions, f, typeName, otdn);
                        }
                    }

                    return GenerateSimpleInputType(name, f);
                });

            InputObjectTypeDefinitionNode input =
                new(
                    null,
                    inputName,
                    new StringValueNode($"Input type for creating {name}"),
                    new List<DirectiveNode>(),
                    inputFields.ToList()
                );

            inputs.Add(input.Name, input);
            return input;
        }

        private static bool ExcludeFieldFromCreateInput(FieldDefinitionNode f)
        {
            return f.Name.Value != "id";
        }

        private static InputValueDefinitionNode GenerateSimpleInputType(NameNode name, FieldDefinitionNode f)
        {
            return new(
                null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {GenerateInputTypeName(name.Value)}"),
                f.Type,
                null,
                f.Directives
            );
        }

        private static InputValueDefinitionNode GetComplexInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, IEnumerable<HotChocolate.Language.IHasName> definitions, FieldDefinitionNode f, string typeName, ObjectTypeDefinitionNode otdn)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateCreateInputType(inputs, otdn, f.Type.NamedType().Name, definitions);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            return new(
                null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {inputTypeName}"),
                new NonNullTypeNode(new NamedTypeNode(node.Name)), // todo - figure out how to properly walk the graph, so you can do [Foo!]!
                null,
                f.Directives
            );
        }

        private static NameNode GenerateInputTypeName(string typeName)
        {
            return new($"Create{typeName}Input");
        }

        public static FieldDefinitionNode Build(NameNode name, Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, DocumentNode root)
        {
            InputObjectTypeDefinitionNode input = GenerateCreateInputType(inputs, objectTypeDefinitionNode, name, root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>());

            return new(
                null,
                new NameNode($"create{name}"),
                new StringValueNode($"Creates a new {name}"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    null,
                    new NameNode("item"),
                    new StringValueNode($"Input representing all the fields for creating {name}"),
                    new NonNullTypeNode(new NamedTypeNode(input.Name)),
                    null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(name),
                new List<DirectiveNode>()
            );
        }
    }
}
