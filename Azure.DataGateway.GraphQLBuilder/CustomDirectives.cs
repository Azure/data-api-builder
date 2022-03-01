using HotChocolate;
using HotChocolate.Types;

namespace Azure.DataGateway.GraphQLBuilder
{
    public static class CustomDirectives
    {
        public static DirectiveType ModelTypeDirective() =>
            new (config =>
            {
                config
                .Name(new NameString("model"))
                .Location(DirectiveLocation.Object);
            });
    }
}
