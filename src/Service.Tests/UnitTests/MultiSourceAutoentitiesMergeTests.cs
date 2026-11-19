// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Regression tests for propagating generated autoentities from multiple child
    /// configurations into the shared root runtime configuration (child of #3711).
    /// </summary>
    [TestClass]
    public class MultiSourceAutoentitiesMergeTests
    {
        private readonly List<string> _tempFiles = new();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (string file in _tempFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }

        [DataTestMethod]
        [DataRow(1, DisplayName = "Parent with one child")]
        [DataRow(2, DisplayName = "Parent with two children")]
        [DataRow(3, DisplayName = "Parent with three children")]
        public async Task GeneratedAutoentitiesFromAllChildrenAreMergedIntoRootConfig(int childCount)
        {
            List<string> childFiles = new();
            for (int i = 0; i < childCount; i++)
            {
                childFiles.Add(WriteChildConfig(i));
            }

            RuntimeConfigProvider provider = CreateProvider(childFiles);
            RuntimeConfig initialConfig = provider.GetConfig();
            Assert.AreEqual(childCount, initialConfig.ChildConfigs.Count);
            Assert.AreEqual(0, initialConfig.Entities.Entities.Count);

            // Each child's data source returns objects unique to that child.
            Dictionary<string, int> dataSourceToChildIndex = initialConfig.ChildConfigs
                .Select((child, index) => (child.Config.DefaultDataSourceName, index))
                .ToDictionary(x => x.DefaultDataSourceName, x => x.index);

            MetadataProviderFactory factory = CreateFactory(provider, dataSourceToChildIndex);

            foreach (ISqlMetadataProvider metadataProvider in factory.ListMetadataProviders())
            {
                await GenerateAutoentitiesAsync((MsSqlMetadataProvider)metadataProvider);
            }

            RuntimeConfig finalConfig = provider.GetConfig();
            Assert.AreEqual(childCount * 2, finalConfig.Entities.Entities.Count,
                $"Entities in merged config: {string.Join(", ", finalConfig.Entities.Entities.Keys)}");

            foreach ((string dataSourceName, int childIndex) in dataSourceToChildIndex)
            {
                foreach (string table in new[] { "orders", "customers" })
                {
                    string entityName = $"c{childIndex}_{table}";
                    Assert.IsTrue(finalConfig.Entities.ContainsKey(entityName), $"Missing generated entity '{entityName}'.");
                    Assert.IsTrue(finalConfig.Entities[entityName].IsAutoentity);
                    Assert.AreEqual(dataSourceName, finalConfig.GetDataSourceNameFromEntityName(entityName),
                        $"Entity '{entityName}' is not mapped to its originating data source.");
                }
            }
        }

        private string WriteChildConfig(int index)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dab-child-{index}-config.json");
            string json = $@"{{
                ""data-source"": {{
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""Server=localhost;Database=child{index};Trusted_Connection=True;""
                }},
                ""entities"": {{}},
                ""autoentities"": {{
                    ""child{index}-auto"": {{
                    ""patterns"": {{ ""include"": [ ""dbo.%"" ], ""name"": ""c{index}_{{object}}"" }},
                    ""permissions"": [ {{ ""role"": ""anonymous"", ""actions"": [ {{ ""action"": ""*"" }} ] }} ]
                    }}
                }}
            }}";
            File.WriteAllText(path, json);
            _tempFiles.Add(path);
            return path;
        }

        private static RuntimeConfigProvider CreateProvider(IEnumerable<string> childFiles)
        {
            string files = string.Join(", ", childFiles.Select(f => $"\"{f.Replace("\\", "\\\\")}\""));
            string parentJson = $@"{{
                ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
                ""data-source-files"": [ {files} ],
                ""runtime"": {{
                    ""rest"": {{ ""enabled"": true }},
                    ""graphql"": {{ ""enabled"": true }},
                    ""mcp"": {{ ""enabled"": true }},
                    ""host"": {{ ""mode"": ""development"" }}
                }},
                ""entities"": {{}}
            }}";
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, parentJson);
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            return new RuntimeConfigProvider(loader);
        }

        private static MetadataProviderFactory CreateFactory(
            RuntimeConfigProvider provider,
            Dictionary<string, int> dataSourceToChildIndex)
        {
            Mock<IQueryBuilder> queryBuilder = new();
            queryBuilder.Setup(x => x.BuildGetAutoentitiesQuery()).Returns("SELECT autoentities");

            Mock<IQueryExecutor> queryExecutor = new();
            queryExecutor.Setup(x => x.ExecuteQueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<IDictionary<string, DbConnectionParam>>(),
                    It.IsAny<Func<System.Data.Common.DbDataReader, List<string>?, Task<JsonArray>>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(),
                    It.IsAny<List<string>>()))
                .ReturnsAsync((string _, IDictionary<string, DbConnectionParam> _, Func<System.Data.Common.DbDataReader, List<string>?, Task<JsonArray>> _, string dataSourceName, CancellationToken _, Microsoft.AspNetCore.Http.HttpContext _, List<string> _) =>
                {
                    int index = dataSourceToChildIndex[dataSourceName];
                    return new JsonArray(
                        new JsonObject { ["entity_name"] = $"c{index}_orders", ["object"] = "orders", ["schema"] = "dbo" },
                        new JsonObject { ["entity_name"] = $"c{index}_customers", ["object"] = "customers", ["schema"] = "dbo" });
                });

            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(queryBuilder.Object);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(queryExecutor.Object);

            MockFileSystem fileSystem = new();
            RuntimeConfigValidator validator = new(provider, fileSystem, NullLogger<RuntimeConfigValidator>.Instance);
            return new MetadataProviderFactory(
                provider,
                validator,
                queryManagerFactory.Object,
                NullLogger<ISqlMetadataProvider>.Instance,
                fileSystem,
                handler: null);
        }

        // Mirrors the autoentity step of SqlMetadataProvider.InitializeAsync without requiring database metadata.
        private static Task GenerateAutoentitiesAsync(MsSqlMetadataProvider metadataProvider)
        {
            Type baseType = typeof(MsSqlMetadataProvider).BaseType!;
            object autoentities = baseType
                .GetProperty("Autoentities", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(metadataProvider)!;
            MethodInfo generate = typeof(MsSqlMetadataProvider).GetMethod(
                "GenerateAutoentitiesIntoEntities",
                BindingFlags.Instance | BindingFlags.NonPublic,
                new[] { typeof(IReadOnlyDictionary<string, Autoentity>), typeof(CancellationToken) })!;
            return (Task)generate.Invoke(metadataProvider, new[] { autoentities, CancellationToken.None })!;
        }
    }
}
