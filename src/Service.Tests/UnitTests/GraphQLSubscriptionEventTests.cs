// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class GraphQLSubscriptionEventTests
    {
        [TestMethod]
        public void CompoundMutationResultPublishesEachRootMutationResult()
        {
            using JsonDocument document = JsonDocument.Parse(@"[{ ""id"": 1 }, { ""id"": 2 }]");

            JsonElement[] records = SqlMutationEngine.GetSubscriptionRecordsFromGraphQLResult(document.RootElement).ToArray();

            Assert.AreEqual(2, records.Length);
            Assert.AreEqual(1, records[0].GetProperty("id").GetInt32());
            Assert.AreEqual(2, records[1].GetProperty("id").GetInt32());
        }

        [TestMethod]
        public void MultipleCreateConnectionPublishesEachCreatedRecord()
        {
            using JsonDocument document = JsonDocument.Parse(@"{ ""items"": [{ ""id"": 1 }, { ""id"": 2 }] }");

            JsonElement[] records = SqlMutationEngine.GetSubscriptionRecordsFromGraphQLResult(document.RootElement).ToArray();

            Assert.AreEqual(2, records.Length);
            Assert.AreEqual(1, records[0].GetProperty("id").GetInt32());
            Assert.AreEqual(2, records[1].GetProperty("id").GetInt32());
        }

        [TestMethod]
        public void SingleMutationResultPublishesSingleRecord()
        {
            using JsonDocument document = JsonDocument.Parse(@"{ ""id"": 1 }");

            JsonElement[] records = SqlMutationEngine.GetSubscriptionRecordsFromGraphQLResult(document.RootElement).ToArray();

            Assert.AreEqual(1, records.Length);
            Assert.AreEqual(1, records[0].GetProperty("id").GetInt32());
        }
    }
}

