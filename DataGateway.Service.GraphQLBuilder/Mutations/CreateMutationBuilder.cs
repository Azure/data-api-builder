using Azure.DataGateway.Config;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    public static class CreateMutationBuilder
    {
        public const string INPUT_ARGUMENT_NAME = "item";
        private static InputObjectTypeDefinitionNode GenerateCreateInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name, IEnumerable<HotChocolate.Language.IHasName> definitions, DatabaseType databaseType)
        {
            NameNode inputName = GenerateInputTypeName(name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => FieldAllowedOnCreateInput(f, databaseType, definitions))
                .Select(f =>
                {
                    if (!IsBuiltInType(f.Type))
                    {
                        string typeName = f.Type.NamedType().Name.Value;
                        HotChocolate.Language.IHasName def = definitions.First(d => d.Name.Value == typeName);
                        if (def is ObjectTypeDefinitionNode otdn)
                        {
                            return GetComplexInputType(inputs, definitions, f, typeName, otdn, databaseType);
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

        /// <summary>
        /// This method is used to determine if a field is allowed to be sent from the client in a Create mutation (eg, id field is not settable during create).
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="databaseType">The type of database to generate for</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool FieldAllowedOnCreateInput(FieldDefinitionNode field, DatabaseType databaseType, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            if (IsBuiltInType(field.Type))
            {
                // With Cosmos we need to have the id field included, as Cosmos doesn't do auto-increment or anything
                return databaseType switch
                {
                    DatabaseType.cosmos => true,
                    _ => field.Name.Value != "id"
                };
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);
            // When creating, you don't need to provide the data for nested models, but you will for other nested types
            if (definition != null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType))
            {
                return false;
            }

            return true;
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

        private static InputValueDefinitionNode GetComplexInputType(Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, IEnumerable<HotChocolate.Language.IHasName> definitions, FieldDefinitionNode f, string typeName, ObjectTypeDefinitionNode otdn, DatabaseType databaseType)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateCreateInputType(inputs, otdn, f.Type.NamedType().Name, definitions, databaseType);
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
            return new($"Create{FormatNameForObject(typeName)}Input");
        }

        public static FieldDefinitionNode Build(NameNode name, Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs, ObjectTypeDefinitionNode objectTypeDefinitionNode, DocumentNode root, DatabaseType databaseType)
        {
            InputObjectTypeDefinitionNode input = GenerateCreateInputType(inputs, objectTypeDefinitionNode, name, root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(), databaseType);

            return new(
                null,
                new NameNode($"create{FormatNameForObject(name)}"),
                new StringValueNode($"Creates a new {name}"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    null,
                    new NameNode(INPUT_ARGUMENT_NAME),
                    new StringValueNode($"Input representing all the fields for creating {name}"),
                    new NonNullTypeNode(new NamedTypeNode(input.Name)),
                    null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(FormatNameForObject(name)),
                new List<DirectiveNode>()
            );
        }
    }
}
