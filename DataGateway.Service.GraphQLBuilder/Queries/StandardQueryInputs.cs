using System.Collections.Generic;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder.Queries
{
    internal static class StandardQueryInputs
    {
        public static InputObjectTypeDefinitionNode IdInputType() =>
            new(
                null,
                new NameNode("IdFilterInput"),
                new StringValueNode("Input type for adding ID filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(null, new NameNode("eq"), new StringValueNode("Equals"), new IdType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("neq"), new StringValueNode("Not Equals"), new IdType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode BooleanInputType() =>
            new(
                null,
                new NameNode("BooleanFilterInput"),
                new StringValueNode("Input type for adding Boolean filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(null, new NameNode("eq"), new StringValueNode("Equals"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("neq"), new StringValueNode("Not Equals"), new BooleanType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode IntInputType() =>
            new(
                null,
                new NameNode("IntFilterInput"),
                new StringValueNode("Input type for adding Int filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(null, new NameNode("eq"), new StringValueNode("Equals"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("gt"), new StringValueNode("Greater Than"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("lt"), new StringValueNode("Less Than"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new IntType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("neq"), new StringValueNode("Not Equals"), new IntType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode FloatInputType() =>
            new(
                null,
                new NameNode("FloatFilterInput"),
                new StringValueNode("Input type for adding Float filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(null, new NameNode("eq"), new StringValueNode("Equals"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("gt"), new StringValueNode("Greater Than"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("gte"), new StringValueNode("Greater Than or Equal To"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("lt"), new StringValueNode("Less Than"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("lte"), new StringValueNode("Less Than or Equal To"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("neq"), new StringValueNode("Not Equals"), new FloatType().ToTypeNode(), null, new List<DirectiveNode>())
                }
            );

        public static InputObjectTypeDefinitionNode StringInputType() =>
            new(
                null,
                new NameNode("StringFilterInput"),
                new StringValueNode("Input type for adding String filters"),
                new List<DirectiveNode>(),
                new List<InputValueDefinitionNode> {
                    new InputValueDefinitionNode(null, new NameNode("eq"), new StringValueNode("Equals"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("contains"), new StringValueNode("Contains"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("notContains"), new StringValueNode("Not Contains"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("startsWith"), new StringValueNode("Starts With"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("endsWith"), new StringValueNode("Ends With"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("neq"), new StringValueNode("Not Equals"), new StringType().ToTypeNode(), null, new List<DirectiveNode>()),
                    new InputValueDefinitionNode(null, new NameNode("caseInsensitive"), new StringValueNode("Case Insensitive"), new BooleanType().ToTypeNode(), new BooleanValueNode(false), new List<DirectiveNode>())
                }
            );

        public static Dictionary<string, InputObjectTypeDefinitionNode> InputTypes = new()
        {
            { "ID", IdInputType() },
            { "Int", IntInputType() },
            { "Float", FloatInputType() },
            { "Boolean", BooleanInputType() },
            { "String", StringInputType() }
        };
    }
}
