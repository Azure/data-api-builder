using HotChocolate.Language;
using HotChocolate.Types;
using DirectiveLocation = HotChocolate.Language.DirectiveLocation;

namespace Azure.DataGateway.Service.GraphQLBuilder.Directives
{
    public static class PrimaryKeyDirective
    {
        public static string DirectiveName { get; } = "primaryKey";

        public static DirectiveDefinitionNode Directive
        {
            get
            {
                return new(
                location: null,
                new NameNode(DirectiveName),
                new StringValueNode("A directive to indicate the primary key field of an item"),
                false,
                new List<InputValueDefinitionNode> {
                    new(location: null,
                    new NameNode("databaseType"),
                    new StringValueNode("The underlying database type"),
                    new StringType().ToTypeNode(),
                    defaultValue: null,
                    new List<DirectiveNode>())
                },
                new List<NameNode> { new NameNode(DirectiveLocation.FieldDefinition.Value) });
            }
        }
    }
}
