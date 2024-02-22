// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class ReferencingFieldDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "dab_referencingField";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                .Name(DirectiveName)
                .Description("Indicates that a field is a referencing field to some referenced field in another table.")
                .Location(DirectiveLocation.FieldDefinition);
        }
    }
}
