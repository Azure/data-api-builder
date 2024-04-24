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
                .Description("When present on a field in a database table, indicates that the field is a referencing field " +
                "to some field in the same/another database table.")
                .Location(DirectiveLocation.FieldDefinition);
        }
    }
}
