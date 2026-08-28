// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the GraphQL nested-filter depth guard in <see cref="GQLFilterParser"/>.
    /// The guard bounds relationship-nesting depth of filter arguments (e.g. filter:{rel:{rel:{...}}}),
    /// which HotChocolate's execution-depth rule does not cover, to prevent nested-filter depth-bomb DoS.
    /// </summary>
    [TestClass]
    public class GraphQLFilterParserUnitTests
    {
        /// <summary>
        /// A nesting level beyond the maximum is rejected with a BadRequest.
        /// </summary>
        [TestMethod]
        public void EnsureWithinNestedFilterDepth_ThrowsWhenExceeded()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => GQLFilterParser.EnsureWithinNestedFilterDepth(nestingLevel: 21, maxNestedFilterDepth: 20));

            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.DatabaseInputError, ex.SubStatusCode);
        }

        /// <summary>
        /// A nesting level at or below the maximum is allowed.
        /// </summary>
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(20)]
        public void EnsureWithinNestedFilterDepth_DoesNotThrowWithinLimit(int nestingLevel)
        {
            // Should not throw.
            GQLFilterParser.EnsureWithinNestedFilterDepth(nestingLevel, maxNestedFilterDepth: 20);
        }

        /// <summary>
        /// The effective nested-filter depth limit falls back to the hardcoded safety ceiling when
        /// runtime.graphql.depth-limit is not configured, uses the depth-limit only when it is stricter,
        /// and never exceeds the ceiling even when depth-limit is larger or set to -1 (unlimited).
        /// </summary>
        [DataTestMethod]
        [DataRow(null, GQLFilterParser.MAX_NESTED_FILTER_DEPTH, DisplayName = "No depth-limit -> safety ceiling")]
        [DataRow(5, 5, DisplayName = "Stricter depth-limit is used")]
        [DataRow(50, GQLFilterParser.MAX_NESTED_FILTER_DEPTH, DisplayName = "Higher depth-limit is capped at ceiling")]
        [DataRow(-1, GQLFilterParser.MAX_NESTED_FILTER_DEPTH, DisplayName = "Unlimited (-1) depth-limit still capped at ceiling")]
        public void GetMaxNestedFilterDepth_ResolvesEffectiveLimit(int? depthLimit, int expected)
        {
            GQLFilterParser parser = CreateParserWithDepthLimit(depthLimit);
            Assert.AreEqual(expected, parser.GetMaxNestedFilterDepth());
        }

        private static GQLFilterParser CreateParserWithDepthLimit(int? depthLimit)
        {
            RuntimeConfig config = new(
                Schema: "",
                DataSource: new(DatabaseType.MSSQL, "", new()),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(DepthLimit: depthLimit) { UserProvidedDepthLimit = depthLimit is not null },
                    Mcp: new(),
                    Host: new(null, null)),
                Entities: new(new Dictionary<string, Entity>()));

            RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            return new GQLFilterParser(provider, metadataProviderFactory.Object);
        }
    }
}
