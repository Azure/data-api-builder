using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class ModelDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "model";
        public static string ModelNameArgument { get; } = "name";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name(DirectiveName)
                .Description("A directive to indicate the type maps to a storable entity not a nested entity.");

            descriptor.Location(DirectiveLocation.Object);

            descriptor.Argument(ModelNameArgument)
                .Description("Underlying name of the database entity.")
                .Type<StringType>();
        }
    }
}
