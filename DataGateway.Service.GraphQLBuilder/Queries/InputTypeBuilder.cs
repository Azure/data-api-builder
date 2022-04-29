using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Queries
{
    public static class InputTypeBuilder
    {
        public static void GenerateInputTypeForObjectType(
            ObjectTypeDefinitionNode node,
            IDictionary<string, InputObjectTypeDefinitionNode> inputTypes
        )
        {
            List<InputValueDefinitionNode> inputFields = GenerateInputFieldsForBuiltInFields(node, inputTypes);
            string filterInputName = GenerateObjectInputFilterName(node);

            inputFields.Add(new(
                    location: null,
                    new("and"),
                    new("Conditions to be treated as AND operations"),
                    new ListTypeNode(new NamedTypeNode(filterInputName)),
                    defaultValue: null,
                    new List<DirectiveNode>()));

            inputFields.Add(new(
                location: null,
                new("or"),
                new("Conditions to be treated as OR operations"),
                new ListTypeNode(new NamedTypeNode(filterInputName)),
                defaultValue: null,
                new List<DirectiveNode>()));

            inputTypes.Add(
                node.Name.Value,
                new(
                    location: null,
                    new NameNode(filterInputName),
                    new StringValueNode($"Filter input for {node.Name} GraphQL type"),
                    new List<DirectiveNode>(),
                    inputFields
                )
            );
        }

        private static List<InputValueDefinitionNode> GenerateInputFieldsForBuiltInFields(
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            IDictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            List<InputValueDefinitionNode> inputFields = new();
            foreach (FieldDefinitionNode field in objectTypeDefinitionNode.Fields)
            {
                string fieldTypeName = field.Type.NamedType().Name.Value;
                if (IsBuiltInType(field.Type))
                {
                    if (!inputTypes.ContainsKey(fieldTypeName))
                    {
                        inputTypes.Add(fieldTypeName, StandardQueryInputs.InputTypes[fieldTypeName]);
                    }

                    InputObjectTypeDefinitionNode inputType = inputTypes[fieldTypeName];

                    inputFields.Add(
                        new(
                            location: null,
                            field.Name,
                            new StringValueNode($"Filter options for {field.Name}"),
                            new NamedTypeNode(inputType.Name.Value),
                            defaultValue: null,
                            new List<DirectiveNode>())
                        );
                }
                else
                {
                    DirectiveNode relationshipDirective = field.Directives.First(f => f.Name.Value == RelationshipDirectiveType.DirectiveName);
                    string targetEntityName = (string)relationshipDirective.Arguments.First(a => a.Name.Value == "target").Value.Value!;

                    inputFields.Add(
                        new(
                            location: null,
                            field.Name,
                            new StringValueNode($"Filter options for {field.Name}"),
                            new NamedTypeNode(GenerateObjectInputFilterName(targetEntityName)),
                            defaultValue: null,
                            new List<DirectiveNode>())
                        );
                }

            }

            return inputFields;
        }

        //private static InputObjectTypeDefinitionNode GenerateComplexInputObject(Dictionary<string, InputObjectTypeDefinitionNode> inputTypes, DocumentNode root, string fieldTypeName)
        //{
        //    IDefinitionNode fieldTypeNode = root.Definitions.First(d => d is HotChocolate.Language.IHasName named && named.Name.Value == fieldTypeName);

        //    return
        //        fieldTypeNode switch
        //        {
        //            ObjectTypeDefinitionNode node when !inputTypes.ContainsKey(GenerateObjectInputFilterName(node)) => new(
        //                location: null,
        //                new NameNode(GenerateObjectInputFilterName(node)),
        //                new StringValueNode($"Filter input for {node.Name} GraphQL type"),
        //                new List<DirectiveNode>(),
        //                GenerateInputFieldsForType(node, inputTypes, root)),

        //            ObjectTypeDefinitionNode node =>
        //                inputTypes[GenerateObjectInputFilterName(node)],

        //            EnumTypeDefinitionNode node when !inputTypes.ContainsKey(GenerateObjectInputFilterName(node)) => new(
        //                location: null,
        //                new NameNode(GenerateObjectInputFilterName(node)),
        //                new StringValueNode($"Filter input for {node.Name} GraphQL type"),
        //                new List<DirectiveNode>(),
        //                new List<InputValueDefinitionNode> {
        //                    new InputValueDefinitionNode(location : null, new NameNode("eq"), new StringValueNode("Equals"), new FloatType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
        //                    new InputValueDefinitionNode(location : null, new NameNode("neq"), new StringValueNode("Not Equals"), new FloatType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>())
        //                }),

        //            EnumTypeDefinitionNode node =>
        //                inputTypes[GenerateObjectInputFilterName(node)],

        //            _ => throw new InvalidOperationException($"Unable to work with type {fieldTypeName}")
        //        };
        //}

        private static string GenerateObjectInputFilterName(INamedSyntaxNode node)
        {
            return GenerateObjectInputFilterName(node.Name.Value);
        }

        private static string GenerateObjectInputFilterName(string name)
        {
            return $"{name}FilterInput";
        }
    }
}
