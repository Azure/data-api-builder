using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
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
        public const string FILTER_FIELD_NAME = "_filter";
        public const string ODATA_FILTER_FIELD_NAME = "_filterOData";

        public static DocumentNode Build(DocumentNode root, IDictionary<string, Entity> entities, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            List<FieldDefinitionNode> queryFields = new();
            List<ObjectTypeDefinitionNode> returnTypes = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;
                    string entityName = ObjectTypeToEntityName(objectTypeDefinitionNode);
                    Entity entity = entities[entityName];

                    ObjectTypeDefinitionNode returnType = GenerateReturnType(name);
                    returnTypes.Add(returnType);

                    queryFields.Add(GenerateGetAllQuery(objectTypeDefinitionNode, name, returnType, inputTypes, entity));
                    queryFields.Add(GenerateByPKQuery(objectTypeDefinitionNode, name));
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(location: null, new NameNode("Query"), description: null, new List<DirectiveNode>(), new List<NamedTypeNode>(), queryFields),
            };
            definitionNodes.AddRange(returnTypes);
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
            Entity entity)
        {
            string filterInputName = InputTypeBuilder.GenerateObjectInputFilterName(objectTypeDefinitionNode.Name.Value);

            if (!inputTypes.ContainsKey(objectTypeDefinitionNode.Name.Value))
            {
                InputTypeBuilder.GenerateInputTypeForObjectType(objectTypeDefinitionNode, inputTypes);
            }

            // Query field for the parent object type
            // Generates a file like:
            //    books(first: Int, after: String, _filter: BooksFilterInput, _filterOData: String): BooksConnection!
            return new(
                location: null,
                Pluralize(name, entity),
                new StringValueNode($"Get a list of all the {name} items from the database"),
                QueryArgumentsForField(filterInputName),
                new NonNullTypeNode(new NamedTypeNode(returnType.Name)),
                new List<DirectiveNode>()
            );
        }

        private static List<InputValueDefinitionNode> QueryArgumentsForField(string filterInputName)
        {
            return new()
            {
                new(location: null, new NameNode(PAGE_START_ARGUMENT_NAME), description: new StringValueNode("The number of items to return from the page start point"), new IntType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                new(location: null, new NameNode(PAGINATION_TOKEN_FIELD_NAME), new StringValueNode("A pagination token from a previous query to continue through a paginated list"), new StringType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                new(location: null, new NameNode(FILTER_FIELD_NAME), new StringValueNode("Filter options for query"), new NamedTypeNode(filterInputName), defaultValue: null, new List<DirectiveNode>()),
                new(location: null, new NameNode(ODATA_FILTER_FIELD_NAME), new StringValueNode("Filter options for query expressed as OData query language"), new StringType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>())
            };
        }

        public static ObjectTypeDefinitionNode AddQueryArgumentsForRelationships(ObjectTypeDefinitionNode node, Entity entity, Dictionary<string, InputObjectTypeDefinitionNode> inputObjects)
        {
            if (entity.Relationships is null)
            {
                return node;
            }

            foreach ((string relationshipName, Relationship relationship) in entity.Relationships)
            {
                if (relationship.Cardinality != Cardinality.Many)
                {
                    continue;
                }

                FieldDefinitionNode field = node.Fields.First(f => f.Name.Value == relationshipName);

                DirectiveNode directive = field.Directives.First(d => d.Name.Value == RelationshipDirectiveType.DirectiveName);

                InputObjectTypeDefinitionNode input = inputObjects[(string)directive.Arguments.First(a => a.Name.Value == "target").Value.Value!];

                List<InputValueDefinitionNode> args = QueryArgumentsForField(input.Name.Value);

                List<FieldDefinitionNode> fields = node.Fields.ToList();
                fields[fields.FindIndex(f => f.Name == field.Name)] = field.WithArguments(args);

                node = node.WithFields(fields);
            }

            return node;
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

        public static bool IsPaginationType(NamedTypeNode objectType)
        {
            return objectType.Name.Value.EndsWith(PAGINATION_OBJECT_TYPE_SUFFIX);
        }

        private static ObjectTypeDefinitionNode GenerateReturnType(NameNode name)
        {
            return new(
                location: null,
                new NameNode(GeneratePaginationTypeName(name.Value)),
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

        public static string GeneratePaginationTypeName(string name)
        {
            return $"{name}{PAGINATION_OBJECT_TYPE_SUFFIX}";
        }
    }
}
