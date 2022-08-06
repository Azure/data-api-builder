using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class PrimaryKeyDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "primaryKey";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                    .Name(DirectiveName)
                    .Description("A directive to indicate the primary key field of an item.")
                    .Location(DirectiveLocation.FieldDefinition);

            descriptor
                    .Argument("databaseType")
                        .Type<StringType>()
                        .Description("The underlying database type.");
        }
    }
}
