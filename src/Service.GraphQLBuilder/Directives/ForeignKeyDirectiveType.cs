// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class ForeignKeyDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "foreignKey";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                .Name(DirectiveName)
                .Description("Indicates that a field holds a foreign key reference to another table.")
                .Location(DirectiveLocation.FieldDefinition);
        }
    }
}
