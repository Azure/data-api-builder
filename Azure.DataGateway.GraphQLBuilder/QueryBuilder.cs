using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.GraphQLBuilder.Utils;

namespace Azure.DataGateway.GraphQLBuilder
{
    public static class QueryBuilder
    {
        public static DocumentNode Build(DocumentNode root)
        {
            List<FieldDefinitionNode> queryFields = new();
            List<ObjectTypeDefinitionNode> returnTypes = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;

                    ObjectTypeDefinitionNode returnType = GenerateReturnType(name);
                    returnTypes.Add(returnType);

                    queryFields.Add(GenerateGetAllQuery(name, returnType));

                    queryFields.Add(GenerateByPKQuery(objectTypeDefinitionNode, name));
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(null, new NameNode("Query"), null, new List<DirectiveNode>(), new List<NamedTypeNode>(), queryFields),
            };
            definitionNodes.AddRange(returnTypes);
            return new(definitionNodes);
        }

        private static FieldDefinitionNode GenerateByPKQuery(ObjectTypeDefinitionNode objectTypeDefinitionNode, NameNode name)
        {
            return new(
                null,
                new NameNode($"{name}_by_pk"),
                new StringValueNode($"Get a {name} from the database by its ID/primary key"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    null,
                    new NameNode("id"),
                    null,
                    objectTypeDefinitionNode.Fields.First(f => f.Name.Value == "id").Type,
                    null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(name),
                new List<DirectiveNode>()
            );
        }

        private static FieldDefinitionNode GenerateGetAllQuery(NameNode name, ObjectTypeDefinitionNode returnType)
        {
            return new(
                null,
                Pluralize(name),
                new StringValueNode($"Get a list of all the {name} items from the database"),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(null, new NameNode("first"), null, new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("continuation"), null, new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                },
                new NonNullTypeNode(new NamedTypeNode(returnType.Name)),
                new List<DirectiveNode>()
            );
        }

        private static ObjectTypeDefinitionNode GenerateReturnType(NameNode name)
        {
            return new(
                null,
                new NameNode($"{name}Connection"),
                null,
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                new List<FieldDefinitionNode> {
                    new FieldDefinitionNode(
                        null,
                        new NameNode("items"),
                        null,
                        new List<InputValueDefinitionNode>(),
                        new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                        new List<DirectiveNode>()),
                    new FieldDefinitionNode(
                        null,
                        new NameNode("continuation"),
                        null,
                        new List<InputValueDefinitionNode>(),
                        new StringType().ToTypeNode(),
                        new List<DirectiveNode>())
                }
            );
        }
    }
}
