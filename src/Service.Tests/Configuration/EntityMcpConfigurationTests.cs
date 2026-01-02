// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    /// <summary>
    /// Tests for entity-level MCP configuration deserialization and validation.
    /// Validates that EntityMcpOptions are correctly deserialized from runtime config JSON.
    /// </summary>
    [TestClass]
    public class EntityMcpConfigurationTests
    {
        private const string BASE_CONFIG_TEMPLATE = @"{{
            ""$schema"": ""test-schema"",
            ""data-source"": {{
                ""database-type"": ""mssql"",
                ""connection-string"": ""test""
            }},
            ""runtime"": {{
                ""rest"": {{ ""enabled"": true, ""path"": ""/api"" }},
                ""graphql"": {{ ""enabled"": true, ""path"": ""/graphql"" }},
                ""host"": {{ ""mode"": ""development"" }}
            }},
            ""entities"": {{
                {0}
            }}
        }}";

        /// <summary>
        /// Helper method to create a config with specified entities JSON
        /// </summary>
        private static string CreateConfig(string entitiesJson)
        {
            return string.Format(BASE_CONFIG_TEMPLATE, entitiesJson);
        }

        /// <summary>
        /// Helper method to assert entity MCP configuration
        /// </summary>
        private static void AssertEntityMcp(Entity entity, bool? expectedDmlTools, bool? expectedCustomTool, string message = null)
        {
            if (expectedDmlTools == null && expectedCustomTool == null)
            {
                Assert.IsNull(entity.Mcp, "MCP options should be null when not specified");
                return;
            }

            Assert.IsNotNull(entity.Mcp, message ?? "MCP options should be present");

            bool actualDmlTools = entity.Mcp?.DmlToolEnabled ?? true; // Default is true
            bool actualCustomTool = entity.Mcp?.CustomToolEnabled ?? false; // Default is false

            Assert.AreEqual(expectedDmlTools ?? true, actualDmlTools,
                $"DmlToolEnabled should be {expectedDmlTools ?? true}");
            Assert.AreEqual(expectedCustomTool ?? false, actualCustomTool,
                $"CustomToolEnabled should be {expectedCustomTool ?? false}");
        }
        /// <summary>
        /// Test that deserializing boolean 'true' shorthand correctly sets dml-tools enabled.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpBooleanTrue_EnablesDmlToolsOnly()
        {
            // Arrange
            string config = CreateConfig(@"
                ""Book"": {
                    ""source"": ""books"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                    ""mcp"": true
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            AssertEntityMcp(runtimeConfig.Entities["Book"], expectedDmlTools: true, expectedCustomTool: false);
        }

        /// <summary>
        /// Test that deserializing boolean 'false' shorthand correctly sets dml-tools disabled.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpBooleanFalse_DisablesDmlTools()
        {
            // Arrange
            string config = CreateConfig(@"
                ""Book"": {
                    ""source"": ""books"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                    ""mcp"": false
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            AssertEntityMcp(runtimeConfig.Entities["Book"], expectedDmlTools: false, expectedCustomTool: false);
        }

        /// <summary>
        /// Test that deserializing object format with both properties works correctly.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObject_SetsBothProperties()
        {
            // Arrange
            string config = CreateConfig(@"
                ""GetBook"": {
                    ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                    ""mcp"": {
                        ""custom-tool"": true,
                        ""dml-tools"": false
                    }
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            AssertEntityMcp(runtimeConfig.Entities["GetBook"], expectedDmlTools: false, expectedCustomTool: true);
        }

        /// <summary>
        /// Test that deserializing object format with only dml-tools works.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithDmlToolsOnly_WorksCorrectly()
        {
            // Arrange
            string config = CreateConfig(@"
                ""Book"": {
                    ""source"": ""books"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                    ""mcp"": {
                        ""dml-tools"": true
                    }
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            AssertEntityMcp(runtimeConfig.Entities["Book"], expectedDmlTools: true, expectedCustomTool: false);
        }

        /// <summary>
        /// Test that entity without MCP configuration has null Mcp property.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_NoMcp_HasNullMcpOptions()
        {
            // Arrange
            string config = CreateConfig(@"
                ""Book"": {
                    ""source"": ""books"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }]
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            Assert.IsNull(runtimeConfig.Entities["Book"].Mcp, "MCP options should be null when not specified");
        }

        /// <summary>
        /// Test that deserializing object format with both properties set to true works correctly.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithBothTrue_SetsCorrectly()
        {
            // Arrange
            string config = CreateConfig(@"
                ""GetBook"": {
                    ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                    ""mcp"": {
                        ""custom-tool"": true,
                        ""dml-tools"": true
                    }
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            AssertEntityMcp(runtimeConfig.Entities["GetBook"], expectedDmlTools: true, expectedCustomTool: true);
        }

        /// <summary>
        /// Test that deserializing object format with both properties set to false works correctly.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithBothFalse_SetsCorrectly()
        {
            // Arrange
            string config = CreateConfig(@"
                ""GetBook"": {
                    ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                    ""mcp"": {
                        ""custom-tool"": false,
                        ""dml-tools"": false
                    }
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            AssertEntityMcp(runtimeConfig.Entities["GetBook"], expectedDmlTools: false, expectedCustomTool: false);
        }

        /// <summary>
        /// Test that deserializing object format with only custom-tool works.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithCustomToolOnly_WorksCorrectly()
        {
            // Arrange
            string config = CreateConfig(@"
                ""GetBook"": {
                    ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                    ""mcp"": {
                        ""custom-tool"": true
                    }
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            AssertEntityMcp(runtimeConfig.Entities["GetBook"], expectedDmlTools: true, expectedCustomTool: true);
        }

        /// <summary>
        /// Test that deserializing config with multiple entities having different MCP settings works.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_MultipleEntitiesWithDifferentMcpSettings_WorksCorrectly()
        {
            // Arrange
            string config = @"{
                ""$schema"": ""test-schema"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""test""
                },
                ""runtime"": {
                    ""rest"": { ""enabled"": true, ""path"": ""/api"" },
                    ""graphql"": { ""enabled"": true, ""path"": ""/graphql"" },
                    ""host"": { ""mode"": ""development"" }
                },
                ""entities"": {
                    ""Book"": {
                        ""source"": ""books"",
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                        ""mcp"": true
                    },
                    ""Author"": {
                        ""source"": ""authors"",
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                        ""mcp"": false
                    },
                    ""Publisher"": {
                        ""source"": ""publishers"",
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }]
                    },
                    ""GetBook"": {
                        ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                        ""mcp"": {
                            ""custom-tool"": true,
                            ""dml-tools"": false
                        }
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);

            // Book: mcp = true
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            AssertEntityMcp(runtimeConfig.Entities["Book"], expectedDmlTools: true, expectedCustomTool: false);

            // Author: mcp = false
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Author"));
            AssertEntityMcp(runtimeConfig.Entities["Author"], expectedDmlTools: false, expectedCustomTool: false);

            // Publisher: no mcp (null)
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Publisher"));
            Assert.IsNull(runtimeConfig.Entities["Publisher"].Mcp, "Mcp should be null when not specified");

            // GetBook: mcp object
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            AssertEntityMcp(runtimeConfig.Entities["GetBook"], expectedDmlTools: false, expectedCustomTool: true);
        }

        /// <summary>
        /// Test that deserializing invalid MCP value (non-boolean, non-object) fails gracefully.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_InvalidMcpValue_FailsGracefully()
        {
            // Arrange
            string config = CreateConfig(@"
                ""Book"": {
                    ""source"": ""books"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                    ""mcp"": ""invalid""
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out _);

            // Assert
            Assert.IsFalse(success, "Config parsing should fail with invalid MCP value");
        }

        /// <summary>
        /// Test that deserializing MCP object with unknown property fails gracefully.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithUnknownProperty_FailsGracefully()
        {
            // Arrange
            string config = CreateConfig(@"
                ""Book"": {
                    ""source"": ""books"",
                    ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }],
                    ""mcp"": {
                        ""dml-tools"": true,
                        ""unknown-property"": true
                    }
                }
            ");

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out _);

            // Assert
            Assert.IsFalse(success, "Config parsing should fail with unknown MCP property");
        }
    }
}
