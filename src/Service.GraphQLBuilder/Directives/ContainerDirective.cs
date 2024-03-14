// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Language;
using HotChocolate.Types;
using DirectiveLocation = HotChocolate.Types.DirectiveLocation;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class ContainerDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "container";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor.Name(DirectiveName)
                               .Description("A directive to indicate the container level entity")
                               .Location(DirectiveLocation.Object);
        }

        /// <summary>
        /// Retrieves the relationship directive defined on the given field definition node.
        /// </summary>
        public static DirectiveNode? GetDirective(FieldDefinitionNode field)
        {
            DirectiveNode? directive = field.Directives.FirstOrDefault(d => d.Name.Value == DirectiveName);
            return directive;
        }
    }
}
