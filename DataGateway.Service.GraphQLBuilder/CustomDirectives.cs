using HotChocolate;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    public static class CustomDirectives
    {
        public static string ModelTypeDirectiveName = "model";
        public static DirectiveType ModelTypeDirective() =>
            new(config =>
           {
               config
               .Name(new NameString(ModelTypeDirectiveName))
               .Location(DirectiveLocation.Object);
           });
    }
}
