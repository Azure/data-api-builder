// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlMultipleCreateOrderHelperUnitTests : MultipleCreateOrderHelperUnitTests
    {
        [ClassInitialize]
        public static async Task Initialize(TestContext testContext)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }
    }
}
