using HotChocolate.Language;
using HotChocolate.Types;
using DirectiveLocation = HotChocolate.Language.DirectiveLocation;

namespace Azure.DataGateway.Service.GraphQLBuilder.Directives
{
    public static class RelationshipDirective
    {
        public static string DirectiveName { get; } = "relationship";

        public static DirectiveDefinitionNode Directive
        {
            get
            {
                return new(
                location: null,
                new NameNode(DirectiveName),
                new StringValueNode("A directive to indicate the relationship between two tables"),
                false,
                new List<InputValueDefinitionNode> {
                    new(location: null,
                    new NameNode("target"),
                    new StringValueNode("The target entity of the relationship"),
                    new StringType().ToTypeNode(),
                    defaultValue: null,
                    new List<DirectiveNode>()),

                    new(location: null,
                    new NameNode("cardinality"),
                    new StringValueNode("The relationship cardinality"),
                    new StringType().ToTypeNode(),
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                new List<NameNode> { new NameNode(DirectiveLocation.FieldDefinition.Value) });
            }
        }
    }
}
