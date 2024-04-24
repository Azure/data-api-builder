// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlMultipleCreateOrderHelperUnitTests : MultipleCreateOrderHelperUnitTests
    {
        [ClassInitialize]
        public static async Task Initialize(TestContext testContext)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }
    }
}
