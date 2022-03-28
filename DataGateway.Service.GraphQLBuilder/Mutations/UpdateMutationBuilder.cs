using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    internal static class UpdateMutationBuilder
    {
        /// <summary>
        /// This method is used to determine if a field is allowed to be sent from the client in a Update mutation (eg, id field is not settable during update).
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool FieldAllowedOnUpdateInput(FieldDefinitionNode field, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            if (IsBuiltInType(field.Type))
            {
                return field.Name.Value != "id";
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);
            // When updating, you don't need to provide the data for nested models, but you will for other nested types
            if (definition != null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType))
            {
                return false;
            }

            return true;
        }

        private static InputObjectTypeDefinitionNode GenerateUpdateInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            NameNode inputName = GenerateInputTypeName(name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => FieldAllowedOnUpdateInput(f, definitions))
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
                    location: null,
                    inputName,
                    new StringValueNode($"Input type for creating {name}"),
                    new List<DirectiveNode>(),
                    inputFields.ToList()
                );

            inputs.Add(input.Name, input);
            return input;
        }

        private static InputValueDefinitionNode GenerateSimpleInputType(NameNode name, FieldDefinitionNode f)
        {
            return new(
                location: null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {GenerateInputTypeName(name.Value)}"),
                f.Type,
                defaultValue: null,
                f.Directives
            );
        }

        private static InputValueDefinitionNode GetComplexInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, IEnumerable<HotChocolate.Language.IHasName> definitions, FieldDefinitionNode f, string typeName, ObjectTypeDefinitionNode otdn)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateUpdateInputType(inputs, otdn, f.Type.NamedType().Name, definitions);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            return new(
                location: null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {inputTypeName}"),
                new NonNullTypeNode(new NamedTypeNode(node.Name)), // todo - figure out how to properly walk the graph, so you can do [Foo!]!
                defaultValue: null,
                f.Directives
            );
        }

        private static NameNode GenerateInputTypeName(string typeName)
        {
            return new($"Update{typeName}Input");
        }

        public static FieldDefinitionNode Build(NameNode name, Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, DocumentNode root)
        {
            InputObjectTypeDefinitionNode input = GenerateUpdateInputType(inputs, objectTypeDefinitionNode, name, root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>());

            FieldDefinitionNode idField = FindIdField(objectTypeDefinitionNode);

            return new(
                location: null,
                new NameNode($"update{name}"),
                new StringValueNode($"Updates a {name}"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    location: null,
                    idField.Name,
                    new("The ID of the item being updated"),
                    new NonNullTypeNode(idField.Type.NamedType()),
                    defaultValue: null,
                    new List<DirectiveNode>()),
                new InputValueDefinitionNode(
                    location: null,
                    new NameNode("item"),
                    new StringValueNode($"Input representing all the fields for updating {name}"),
                    new NonNullTypeNode(new NamedTypeNode(input.Name)),
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(name),
                new List<DirectiveNode>()
            );
        }
    }
}
