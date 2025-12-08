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
        /// <summary>
        /// Test that deserializing boolean 'true' shorthand correctly sets dml-tools enabled.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpBooleanTrue_EnablesDmlToolsOnly()
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
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            
            Entity bookEntity = runtimeConfig.Entities["Book"];
            Assert.IsNotNull(bookEntity.Mcp, "MCP options should be present");
            Assert.IsTrue(bookEntity.Mcp.DmlToolEnabled, "DmlTools should be enabled");
            Assert.IsFalse(bookEntity.Mcp.CustomToolEnabled, "CustomTool should be disabled (default)");
        }

        /// <summary>
        /// Test that deserializing boolean 'false' shorthand correctly sets dml-tools disabled.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpBooleanFalse_DisablesDmlTools()
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
                        ""mcp"": false
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            
            Entity bookEntity = runtimeConfig.Entities["Book"];
            Assert.IsNotNull(bookEntity.Mcp, "MCP options should be present");
            Assert.IsFalse(bookEntity.Mcp.DmlToolEnabled, "DmlTools should be disabled");
            Assert.IsFalse(bookEntity.Mcp.CustomToolEnabled, "CustomTool should be disabled (default)");
        }

        /// <summary>
        /// Test that deserializing object format with both properties works correctly.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObject_SetsBothProperties()
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
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            
            Entity spEntity = runtimeConfig.Entities["GetBook"];
            Assert.IsNotNull(spEntity.Mcp, "MCP options should be present");
            Assert.IsTrue(spEntity.Mcp.CustomToolEnabled, "CustomTool should be enabled");
            Assert.IsFalse(spEntity.Mcp.DmlToolEnabled, "DmlTools should be disabled");
        }

        /// <summary>
        /// Test that deserializing object format with only dml-tools works.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithDmlToolsOnly_WorksCorrectly()
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
                        ""mcp"": {
                            ""dml-tools"": true
                        }
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            
            Entity bookEntity = runtimeConfig.Entities["Book"];
            Assert.IsNotNull(bookEntity.Mcp, "MCP options should be present");
            Assert.IsTrue(bookEntity.Mcp.DmlToolEnabled, "DmlTools should be enabled");
            Assert.IsFalse(bookEntity.Mcp.CustomToolEnabled, "CustomTool should be disabled (default)");
        }

        /// <summary>
        /// Test that entity without MCP configuration has null MCP options.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_NoMcp_HasNullMcpOptions()
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
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""*""] }]
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Book"));
            
            Entity bookEntity = runtimeConfig.Entities["Book"];
            Assert.IsNull(bookEntity.Mcp, "MCP options should be null when not specified");
        }

        /// <summary>
        /// Test that deserializing object format with both properties set to true works correctly.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithBothTrue_SetsCorrectly()
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
                    ""GetBook"": {
                        ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                        ""mcp"": {
                            ""custom-tool"": true,
                            ""dml-tools"": true
                        }
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            
            Entity spEntity = runtimeConfig.Entities["GetBook"];
            Assert.IsNotNull(spEntity.Mcp, "MCP options should be present");
            Assert.IsTrue(spEntity.Mcp.CustomToolEnabled, "CustomTool should be enabled");
            Assert.IsTrue(spEntity.Mcp.DmlToolEnabled, "DmlTools should be enabled");
        }

        /// <summary>
        /// Test that deserializing object format with both properties set to false works correctly.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithBothFalse_SetsCorrectly()
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
                    ""GetBook"": {
                        ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                        ""mcp"": {
                            ""custom-tool"": false,
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
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            
            Entity spEntity = runtimeConfig.Entities["GetBook"];
            Assert.IsNotNull(spEntity.Mcp, "MCP options should be present");
            Assert.IsFalse(spEntity.Mcp.CustomToolEnabled, "CustomTool should be disabled");
            Assert.IsFalse(spEntity.Mcp.DmlToolEnabled, "DmlTools should be disabled");
        }

        /// <summary>
        /// Test that deserializing object format with only custom-tool works.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_McpObjectWithCustomToolOnly_WorksCorrectly()
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
                    ""GetBook"": {
                        ""source"": { ""type"": ""stored-procedure"", ""object"": ""dbo.GetBook"" },
                        ""permissions"": [{ ""role"": ""anonymous"", ""actions"": [""execute""] }],
                        ""mcp"": {
                            ""custom-tool"": true
                        }
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsTrue(success, "Config should parse successfully");
            Assert.IsNotNull(runtimeConfig);
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            
            Entity spEntity = runtimeConfig.Entities["GetBook"];
            Assert.IsNotNull(spEntity.Mcp, "MCP options should be present");
            Assert.IsTrue(spEntity.Mcp.CustomToolEnabled, "CustomTool should be enabled");
            Assert.IsFalse(spEntity.Mcp.DmlToolEnabled, "DmlTools should be disabled (default is false)");
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
            Entity bookEntity = runtimeConfig.Entities["Book"];
            Assert.IsNotNull(bookEntity.Mcp);
            Assert.IsTrue(bookEntity.Mcp.DmlToolEnabled);
            Assert.IsFalse(bookEntity.Mcp.CustomToolEnabled);
            
            // Author: mcp = false
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Author"));
            Entity authorEntity = runtimeConfig.Entities["Author"];
            Assert.IsNotNull(authorEntity.Mcp);
            Assert.IsFalse(authorEntity.Mcp.DmlToolEnabled);
            Assert.IsFalse(authorEntity.Mcp.CustomToolEnabled);
            
            // Publisher: no mcp
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("Publisher"));
            Entity publisherEntity = runtimeConfig.Entities["Publisher"];
            Assert.IsNull(publisherEntity.Mcp);
            
            // GetBook: mcp object
            Assert.IsTrue(runtimeConfig.Entities.ContainsKey("GetBook"));
            Entity spEntity = runtimeConfig.Entities["GetBook"];
            Assert.IsNotNull(spEntity.Mcp);
            Assert.IsTrue(spEntity.Mcp.CustomToolEnabled);
            Assert.IsFalse(spEntity.Mcp.DmlToolEnabled);
        }

        /// <summary>
        /// Test that deserializing invalid MCP value (non-boolean, non-object) fails gracefully.
        /// </summary>
        [TestMethod]
        public void DeserializeConfig_InvalidMcpValue_FailsGracefully()
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
                        ""mcp"": ""invalid""
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

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
                        ""mcp"": {
                            ""dml-tools"": true,
                            ""unknown-property"": true
                        }
                    }
                }
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(config, out RuntimeConfig runtimeConfig);

            // Assert
            Assert.IsFalse(success, "Config parsing should fail with unknown MCP property");
        }
    }
}
