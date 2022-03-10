using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Queries
{
    public static class QueryBuilder
    {
        public static DocumentNode Build(DocumentNode root)
        {
            List<FieldDefinitionNode> queryFields = new();
            List<ObjectTypeDefinitionNode> returnTypes = new();
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;

                    ObjectTypeDefinitionNode returnType = GenerateReturnType(name);
                    returnTypes.Add(returnType);

                    queryFields.Add(GenerateGetAllQuery(objectTypeDefinitionNode, name, returnType, inputTypes, root));
                    queryFields.Add(GenerateByPKQuery(objectTypeDefinitionNode, name));
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(location: null, new NameNode("Query"), description: null, new List<DirectiveNode>(), new List<NamedTypeNode>(), queryFields),
            };
            definitionNodes.AddRange(returnTypes);
            definitionNodes.AddRange(inputTypes.Values);
            return new(definitionNodes);
        }

        private static FieldDefinitionNode GenerateByPKQuery(ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name)
        {
            return new(
                location: null,
                new NameNode($"{FormatNameForField(name)}_by_pk"),
                new StringValueNode($"Get a {name} from the database by its ID/primary key"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    location : null,
                    new NameNode("id"),
                    description: null,
                    objectTypeDefinitionNode.Fields.First(f => f.Name.Value == "id").Type,
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(name),
                new List<DirectiveNode>()
            );
        }

        private static FieldDefinitionNode GenerateGetAllQuery(ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name, ObjectTypeDefinitionNode returnType, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes, DocumentNode root)
        {
            List<InputValueDefinitionNode> inputFields = GenerateInputFieldsForType(objectTypeDefinitionNode, inputTypes, root);

            string filterInputName = GenerateObjectInputFilterName(objectTypeDefinitionNode);

            if (!inputTypes.ContainsKey(objectTypeDefinitionNode.Name.Value))
            {
                inputTypes.Add(
                    objectTypeDefinitionNode.Name.Value,
                    new(
                        location: null,
                        new NameNode(filterInputName),
                        new StringValueNode($"Filter input for {objectTypeDefinitionNode.Name} GraphQL type"),
                        new List<DirectiveNode>(),
                        inputFields
                    )
                );
            }

            return new(
                location: null,
                Pluralize(name),
                new StringValueNode($"Get a list of all the {name} items from the database"),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(location : null, new NameNode("first"), description: null, new IntType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(location : null, new NameNode("continuation"), new StringValueNode("A continuation token from a previous query to continue through a paginated list"), new StringType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                    new(location : null, new NameNode("_filter"), new StringValueNode("Filter options for query"), new NamedTypeNode(filterInputName), defaultValue: null, new List<DirectiveNode>())
                },
                new NonNullTypeNode(new NamedTypeNode(returnType.Name)),
                new List<DirectiveNode>()
            );
        }

        private static List<InputValueDefinitionNode> GenerateInputFieldsForType(ObjectTypeDefinitionNode objectTypeDefinitionNode, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes, DocumentNode root)
        {
            List<InputValueDefinitionNode> inputFields = new();
            foreach (FieldDefinitionNode field in objectTypeDefinitionNode.Fields)
            {
                string fieldTypeName = field.Type.NamedType().Name.Value;
                if (!inputTypes.ContainsKey(fieldTypeName))
                {
                    if (IsBuiltInType(field.Type))
                    {
                        inputTypes.Add(fieldTypeName, StandardQueryInputs.InputTypes[fieldTypeName]);
                    }
                    else
                    {
                        IDefinitionNode fieldTypeNode = root.Definitions.First(d => d is HotChocolate.Language.IHasName named && named.Name.Value == fieldTypeName);

                        InputObjectTypeDefinitionNode inputObjectType =
                            fieldTypeNode switch
                            {
                                ObjectTypeDefinitionNode node when !inputTypes.ContainsKey(GenerateObjectInputFilterName(node)) => new(
                                    location: null,
                                    new NameNode(GenerateObjectInputFilterName(node)),
                                    new StringValueNode($"Filter input for {node.Name} GraphQL type"),
                                    new List<DirectiveNode>(),
                                    GenerateInputFieldsForType(node, inputTypes, root)),

                                ObjectTypeDefinitionNode node =>
                                    inputTypes[GenerateObjectInputFilterName(node)],

                                EnumTypeDefinitionNode node when !inputTypes.ContainsKey(GenerateObjectInputFilterName(node)) => new(
                                    location: null,
                                    new NameNode(GenerateObjectInputFilterName(node)),
                                    new StringValueNode($"Filter input for {node.Name} GraphQL type"),
                                    new List<DirectiveNode>(),
                                    new List<InputValueDefinitionNode> {
                                        new InputValueDefinitionNode(location : null, new NameNode("eq"), new StringValueNode("Equals"), new FloatType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                                        new InputValueDefinitionNode(location : null, new NameNode("neq"), new StringValueNode("Not Equals"), new FloatType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>())
                                    }),

                                EnumTypeDefinitionNode node =>
                                    inputTypes[GenerateObjectInputFilterName(node)],

                                _ => throw new InvalidOperationException($"Unable to work with type {fieldTypeName}")
                            };

                        inputTypes.Add(fieldTypeName, inputObjectType);
                    }
                }

                InputObjectTypeDefinitionNode inputType = inputTypes[fieldTypeName];

                inputFields.Add(new(location: null, field.Name, new StringValueNode($"Filter options for {field.Name}"), new NamedTypeNode(inputType.Name.Value), defaultValue: null, new List<DirectiveNode>()));
            }

            return inputFields;
        }

        private static string GenerateObjectInputFilterName(INamedSyntaxNode objectDefNode)
        {
            return $"{objectDefNode.Name}FilterInput";
        }

        private static ObjectTypeDefinitionNode GenerateReturnType(NameNode name)
        {
            return new(
                location: null,
                new NameNode($"{name}Connection"),
                new StringValueNode("The return object from a filter query that supports a continuation token for paging through results"),
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                new List<FieldDefinitionNode> {
                    new FieldDefinitionNode(
                        location: null,
                        new NameNode("items"),
                        new StringValueNode("The list of items that matched the filter"),
                        new List<InputValueDefinitionNode>(),
                        new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                        new List<DirectiveNode>()),
                    new FieldDefinitionNode(
                        location : null,
                        new NameNode("continuation"),
                        new StringValueNode("A continuation token to provide to subsequent pages of a query"),
                        new List<InputValueDefinitionNode>(),
                        new StringType().ToTypeNode(),
                        new List<DirectiveNode>()),
                    new FieldDefinitionNode(
                        location: null,
                        new NameNode("hasNextPage"),
                        new StringValueNode("Indicates if there are more pages of items to return"),
                        new List<InputValueDefinitionNode>(),
                        new BooleanType().ToTypeNode(),
                        new List<DirectiveNode>())
                }
            );
        }
    }
}
