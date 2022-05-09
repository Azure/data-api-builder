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
                    string targetEntityName = RelationshipDirectiveType.Target(field);

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

        private static string GenerateObjectInputFilterName(INamedSyntaxNode node)
        {
            return GenerateObjectInputFilterName(node.Name.Value);
        }

        public static string GenerateObjectInputFilterName(string name)
        {
            return $"{name}FilterInput";
        }
    }
}
