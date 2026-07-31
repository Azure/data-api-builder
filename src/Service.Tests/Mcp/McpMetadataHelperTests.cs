// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    [TestClass]
    public class McpMetadataHelperTests
    {
        [DataTestMethod]
        [DataRow(null, DisplayName = "Null entity name")]
        [DataRow("", DisplayName = "Empty entity name")]
        [DataRow("   ", DisplayName = "Whitespace entity name")]
        public void TryResolveMetadata_ExplicitFactory_InvalidEntityNameReturnsFalse(string? entityName)
        {
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: null,
                Runtime: null,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();

            bool resolved = McpMetadataHelper.TryResolveMetadata(
                entityName!,
                config,
                metadataProviderFactory.Object,
                out ISqlMetadataProvider _,
                out DatabaseObject _,
                out string dataSourceName,
                out string error);

            Assert.IsFalse(resolved);
            Assert.AreEqual(string.Empty, dataSourceName);
            Assert.AreEqual("Entity name cannot be null or empty.", error);
            metadataProviderFactory.Verify(
                factory => factory.GetMetadataProvider(It.IsAny<string>()),
                Times.Never);
        }
    }
}
