// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Currently, we don't support multiple-create for MySql but the order determination logic for insertions is valid for MySql as well.
    /// </summary>
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
