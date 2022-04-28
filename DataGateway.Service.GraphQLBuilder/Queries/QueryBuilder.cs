using Azure.DataGateway.Config;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Queries
{
    public static class QueryBuilder
    {
        public const string PAGINATION_FIELD_NAME = "items";
        public const string PAGINATION_TOKEN_FIELD_NAME = "after";
        public const string HAS_NEXT_PAGE_FIELD_NAME = "hasNextPage";
        public const string PAGE_START_ARGUMENT_NAME = "first";
        public const string PAGINATION_OBJECT_TYPE_SUFFIX = "Connection";

        public static DocumentNode Build(DocumentNode root, IDictionary<string, Entity> entities)
        {
            List<FieldDefinitionNode> queryFields = new();
            List<ObjectTypeDefinitionNode> returnTypes = new();
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;
                    Entity entity = entities[name.Value];

                    ObjectTypeDefinitionNode returnType = GenerateReturnType(name);
                    returnTypes.Add(returnType);

                    queryFields.Add(GenerateGetAllQuery(objectTypeDefinitionNode, name, returnType, inputTypes, root, entity));
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
            FieldDefinitionNode primaryKeyField = FindPrimaryKeyField(objectTypeDefinitionNode);
            return new(
                location: null,
                new NameNode($"{FormatNameForField(name)}_by_pk"),
                new StringValueNode($"Get a {name} from the database by its ID/primary key"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    location : null,
                    primaryKeyField.Name,
                    description: null,
                    primaryKeyField.Type,
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(name),
                new List<DirectiveNode>()
            );
        }

        private static FieldDefinitionNode GenerateGetAllQuery(
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            ObjectTypeDefinitionNode returnType,
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes,
            DocumentNode root,
            Entity entity)
        {
            List<InputValueDefinitionNode> inputFields = GenerateInputFieldsForType(objectTypeDefinitionNode, inputTypes, root);

            string filterInputName = GenerateObjectInputFilterName(objectTypeDefinitionNode);

            if (!inputTypes.ContainsKey(objectTypeDefinitionNode.Name.Value))
            {
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
                Pluralize(name, entity),
                new StringValueNode($"Get a list of all the {name} items from the database"),
                new List<InputValueDefinitionNode> {
                    new(location : null, new NameNode(PAGE_START_ARGUMENT_NAME), description: null, new IntType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                    new(location : null, new NameNode(PAGINATION_TOKEN_FIELD_NAME), new StringValueNode("A pagination token from a previous query to continue through a paginated list"), new StringType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                    new(location : null, new NameNode("_filter"), new StringValueNode("Filter options for query"), new NamedTypeNode(filterInputName), defaultValue: null, new List<DirectiveNode>()),
                    new(location : null, new NameNode("_filterOData"), new StringValueNode("Filter options for query expressed as OData query language"), new StringType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>())
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

        public static ObjectType PaginationTypeToModelType(ObjectType underlyingFieldType, IReadOnlyCollection<INamedType> types)
        {
            IEnumerable<ObjectType> modelTypes = types.Where(t => t is ObjectType)
                .Cast<ObjectType>()
                .Where(IsModelType);

            return modelTypes.First(t => t.Name.Value == underlyingFieldType.Name.Value.Replace(PAGINATION_OBJECT_TYPE_SUFFIX, ""));
        }

        public static bool IsPaginationType(ObjectType objectType)
        {
            return objectType.Name.Value.EndsWith(PAGINATION_OBJECT_TYPE_SUFFIX);
        }

        private static string GenerateObjectInputFilterName(INamedSyntaxNode objectDefNode)
        {
            return $"{objectDefNode.Name}FilterInput";
        }

        private static ObjectTypeDefinitionNode GenerateReturnType(NameNode name)
        {
            return new(
                location: null,
<<<<<<< HEAD
<<<<<<< HEAD
                new NameNode($"{name}{PAGINATION_OBJECT_TYPE_SUFFIX}"),
=======
                new NameNode($"{name}Connection"),
>>>>>>> 56bb3c3 (rollback of the endCursor to after as field name)
=======
                new NameNode($"{name}{PAGINATION_OBJECT_TYPE_SUFFIX}"),
>>>>>>> 05febf9 (WIP)
                new StringValueNode("The return object from a filter query that supports a pagination token for paging through results"),
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                new List<FieldDefinitionNode> {
                    new FieldDefinitionNode(
                        location: null,
                        new NameNode(PAGINATION_FIELD_NAME),
                        new StringValueNode("The list of items that matched the filter"),
                        new List<InputValueDefinitionNode>(),
                        new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                        new List<DirectiveNode>()),
                    new FieldDefinitionNode(
                        location : null,
                        new NameNode(PAGINATION_TOKEN_FIELD_NAME),
                        new StringValueNode("A pagination token to provide to subsequent pages of a query"),
                        new List<InputValueDefinitionNode>(),
                        new StringType().ToTypeNode(),
                        new List<DirectiveNode>()),
                    new FieldDefinitionNode(
                        location: null,
                        new NameNode(HAS_NEXT_PAGE_FIELD_NAME),
                        new StringValueNode("Indicates if there are more pages of items to return"),
                        new List<InputValueDefinitionNode>(),
                        new NonNullType(new BooleanType()).ToTypeNode(),
                        new List<DirectiveNode>())
                }
            );
        }
    }
}
