// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlNestedMutationBuilderTests : NestedMutationBuilderTests
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            databaseEngine = TestCategory.MSSQL;
            await InitializeAsync();
        }
    }
}
