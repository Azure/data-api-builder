// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes
{
    public class OrderByType : EnumType<OrderBy>
    {
        public static string EnumName { get; } = nameof(OrderBy);

        protected override void Configure(IEnumTypeDescriptor<OrderBy> descriptor)
        {
            base.Configure(descriptor);
            descriptor.Name(EnumName);
        }
    }

    public enum OrderBy
    {
        ASC, DESC
    }
}
