// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgresQueryBuilderHelperTests
    {
        [TestMethod]
        public void BuildExecute_IsNotImplemented()
        {
            PostgresQueryBuilder builder = new();

            Assert.ThrowsException<NotImplementedException>(() => builder.Build((SqlExecuteStructure)null!));
        }

        [TestMethod]
        public void BuildStoredProcedureResultDetailsQuery_IsNotImplemented()
        {
            PostgresQueryBuilder builder = new();

            Assert.ThrowsException<NotImplementedException>(() =>
                builder.BuildStoredProcedureResultDetailsQuery("get_books"));
        }

        [TestMethod]
        public void IsInsert_MissingOperationMetadataThrows()
        {
            Dictionary<string, object?> result = new();

            Assert.ThrowsException<ArgumentException>(() => PostgresQueryBuilder.IsInsert(result));
        }

        [TestMethod]
        public void IsInsert_InvalidOperationMetadataThrowsAndRemovesMetadata()
        {
            Dictionary<string, object?> result = new()
            {
                ["___upsert_op___"] = "invalid"
            };

            Assert.ThrowsException<ArgumentException>(() => PostgresQueryBuilder.IsInsert(result));
            Assert.IsFalse(result.ContainsKey("___upsert_op___"));
        }
    }
}
