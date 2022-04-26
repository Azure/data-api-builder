using HotChocolate;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder.Directives
{
    public class PrimaryKeyDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "primaryKey";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                    .Name(new NameString(DirectiveName))
                    .Description("A directive to indicate the primary key field of an item.")
                    .Location(DirectiveLocation.FieldDefinition);

            descriptor
                    .Argument(new NameString("databaseType"))
                        .Type(new StringType().ToTypeNode())
                        .Description("The underlying database type");
        }
    }
}
