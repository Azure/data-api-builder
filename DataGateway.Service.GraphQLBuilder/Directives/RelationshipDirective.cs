using HotChocolate;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder.Directives
{
    public class RelationshipDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "relationship";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name(new NameString(DirectiveName))
                               .Description("A directive to indicate the relationship between two tables")
                                .Location(DirectiveLocation.FieldDefinition);

            descriptor.Argument(new NameString("databaseType"))
                  .Type<StringType>()
                  .Description("The underlying database type");

            descriptor.Argument(new NameString("cardinality"))
                  .Type<StringType>()
                  .Description("The relationship cardinality");
        }
    }
}
