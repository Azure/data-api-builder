using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using HotChocolate.Language;
using HotChocolate.Types;
using DirectiveLocation = HotChocolate.Types.DirectiveLocation;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class DefaultValueDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "defaultValue";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                .Name(DirectiveName)
                .Description("The default value to be used when creating an item.")
                .Location(DirectiveLocation.FieldDefinition);

            descriptor
                .Argument("value")
                .Type<DefaultValueType>();
        }

        public static bool TryGetDefaultValue(FieldDefinitionNode field, [NotNullWhen(true)] out ObjectValueNode? value)
        {
            DirectiveNode? directive = field.Directives.FirstOrDefault(d => d.Name.Value == DirectiveName);

            if (directive is null)
            {
                value = null;
                return false;
            }

            value = (ObjectValueNode)directive.Arguments[0].Value!;
            return true;
        }
    }
}
