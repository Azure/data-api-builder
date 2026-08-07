// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

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

        [TestMethod]
        public void GraphQLDeletePayloadContainsOnlyPrimaryKeyValues()
        {
            Dictionary<string, object?> parameters = new()
            {
                ["Id"] = 1,
                ["filter"] = new List<IValueNode> { new StringValueNode("ignored") }
            };
            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new List<string> { "id" }
            };
            Mock<ISqlMetadataProvider> metadataProvider = new();
            metadataProvider.Setup(provider => provider.GetSourceDefinition("Actor")).Returns(sourceDefinition);
            string? exposedColumnName = "Id";
            metadataProvider
                .Setup(provider => provider.TryGetExposedColumnName("Actor", "id", out exposedColumnName))
                .Returns(true);

            JsonElement record = SqlMutationEngine.GetGraphQLDeleteSubscriptionRecord("Actor", parameters, metadataProvider.Object);

            Assert.AreEqual(1, record.GetProperty("Id").GetInt32());
            Assert.IsFalse(record.TryGetProperty("filter", out _));
        }
    }
}

#nullable restore
