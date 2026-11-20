// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlQueryBuilderHelperTests
    {
        [TestMethod]
        public void BuildExecute_IsNotImplemented()
        {
            MySqlQueryBuilder builder = new();

            Assert.ThrowsException<NotImplementedException>(() => builder.Build((SqlExecuteStructure)null!));
        }

        [TestMethod]
        public void BuildStoredProcedureResultDetailsQuery_IsNotImplemented()
        {
            MySqlQueryBuilder builder = new();

            Assert.ThrowsException<NotImplementedException>(() =>
                builder.BuildStoredProcedureResultDetailsQuery("get_books"));
        }
    }
}
