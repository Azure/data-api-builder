// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: AutoGeneratedDirectiveType.cs
// **************************************

using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    public class AutoGeneratedDirectiveType : DirectiveType
    {
        public static string DirectiveName { get; } = "autoGenerated";

        protected override void Configure(IDirectiveTypeDescriptor descriptor)
        {
            descriptor
                .Name(DirectiveName)
                .Description("Indicates that a field is auto generated by the database.")
                .Location(DirectiveLocation.FieldDefinition);
        }
    }
}
