// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    /// <summary>
    /// Directive indicating that a field is a computed measure from a semantic model,
    /// not a physical database column. Enables codegen tools to distinguish measures
    /// from columns via GraphQL introspection.
    /// </summary>
    public class MeasureDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "measure";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                .Name(DirectiveName)
                .Description("Indicates that a field is a computed measure from a semantic model.")
                .Location(DirectiveLocation.FieldDefinition);
        }
    }
}
