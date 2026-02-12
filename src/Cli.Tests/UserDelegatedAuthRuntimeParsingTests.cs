// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    [TestClass]
    public class UserDelegatedAuthRuntimeParsingTests
    {
        [TestMethod]
        public void TestRuntimeCanParseUserDelegatedAuthConfig()
        {
            // Arrange
            string configJson = @"{
                ""$schema"": ""test"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""testconnectionstring"",
                    ""user-delegated-auth"": {
                        ""enabled"": true,
                        ""database-audience"": ""https://database.windows.net""
                    }
                },
                ""runtime"": {
                    ""rest"": {
                        ""enabled"": true,
                        ""path"": ""/api""
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""path"": ""/graphql"",
                        ""allow-introspection"": true
                    },
                    ""host"": {
                        ""mode"": ""development"",
                        ""cors"": {
                            ""origins"": [],
                            ""allow-credentials"": false
                        },
                        ""authentication"": {
                            ""provider"": ""StaticWebApps""
                        }
                    }
                },
                ""entities"": {}
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig? config);

            // Assert
            Assert.IsTrue(success);
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.DataSource.UserDelegatedAuth);
            Assert.IsTrue(config.DataSource.UserDelegatedAuth.Enabled);
            Assert.AreEqual("https://database.windows.net", config.DataSource.UserDelegatedAuth.DatabaseAudience);
            Assert.AreEqual(50, config.DataSource.UserDelegatedAuth.EffectiveTokenCacheDurationMinutes);
            Assert.IsTrue(config.DataSource.UserDelegatedAuth.EffectiveDisableConnectionPooling);
        }

        [TestMethod]
        public void TestRuntimeCanParseConfigWithoutUserDelegatedAuth()
        {
            // Arrange
            string configJson = @"{
                ""$schema"": ""test"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""testconnectionstring""
                },
                ""runtime"": {
                    ""rest"": {
                        ""enabled"": true,
                        ""path"": ""/api""
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""path"": ""/graphql"",
                        ""allow-introspection"": true
                    },
                    ""host"": {
                        ""mode"": ""development"",
                        ""cors"": {
                            ""origins"": [],
                            ""allow-credentials"": false
                        },
                        ""authentication"": {
                            ""provider"": ""StaticWebApps""
                        }
                    }
                },
                ""entities"": {}
            }";

            // Act
            bool success = RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig? config);

            // Assert
            Assert.IsTrue(success);
            Assert.IsNotNull(config);
            Assert.IsNull(config.DataSource.UserDelegatedAuth);
        }
    }
}
