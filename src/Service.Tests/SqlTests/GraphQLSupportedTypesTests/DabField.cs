// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{
    public class DabField
    {
        public string Alias { get; set; }
        public string BackingColumnName { get; set; }
        public DabField(string alias, string backingColumnName)
        {
            Alias = alias;
            BackingColumnName = backingColumnName;
        }

        public DabField(string backingColumnName)
        {
            Alias = backingColumnName;
            BackingColumnName = backingColumnName;
        }
    }
}
