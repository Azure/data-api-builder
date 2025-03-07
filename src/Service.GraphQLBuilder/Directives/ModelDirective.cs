// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Directives
{
    [DirectiveType(
        DirectiveLocation.Object
        | DirectiveLocation.FieldDefinition,
        Name = Names.MODEL)]
    [GraphQLDescription(
        "A directive to indicate the type maps to a " +
        "storable entity not a nested entity.")]
    public class ModelDirective
    {
        [GraphQLDescription(
            "Underlying name of the database entity.")]
        public string? Name { get; set; }

        public static class Names
        {
            public const string MODEL = "model";
            public const string NAME_ARGUMENT = "name";
        }
    }
}
