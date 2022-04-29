using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder.Directives
{
    public class RelationshipDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "relationship";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name(DirectiveName)
                               .Description("A directive to indicate the relationship between two tables")
                               .Location(DirectiveLocation.FieldDefinition);

            descriptor.Argument("target")
                  .Type<StringType>()
                  .Description("The name of the entity the relationship targets");

            descriptor.Argument("cardinality")
                  .Type<StringType>()
                  .Description("The relationship cardinality");
        }
    }
}
