using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class ModelDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "model";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name(DirectiveName)
                .Description("A directive to indicate the type maps to a storable entity not a nested entity.");

            descriptor.Location(DirectiveLocation.Object);

            descriptor.Argument("name")
                .Description("Underlying name of the database entity.")
                .Type<StringType>();
        }
    }
}
