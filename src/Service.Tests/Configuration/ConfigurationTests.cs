// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Product;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.HealthCheck;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.OpenApiIntegration;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using HotChocolate;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using VerifyMSTest;
using static Azure.DataApiBuilder.Config.FileSystemRuntimeConfigLoader;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationEndpoints;
using static Azure.DataApiBuilder.Service.Tests.Configuration.TestConfigFileReader;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class ConfigurationTests
    : VerifyBase
    {
        private const string COSMOS_ENVIRONMENT = TestCategory.COSMOSDBNOSQL;
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
        private const string MYSQL_ENVIRONMENT = TestCategory.MYSQL;
        private const string POSTGRESQL_ENVIRONMENT = TestCategory.POSTGRESQL;
        private const string POST_STARTUP_CONFIG_ENTITY = "Book";
        private const string POST_STARTUP_CONFIG_ENTITY_SOURCE = "books";
        private const string POST_STARTUP_CONFIG_ROLE = "PostStartupConfigRole";
        private const string COSMOS_DATABASE_NAME = "config_db";
        private const string CUSTOM_CONFIG_FILENAME = "custom-config.json";
        private const string OPENAPI_SWAGGER_ENDPOINT = "swagger";
        private const string OPENAPI_DOCUMENT_ENDPOINT = "openapi";
        private const string BROWSER_USER_AGENT_HEADER = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36";
        private const string BROWSER_ACCEPT_HEADER = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";

        private const int RETRY_COUNT = 5;
        private const int RETRY_WAIT_SECONDS = 1;

        /// <summary>
        ///
        /// </summary>
        public const string BOOK_ENTITY_JSON = @"
            {
              ""entities"": {
                    ""Book"": {
                    ""source"": {
                        ""object"": ""books"",
                        ""type"": ""table""
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""type"": {
                        ""singular"": ""book"",
                        ""plural"": ""books""
                        }
                    },
                    ""rest"":{
                        ""enabled"": true
                    },
                    ""permissions"": [
                        {
                        ""role"": ""anonymous"",
                        ""actions"": [
                                {
                                    ""action"": ""read""
                                }
                            ]
                        }
                    ],
                    ""mappings"": null,
                    ""relationships"": null
                    }
                }
            }";

        /// <summary>
        /// A valid REST API request body with correct parameter types for all the fields.
        /// </summary>
        public const string REQUEST_BODY_WITH_CORRECT_PARAM_TYPES = @"
                    {
                        ""title"": ""New book"",
                        ""publisher_id"": 1234
                    }
                ";

        /// <summary>
        /// An invalid REST API request body with incorrect parameter type for publisher_id field.
        /// </summary>
        public const string REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES = @"
                    {
                        ""title"": ""New book"",
                        ""publisher_id"": ""one""
                    }
                ";

        /// <summary>
        /// A config file with SP entity with no REST section defined.
        /// This config string is used for validating the REST HTTP methods that are enabled.
        /// </summary>
        public const string SP_CONFIG_WITH_NO_REST_SETTINGS = @"
        {
          ""entities"": {
            ""GetBooks"": {
              ""source"": {
                ""object"": ""get_books"",
                ""type"": ""stored-procedure"",
                ""parameters"": null,
                ""key-fields"": null
              },
              ""graphql"": {
                ""enabled"": true,
                ""operation"": ""query"",
                ""type"": {
                  ""singular"": ""GetBooks"",
                  ""plural"": ""GetBooks""
                }
              },
              ""permissions"": [
                {
                  ""role"": ""anonymous"",
                  ""actions"": [
                    {
                      ""action"": ""execute"",
                      ""fields"": null,
                      ""policy"": {
                        ""request"": null,
                        ""database"": null
                      }
                    }
                  ]
                }
              ],
              ""mappings"": null,
              ""relationships"": null
            }
          }
        }";

        /// <summary>
        /// A config file with SP entity with a custom path defined in REST section.
        /// This config string is used for validating the REST HTTP methods that are enabled.
        /// </summary>
        public const string SP_CONFIG_WITH_ONLY_PATH_IN_REST_SETTINGS = @"
        {
            ""entities"": {
                ""GetBooks"": {
                    ""source"": {
                    ""object"": ""get_books"",
                    ""type"": ""stored-procedure"",
                    ""parameters"": null,
                    ""key-fields"": null
                    },
                    ""graphql"": {
                    ""enabled"": true,
                    ""operation"": ""query"",
                    ""type"": {
                        ""singular"": ""GetBooks"",
                        ""plural"": ""GetBooks""
                    }
                    },
                    ""rest"":{
                    ""path"": ""get_books""
                    },
                    ""permissions"": [
                    {
                        ""role"": ""anonymous"",
                        ""actions"": [
                        {
                            ""action"": ""execute"",
                            ""fields"": null,
                            ""policy"": {
                            ""request"": null,
                            ""database"": null
                            }
                        }
                        ]
                    }
                    ],
                    ""mappings"": null,
                    ""relationships"": null
                }
            }
        }";

        /// <summary>
        /// A config file with a SP entity with the supported HTTP methods defined in REST section.
        /// This config string is used for validating the REST HTTP methods that are enabled.
        /// </summary>
        public const string SP_CONFIG_WITH_JUST_METHODS_IN_REST_SETTINGS = @"
            {
              ""entities"": {
                    ""GetBooks"": {
                    ""source"": {
                        ""object"": ""get_books"",
                        ""type"": ""stored-procedure"",
                        ""parameters"": null,
                        ""key-fields"": null
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""operation"": ""query"",
                        ""type"": {
                        ""singular"": ""GetBooks"",
                        ""plural"": ""GetBooks""
                        }
                    },
                    ""rest"":{
                        ""methods"": [
                        ""get""
                        ]
                    },
                    ""permissions"": [
                        {
                        ""role"": ""anonymous"",
                        ""actions"": [
                            {
                            ""action"": ""execute"",
                            ""fields"": null,
                            ""policy"": {
                                ""request"": null,
                                ""database"": null
                            }
                            }
                        ]
                        }
                    ],
                    ""mappings"": null,
                    ""relationships"": null
                    }
                }
            }";

        /// <summary>
        /// A config file with a SP entity for which REST APIs are disabled.
        /// This config string is used for validating that none of the REST methods are enabled.
        /// </summary>
        public const string SP_CONFIG_WITH_REST_DISABLED = @"
            {
              ""entities"": {
                    ""GetBooks"": {
                    ""source"": {
                        ""object"": ""get_books"",
                        ""type"": ""stored-procedure"",
                        ""parameters"": null,
                        ""key-fields"": null
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""operation"": ""query"",
                        ""type"": {
                        ""singular"": ""GetBooks"",
                        ""plural"": ""GetBooks""
                        }
                    },
                    ""rest"":{
                        ""enabled"": false
                    },
                    ""permissions"": [
                        {
                        ""role"": ""anonymous"",
                        ""actions"": [
                            {
                            ""action"": ""execute"",
                            ""fields"": null,
                            ""policy"": {
                                ""request"": null,
                                ""database"": null
                            }
                            }
                        ]
                        }
                    ],
                    ""mappings"": null,
                    ""relationships"": null
                    }
                }
            }";

        /// <summary>
        /// A config file with a SP entity for which REST path and methods are not explicitly configured.
        /// This config string is used for validating the default REST behavior.
        /// </summary>
        public const string SP_CONFIG_WITH_JUST_REST_ENABLED = @"
            {
              ""entities"": {
                    ""GetBooks"": {
                    ""source"": {
                        ""object"": ""get_books"",
                        ""type"": ""stored-procedure"",
                        ""parameters"": null,
                        ""key-fields"": null
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""operation"": ""query"",
                        ""type"": {
                        ""singular"": ""GetBooks"",
                        ""plural"": ""GetBooks""
                        }
                    },
                    ""rest"":{
                        ""enabled"": true
                    },
                    ""permissions"": [
                        {
                        ""role"": ""anonymous"",
                        ""actions"": [
                            {
                            ""action"": ""execute"",
                            ""fields"": null,
                            ""policy"": {
                                ""request"": null,
                                ""database"": null
                            }
                            }
                        ]
                        }
                    ],
                    ""mappings"": null,
                    ""relationships"": null
                    }
                }
            }";

        /// <summary>
        /// Invalid properties:
        /// `data-source-file` instead of `data-source-files`
        /// `GraphQL` instead of `graphql` in the global runtime section.
        /// `rst` instead of `rest` in the entity section.
        /// </summary>
        public const string CONFIG_WITH_INVALID_SCHEMA = @"
        {
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""test-connection-string""
            },
            ""data-source-file"": [],
            ""runtime"": {
                ""rest"": {
                    ""enabled"": true,
                    ""path"": ""/api""
                },
                ""Graphql"": {
                    ""enabled"": true,
                    ""path"": ""/graphql"",
                    ""allow-introspection"": true
                },
                ""host"": {
                ""cors"": {
                    ""origins"": [
                    ""http://localhost:5000""
                    ],
                    ""allow-credentials"": false
                },
                ""authentication"": {
                    ""provider"": ""StaticWebApps""
                },
                ""mode"": ""development""
                }
            },
            ""entities"": {
                ""Publisher"": {
                    ""source"": {
                        ""object"": ""publishers"",
                        ""type"": ""table""
                    },
                    ""graphql"": {
                        ""enabled"": true,
                        ""type"": {
                            ""singular"": ""Publisher"",
                            ""plural"": ""Publishers""
                        }
                    },
                    ""rst"": {
                        ""enabled"": true
                    },
                    ""permissions"": [
                        {
                            ""role"": ""anonymous"",
                            ""actions"": [
                                {
                                    ""action"": ""create""
                                }
                            ]
                        }
                    ]
                }
            }
        }";

        internal const string GRAPHQL_SCHEMA_WITH_CYCLE_ARRAY = @"
type Character {
    id : ID,
    name : String,
    moons: [Moon],
}

type Planet @model(name:""PlanetAlias"") {
    id : ID!,
    name : String,
    character: Character
}

type Moon {
    id : ID,
    name : String,
    details : String,
    character: Character
}
";

        internal const string GRAPHQL_SCHEMA_WITH_CYCLE_OBJECT = @"
type Character {
    id : ID,
    name : String,
    moons: Moon,
}

type Planet @model(name:""PlanetAlias"") {
    id : ID!,
    name : String,
    character: Character
}

type Moon {
    id : ID,
    name : String,
    details : String,
    character: Character
}
";

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// When updating config during runtime is possible, then For invalid config the Application continues to
        /// accept request with status code of 503.
        /// But if invalid config is provided during startup, ApplicationException is thrown
        /// and application exits.
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { }, true, DisplayName = "No config returns 503 - config file flag absent")]
        [DataRow(new string[] { "--ConfigFileName=" }, true, DisplayName = "No config returns 503 - empty config file option")]
        [DataRow(new string[] { }, false, DisplayName = "Throws Application exception")]
        [TestMethod("Validates that queries before runtime is configured returns a 503 in hosting scenario whereas an application exception when run through CLI")]
        public async Task TestNoConfigReturnsServiceUnavailable(
            string[] args,
            bool isUpdateableRuntimeConfig)
        {
            TestServer server;

            try
            {
                if (isUpdateableRuntimeConfig)
                {
                    server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(args));
                }
                else
                {
                    server = new(Program.CreateWebHostBuilder(args));
                }

                HttpClient httpClient = server.CreateClient();
                HttpResponseMessage result = await httpClient.GetAsync("/graphql");
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            }
            catch (Exception e)
            {
                Assert.IsFalse(isUpdateableRuntimeConfig);
                Assert.AreEqual(typeof(ApplicationException), e.GetType());
                Assert.AreEqual(
                    $"Could not initialize the engine with the runtime config file: {DEFAULT_CONFIG_FILE_NAME}",
                    e.Message);
            }
        }

        /// <summary>
        /// Verify that https redirection is disabled when --no-https-redirect flag is passed  through CLI.
        /// We check if IsHttpsRedirectionDisabled is set to true with --no-https-redirect flag.
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { "" }, false, DisplayName = "Https redirection allowed")]
        [DataRow(new string[] { Startup.NO_HTTPS_REDIRECT_FLAG }, true, DisplayName = "Http redirection disabled")]
        [TestMethod("Validates that https redirection is disabled when --no-https-redirect option is used when engine is started through CLI")]
        public void TestDisablingHttpsRedirection(
            string[] args,
            bool expectedIsHttpsRedirectionDisabled)
        {
            Program.CreateWebHostBuilder(args).Build();
            Assert.AreEqual(expectedIsHttpsRedirectionDisabled, Program.IsHttpsRedirectionDisabled);
        }

        /// <summary>
        /// Checks correct serialization and deserialization of Source Type from
        /// Enum to String and vice-versa.
        /// Consider both cases for source as an object and as a string
        /// </summary>
        [DataTestMethod]
        [DataRow(true, EntitySourceType.StoredProcedure, "stored-procedure", DisplayName = "source is a stored-procedure")]
        [DataRow(true, EntitySourceType.Table, "table", DisplayName = "source is a table")]
        [DataRow(true, EntitySourceType.View, "view", DisplayName = "source is a view")]
        [DataRow(false, null, null, DisplayName = "source is just string")]
        public void TestCorrectSerializationOfSourceObject(
            bool isDatabaseObjectSource,
            EntitySourceType sourceObjectType,
            string sourceTypeName)
        {
            RuntimeConfig runtimeConfig;
            if (isDatabaseObjectSource)
            {
                EntitySource entitySource = new(
                    Type: sourceObjectType,
                    Object: "sourceName",
                    Parameters: null,
                    KeyFields: null
                );
                runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                    entityName: "MyEntity",
                    entitySource: entitySource,
                    roleName: "Anonymous",
                    operation: EntityActionOperation.All
                );
            }
            else
            {
                string entitySource = "sourceName";
                runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                    entityName: "MyEntity",
                    entitySource: entitySource,
                    roleName: "Anonymous",
                    operation: EntityActionOperation.All
                );
            }

            string runtimeConfigJson = runtimeConfig.ToJson();

            if (isDatabaseObjectSource)
            {
                Assert.IsTrue(runtimeConfigJson.Contains(sourceTypeName));
            }

            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(runtimeConfigJson, out RuntimeConfig deserializedRuntimeConfig));

            Assert.IsTrue(deserializedRuntimeConfig.Entities.ContainsKey("MyEntity"));
            Assert.AreEqual("sourceName", deserializedRuntimeConfig.Entities["MyEntity"].Source.Object);

            if (isDatabaseObjectSource)
            {
                Assert.AreEqual(sourceObjectType, deserializedRuntimeConfig.Entities["MyEntity"].Source.Type);
            }
            else
            {
                Assert.AreEqual(EntitySourceType.Table, deserializedRuntimeConfig.Entities["MyEntity"].Source.Type);
            }
        }

        /// <summary>
        /// Validates that DAB supplements the MSSQL database connection strings with the property "Application Name" and
        /// 1. Adds the property/value "Application Name=dab_oss_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is not set.
        /// 2. Adds the property/value "Application Name=dab_hosted_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is set to "dab_hosted".
        /// (DAB_APP_NAME_ENV is set in hosted scenario or when user sets the value.)
        /// NOTE: "#pragma warning disable format" is used here to avoid removing intentional, readability promoting spacing in DataRow display names.
        /// </summary>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Application Name.</param>
        /// <param name="dabEnvOverride">Whether DAB_APP_NAME_ENV is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        #pragma warning disable format
        [DataTestMethod]
        [DataRow("Data Source=<>;"                              , "Data Source=<>;Application Name="             , false, DisplayName = "[MSSQL]: DAB adds version 'dab_oss_major_minor_patch' to non-provided connection string property 'Application Name'.")]
        [DataRow("Data Source=<>;Application Name=CustAppName;" , "Data Source=<>;Application Name=CustAppName," , false, DisplayName = "[MSSQL]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'Application Name' property.")]
        [DataRow("Data Source=<>;App=CustAppName;"              , "Data Source=<>;Application Name=CustAppName," , false, DisplayName = "[MSSQL]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'App' property and resolves property to 'Application Name'.")]
        [DataRow("Data Source=<>;"                              , "Data Source=<>;Application Name="             , true , DisplayName = "[MSSQL]: DAB adds DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to non-provided connection string property 'Application Name'.")]
        [DataRow("Data Source=<>;Application Name=CustAppName;" , "Data Source=<>;Application Name=CustAppName," , true , DisplayName = "[MSSQL]: DAB appends DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'Application Name' property.")]
        [DataRow("Data Source=<>;App=CustAppName;"              , "Data Source=<>;Application Name=CustAppName," , true , DisplayName = "[MSSQL]: DAB appends version string 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'App' property and resolves property to 'Application Name'.")]
        #pragma warning restore format
        public void MsSqlConnStringSupplementedWithAppNameProperty(
            string configProvidedConnString,
            string expectedDabModifiedConnString,
            bool dabEnvOverride)
        {
            // Explicitly set the DAB_APP_NAME_ENV to null to ensure that the DAB_APP_NAME_ENV is not set.
            if (dabEnvOverride)
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, "dab_hosted");
            }
            else
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);
            }

            // Resolve assembly version. Not possible to do in DataRow as DataRows expect compile-time constants.
            string resolvedAssemblyVersion = ProductInfo.GetDataApiBuilderUserAgent();
            expectedDabModifiedConnString += resolvedAssemblyVersion;

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(DatabaseType.MSSQL, configProvidedConnString);

            // Act
            bool configParsed = RuntimeConfigLoader.TryParseConfig(
                runtimeConfig.ToJson(),
                out RuntimeConfig updatedRuntimeConfig,
                replaceEnvVar: true);

            // Assert
            Assert.AreEqual(
                expected: true,
                actual: configParsed,
                message: "Runtime config unexpectedly failed parsing.");
            Assert.AreEqual(
                expected: expectedDabModifiedConnString,
                actual: updatedRuntimeConfig.DataSource.ConnectionString,
                message: "DAB did not properly set the 'Application Name' connection string property.");
        }

        /// <summary>
        /// Validates that DAB supplements the PgSQL database connection strings with the property "ApplicationName" and
        /// 1. Adds the property/value "Application Name=dab_oss_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is not set.
        /// 2. Adds the property/value "Application Name=dab_hosted_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is set to "dab_hosted".
        /// (DAB_APP_NAME_ENV is set in hosted scenario or when user sets the value.)
        /// NOTE: "#pragma warning disable format" is used here to avoid removing intentional, readability promoting spacing in DataRow display names.
        /// </summary>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Application Name.</param>
        /// <param name="dabEnvOverride">Whether DAB_APP_NAME_ENV is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        [DataTestMethod]
        [DataRow("Host=foo;Username=testuser;", "Host=foo;Username=testuser;Application Name=", false, DisplayName = "[PGSQL]:DAB adds version 'dab_oss_major_minor_patch' to non-provided connection string property 'ApplicationName']")]
        [DataRow("Host=foo;Username=testuser;", "Host=foo;Username=testuser;Application Name=", true, DisplayName = "[PGSQL]:DAB adds DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to non-provided connection string property 'ApplicationName'.]")]
        [DataRow("Host=foo;Username=testuser;Application Name=UserAppName", "Host=foo;Username=testuser;Application Name=UserAppName,", false, DisplayName = "[PGSQL]:DAB appends version 'dab_oss_major_minor_patch' to user supplied 'Application Name' property.]")]
        [DataRow("Host=foo;Username=testuser;Application Name=UserAppName", "Host=foo;Username=testuser;Application Name=UserAppName,", true, DisplayName = "[PGSQL]:DAB appends version string 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'ApplicationName' property.]")]
        public void PgSqlConnStringSupplementedWithAppNameProperty(
            string configProvidedConnString,
            string expectedDabModifiedConnString,
            bool dabEnvOverride)
        {
            // Explicitly set the DAB_APP_NAME_ENV to null to ensure that the DAB_APP_NAME_ENV is not set.
            if (dabEnvOverride)
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, "dab_hosted");
            }
            else
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);
            }

            // Resolve assembly version. Not possible to do in DataRow as DataRows expect compile-time constants.
            string resolvedAssemblyVersion = ProductInfo.GetDataApiBuilderUserAgent();
            expectedDabModifiedConnString += resolvedAssemblyVersion;

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(DatabaseType.PostgreSQL, configProvidedConnString);

            // Act
            bool configParsed = RuntimeConfigLoader.TryParseConfig(
                runtimeConfig.ToJson(),
                out RuntimeConfig updatedRuntimeConfig,
                replaceEnvVar: true);

            // Assert
            Assert.AreEqual(
                expected: true,
                actual: configParsed,
                message: "Runtime config unexpectedly failed parsing.");
            Assert.AreEqual(
                expected: expectedDabModifiedConnString,
                actual: updatedRuntimeConfig.DataSource.ConnectionString,
                message: "DAB did not properly set the 'Application Name' connection string property.");
        }

        /// <summary>
        /// Validates that DAB doesn't append nor modify
        /// - the 'Application Name' or 'App' properties in MySQL database connection strings.
        /// - the 'Application Name' property in
        /// CosmosDB_PostgreSQL, CosmosDB_NoSQL database connection strings.
        /// This test validates that this behavior holds true when the DAB_APP_NAME_ENV environment variable
        /// - is set (dabEnvOverride==true) -> (DAB hosted)
        /// - is not set (dabEnvOverride==false) -> (DAB OSS).
        /// </summary>
        /// <param name="databaseType">database type.</param>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Application Name.</param>
        /// <param name="dabEnvOverride">Whether DAB_APP_NAME_ENV is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        #pragma warning disable format
        [DataTestMethod]
        [DataRow(DatabaseType.MySQL, "Something;"                                 , "Something;"                                 , false, DisplayName = "[MYSQL|DAB OSS]:No addition of 'Application Name' or 'App' property to connection string.")]
        [DataRow(DatabaseType.MySQL, "Something;Application Name=CustAppName;"    , "Something;Application Name=CustAppName;"    , false, DisplayName = "[MYSQL|DAB OSS]:No modification of customer overridden 'Application Name' property.")]
        [DataRow(DatabaseType.MySQL, "Something1;App=CustAppName;Something2;"     , "Something1;App=CustAppName;Something2;"     , false, DisplayName = "[MySQL|DAB OSS]:No modification of customer overridden 'App' property.")]
        [DataRow(DatabaseType.MySQL, "Something;"                                 , "Something;"                                 , true , DisplayName = "[MYSQL|DAB hosted]:No addition of 'Application Name' or 'App' property to connection string.")]
        [DataRow(DatabaseType.MySQL, "Something;Application Name=CustAppName;"    , "Something;Application Name=CustAppName;"    , true , DisplayName = "[MYSQL|DAB hosted]:No modification of customer overridden 'Application Name' property.")]
        [DataRow(DatabaseType.MySQL, "Something1;App=CustAppName;Something2;"     , "Something1;App=CustAppName;Something2;"     , true, DisplayName = "[MySQL|DAB hosted]:No modification of customer overridden 'App' property.")]
        [DataRow(DatabaseType.CosmosDB_NoSQL, "Something;"                             , "Something;"                             , false, DisplayName = "[COSMOSDB_NOSQL|DAB OSS]:No addition of 'Application Name' property to connection string.")]
        [DataRow(DatabaseType.CosmosDB_NoSQL, "Something;Application Name=CustAppName;", "Something;Application Name=CustAppName;", false, DisplayName = "[COSMOSDB_NOSQL|DAB OSS]:No modification of customer overridden 'Application Name' property.")]
        [DataRow(DatabaseType.CosmosDB_NoSQL, "Something;"                             , "Something;"                             , true , DisplayName = "[COSMOSDB_NOSQL|DAB hosted]:No addition of 'Application Name' property to connection string.")]
        [DataRow(DatabaseType.CosmosDB_NoSQL, "Something;Application Name=CustAppName;", "Something;Application Name=CustAppName;", true , DisplayName = "[COSMOSDB_NOSQL|DAB hosted]:No modification of customer overridden 'Application Name' property.")]
        [DataRow(DatabaseType.CosmosDB_PostgreSQL, "Something;"                             , "Something;"                             , false, DisplayName = "[COSMOSDB_PGSQL|DAB OSS]:No addition of 'Application Name' property to connection string.")]
        [DataRow(DatabaseType.CosmosDB_PostgreSQL, "Something;Application Name=CustAppName;", "Something;Application Name=CustAppName;", false, DisplayName = "[COSMOSDB_PGSQL|DAB OSS]:No modification of customer overridden 'Application Name' property.")]
        [DataRow(DatabaseType.CosmosDB_PostgreSQL, "Something;"                             , "Something;"                             , true , DisplayName = "[COSMOSDB_PGSQL|DAB hosted]:No addition of 'Application Name' property to connection string.")]
        [DataRow(DatabaseType.CosmosDB_PostgreSQL, "Something;Application Name=CustAppName;", "Something;Application Name=CustAppName;", true , DisplayName = "[COSMOSDB_PGSQL|DAB hosted]:No modification of customer overridden 'Application Name' property.")]
        #pragma warning restore format
        public void TestConnectionStringIsCorrectlyUpdatedWithApplicationName(
            DatabaseType databaseType,
            string configProvidedConnString,
            string expectedDabModifiedConnString,
            bool dabEnvOverride)
        {
            // Explicitly set the DAB_APP_NAME_ENV to null to ensure that the DAB_APP_NAME_ENV is not set.
            if (dabEnvOverride)
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, "dab_hosted");
            }
            else
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);
            }

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(databaseType, configProvidedConnString);

            // Act
            bool configParsed = RuntimeConfigLoader.TryParseConfig(
                runtimeConfig.ToJson(),
                out RuntimeConfig updatedRuntimeConfig,
                replaceEnvVar: true);

            // Assert
            Assert.AreEqual(
                expected: true,
                actual: configParsed,
                message: "Runtime config unexpectedly failed parsing.");
            Assert.AreEqual(
                expected: expectedDabModifiedConnString,
                actual: updatedRuntimeConfig.DataSource.ConnectionString,
                message: "DAB did not properly set the 'Application Name' connection string property.");
        }

        [TestMethod("Validates that once the configuration is set, the config controller isn't reachable."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestConflictAlreadySetConfiguration(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            _ = await httpClient.PostAsync(configurationEndpoint, content);
            ValidateCosmosDbSetup(server);

            HttpResponseMessage result = await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that the config controller returns a conflict when using local configuration."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestConflictLocalConfiguration(string configurationEndpoint)
        {
            Environment.SetEnvironmentVariable
                (ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ValidateCosmosDbSetup(server);

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage result =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates setting the configuration at runtime."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSettingConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
        }

        [TestMethod("Validates an invalid configuration returns a bad request."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestInvalidConfigurationAtRuntime(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint, "invalidString");

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.BadRequest, postResult.StatusCode);
        }

        [TestMethod("Validates a failure in one of the config updated handlers returns a bad request."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSettingFailureConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            RuntimeConfigProvider runtimeConfigProvider = server.Services.GetService<RuntimeConfigProvider>();
            runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add((_, _) =>
            {
                return Task.FromResult(false);
            });

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);

            Assert.AreEqual(HttpStatusCode.BadRequest, postResult.StatusCode);
        }

        [TestMethod("Validates that the configuration endpoint doesn't return until all configuration loaded handlers have executed."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestLongRunningConfigUpdatedHandlerConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            RuntimeConfigProvider runtimeConfigProvider = server.Services.GetService<RuntimeConfigProvider>();
            bool taskHasCompleted = false;
            runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
            {
                await Task.Delay(1000);
                taskHasCompleted = true;
                return true;
            });

            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);

            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
            Assert.IsTrue(taskHasCompleted);
        }

        /// <summary>
        /// Tests that sending configuration to the DAB engine post-startup will properly hydrate
        /// the AuthorizationResolver by:
        /// 1. Validate that pre-configuration hydration requests result in 503 Service Unavailable
        /// 2. Validate that custom configuration hydration succeeds.
        /// 3. Validate that request to protected entity without role membership triggers Authorization Resolver
        /// to reject the request with HTTP 403 Forbidden.
        /// 4. Validate that request to protected entity with required role membership passes authorization requirements
        /// and succeeds with HTTP 200 OK.
        /// Note: This test is database engine agnostic, though requires denoting a database environment to fetch a usable
        /// connection string to complete the test. Most applicable to CI/CD test execution.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod("Validates setting the AuthN/Z configuration post-startup during runtime.")]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSqlSettingPostStartupConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            RuntimeConfig configuration = AuthorizationHelpers.InitRuntimeConfig(
                entityName: POST_STARTUP_CONFIG_ENTITY,
                entitySource: POST_STARTUP_CONFIG_ENTITY_SOURCE,
                roleName: POST_STARTUP_CONFIG_ROLE,
                operation: EntityActionOperation.Read,
                includedCols: new HashSet<string>() { "*" });

            JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);

            HttpResponseMessage preConfigHydrationResult =
                await httpClient.GetAsync($"/{POST_STARTUP_CONFIG_ENTITY}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigHydrationResult.StatusCode);

            HttpResponseMessage preConfigOpenApiDocumentExistence =
                await httpClient.GetAsync($"{RestRuntimeOptions.DEFAULT_PATH}/{OPENAPI_DOCUMENT_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigOpenApiDocumentExistence.StatusCode);

            // SwaggerUI (OpenAPI user interface) is not made available in production/hosting mode.
            HttpResponseMessage preConfigOpenApiSwaggerEndpointAvailability =
                await httpClient.GetAsync($"/{OPENAPI_SWAGGER_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigOpenApiSwaggerEndpointAvailability.StatusCode);

            HttpStatusCode responseCode = await HydratePostStartupConfiguration(httpClient, content, configurationEndpoint);

            // When the authorization resolver is properly configured, authorization will have failed
            // because no auth headers are present.
            Assert.AreEqual(
                expected: HttpStatusCode.Forbidden,
                actual: responseCode,
                message: "Configuration not yet hydrated after retry attempts..");

            // Sends a GET request to a protected entity which requires a specific role to access.
            // Authorization will pass because proper auth headers are present.
            HttpRequestMessage message = new(method: HttpMethod.Get, requestUri: $"api/{POST_STARTUP_CONFIG_ENTITY}");
            string swaTokenPayload = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                addAuthenticated: true,
                specificRole: POST_STARTUP_CONFIG_ROLE);
            message.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER, swaTokenPayload);
            message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, POST_STARTUP_CONFIG_ROLE);
            HttpResponseMessage authorizedResponse = await httpClient.SendAsync(message);
            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);

            // OpenAPI document is created during config hydration and
            // is made available after config hydration completes.
            HttpResponseMessage postConfigOpenApiDocumentExistence =
                await httpClient.GetAsync($"{RestRuntimeOptions.DEFAULT_PATH}/{OPENAPI_DOCUMENT_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.OK, postConfigOpenApiDocumentExistence.StatusCode);

            // SwaggerUI (OpenAPI user interface) is not made available in production/hosting mode.
            // HTTP 400 - BadRequest because when SwaggerUI is disabled, the endpoint is not mapped
            // and the request is processed and failed by the RestService.
            HttpResponseMessage postConfigOpenApiSwaggerEndpointAvailability =
                await httpClient.GetAsync($"/{OPENAPI_SWAGGER_ENDPOINT}");
            Assert.AreEqual(HttpStatusCode.BadRequest, postConfigOpenApiSwaggerEndpointAvailability.StatusCode);
        }

        /// <summary>
        /// Tests that sending configuration to the DAB engine post-startup will properly hydrate even with data-source-files specified.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod("Validates RuntimeConfig setup for post-configuraiton hydration with datasource-files specified.")]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestValidMultiSourceRunTimePostStartupConfigurations(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            RuntimeConfig config = AuthorizationHelpers.InitRuntimeConfig(
                entityName: POST_STARTUP_CONFIG_ENTITY,
                entitySource: POST_STARTUP_CONFIG_ENTITY_SOURCE,
                roleName: POST_STARTUP_CONFIG_ROLE,
                operation: EntityActionOperation.Read,
                includedCols: new HashSet<string>() { "*" });

            // Set up Configuration with DataSource files.
            config = config with { DataSourceFiles = new DataSourceFiles(new List<String>() { "file1", "file2" }) };

            JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, config, configurationEndpoint);

            HttpResponseMessage postResult = await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();

            Assert.IsNotNull(configProvider, "Configuration Provider shouldn't be null after setting the configuration at runtime.");
            Assert.IsTrue(configProvider.TryGetConfig(out RuntimeConfig configuration), "TryGetConfig should return true when the config is set.");
            Assert.IsNotNull(configuration, "Config returned should not be null.");

            Assert.IsNotNull(configuration.DataSource, "The base datasource should get populated in case of late hydration of config inspite of invalid multi-db files.");
            Assert.AreEqual(1, configuration.ListAllDataSources().Count(), "There should be only 1 datasource populated for late hydration of config with invalid multi-db files.");
        }

        [TestMethod("Validates that local CosmosDB_NoSQL settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestLoadingLocalCosmosSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates access token is correctly loaded when Account Key is not present for Cosmos."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestLoadingAccessTokenForCosmosClient(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint, null, true);

            HttpResponseMessage authorizedResponse = await httpClient.PostAsync(configurationEndpoint, content);

            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);
            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Clients);
            Assert.IsTrue(cosmosClientProvider.Clients.Any());
        }

        [TestMethod("Validates that local MsSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MSSQL)]
        public void TestLoadingLocalMsSqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            QueryEngineFactory queryEngineFactory = (QueryEngineFactory)server.Services.GetService(typeof(IQueryEngineFactory));
            Assert.IsInstanceOfType(queryEngineFactory.GetQueryEngine(DatabaseType.MSSQL), typeof(SqlQueryEngine));

            MutationEngineFactory mutationEngineFactory = (MutationEngineFactory)server.Services.GetService(typeof(IMutationEngineFactory));
            Assert.IsInstanceOfType(mutationEngineFactory.GetMutationEngine(DatabaseType.MSSQL), typeof(SqlMutationEngine));

            QueryManagerFactory queryManagerFactory = (QueryManagerFactory)server.Services.GetService(typeof(IAbstractQueryManagerFactory));
            Assert.IsInstanceOfType(queryManagerFactory.GetQueryBuilder(DatabaseType.MSSQL), typeof(MsSqlQueryBuilder));
            Assert.IsInstanceOfType(queryManagerFactory.GetQueryExecutor(DatabaseType.MSSQL), typeof(MsSqlQueryExecutor));

            MetadataProviderFactory metadataProviderFactory = (MetadataProviderFactory)server.Services.GetService(typeof(IMetadataProviderFactory));
            Assert.IsTrue(metadataProviderFactory.ListMetadataProviders().Any(x => x.GetType() == typeof(MsSqlMetadataProvider)));
        }

        [TestMethod("Validates that local PostgreSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.POSTGRESQL)]
        public void TestLoadingLocalPostgresSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, POSTGRESQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            QueryEngineFactory queryEngineFactory = (QueryEngineFactory)server.Services.GetService(typeof(IQueryEngineFactory));
            Assert.IsInstanceOfType(queryEngineFactory.GetQueryEngine(DatabaseType.PostgreSQL), typeof(SqlQueryEngine));

            MutationEngineFactory mutationEngineFactory = (MutationEngineFactory)server.Services.GetService(typeof(IMutationEngineFactory));
            Assert.IsInstanceOfType(mutationEngineFactory.GetMutationEngine(DatabaseType.PostgreSQL), typeof(SqlMutationEngine));

            QueryManagerFactory queryManagerFactory = (QueryManagerFactory)server.Services.GetService(typeof(IAbstractQueryManagerFactory));
            Assert.IsInstanceOfType(queryManagerFactory.GetQueryBuilder(DatabaseType.PostgreSQL), typeof(PostgresQueryBuilder));
            Assert.IsInstanceOfType(queryManagerFactory.GetQueryExecutor(DatabaseType.PostgreSQL), typeof(PostgreSqlQueryExecutor));

            MetadataProviderFactory metadataProviderFactory = (MetadataProviderFactory)server.Services.GetService(typeof(IMetadataProviderFactory));
            Assert.IsTrue(metadataProviderFactory.ListMetadataProviders().Any(x => x.GetType() == typeof(PostgreSqlMetadataProvider)));
        }

        [TestMethod("Validates that local MySql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MYSQL)]
        public void TestLoadingLocalMySqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MYSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            QueryEngineFactory queryEngineFactory = (QueryEngineFactory)server.Services.GetService(typeof(IQueryEngineFactory));
            Assert.IsInstanceOfType(queryEngineFactory.GetQueryEngine(DatabaseType.MySQL), typeof(SqlQueryEngine));

            MutationEngineFactory mutationEngineFactory = (MutationEngineFactory)server.Services.GetService(typeof(IMutationEngineFactory));
            Assert.IsInstanceOfType(mutationEngineFactory.GetMutationEngine(DatabaseType.MySQL), typeof(SqlMutationEngine));

            QueryManagerFactory queryManagerFactory = (QueryManagerFactory)server.Services.GetService(typeof(IAbstractQueryManagerFactory));
            Assert.IsInstanceOfType(queryManagerFactory.GetQueryBuilder(DatabaseType.MySQL), typeof(MySqlQueryBuilder));
            Assert.IsInstanceOfType(queryManagerFactory.GetQueryExecutor(DatabaseType.MySQL), typeof(MySqlQueryExecutor));

            MetadataProviderFactory metadataProviderFactory = (MetadataProviderFactory)server.Services.GetService(typeof(IMetadataProviderFactory));
            Assert.IsTrue(metadataProviderFactory.ListMetadataProviders().Any(x => x.GetType() == typeof(MySqlMetadataProvider)));
        }

        [TestMethod("Validates that trying to override configs that are already set fail."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestOverridingLocalSettingsFails(string configurationEndpoint)
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            JsonContent config = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage postResult = await client.PostAsync(configurationEndpoint, config);
            Assert.AreEqual(HttpStatusCode.Conflict, postResult.StatusCode);
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(CONFIGURATION_ENDPOINT)]
        [DataRow(CONFIGURATION_ENDPOINT_V2)]
        public async Task TestSettingConfigurationCreatesCorrectClasses(string configurationEndpoint)
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

            HttpResponseMessage postResult = await client.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
            RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();

            Assert.IsNotNull(configProvider, "Configuration Provider shouldn't be null after setting the configuration at runtime.");
            Assert.IsTrue(configProvider.TryGetConfig(out RuntimeConfig configuration), "TryGetConfig should return true when the config is set.");
            Assert.IsNotNull(configuration, "Config returned should not be null.");

            ConfigurationPostParameters expectedParameters = GetCosmosConfigurationParameters();
            Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, configuration.DataSource.DatabaseType, "Expected CosmosDB_NoSQL database type after configuring the runtime with CosmosDB_NoSQL settings.");
            CosmosDbNoSQLDataSourceOptions options = configuration.DataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>();
            Assert.IsNotNull(options);
            Assert.AreEqual(expectedParameters.Schema, options.GraphQLSchema, "Expected the schema in the configuration to match the one sent to the configuration endpoint.");

            // Don't use Assert.AreEqual, because a failure will print the entire connection string in the error message.
            Assert.IsTrue(expectedParameters.ConnectionString == configuration.DataSource.ConnectionString, "Expected the connection string in the configuration to match the one sent to the configuration endpoint.");
            string db = options.Database;
            Assert.AreEqual(COSMOS_DATABASE_NAME, db, "Expected the database name in the runtime config to match the one sent to the configuration endpoint.");
        }

        [TestMethod("Validates that an exception is thrown if there's a null model in filter parser.")]
        public void VerifyExceptionOnNullModelinFilterParser()
        {
            ODataParser parser = new();
            try
            {
                // FilterParser has no model so we expect exception
                parser.GetFilterClause(filterQueryString: string.Empty, resourcePath: string.Empty);
                Assert.Fail();
            }
            catch (DataApiBuilderException exception)
            {
                Assert.AreEqual("The runtime has not been initialized with an Edm model.", exception.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            }
        }

        /// <summary>
        /// This test reads the dab-config.MsSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of MsSql config file succeeds."), TestCategory(TestCategory.MSSQL)]
        public Task TestReadingRuntimeConfigForMsSql()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{CONFIGFILE_NAME}.{MSSQL_ENVIRONMENT}{CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.MySql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of MySql config file succeeds."), TestCategory(TestCategory.MYSQL)]
        public Task TestReadingRuntimeConfigForMySql()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{CONFIGFILE_NAME}.{MYSQL_ENVIRONMENT}{CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.PostgreSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of PostgreSql config file succeeds."), TestCategory(TestCategory.POSTGRESQL)]
        public Task TestReadingRuntimeConfigForPostgreSql()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{CONFIGFILE_NAME}.{POSTGRESQL_ENVIRONMENT}{CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.CosmosDb_NoSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of the CosmosDB_NoSQL config file succeeds."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public Task TestReadingRuntimeConfigForCosmos()
        {
            return ConfigFileDeserializationValidationHelper(File.ReadAllText($"{CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// Helper method to validate the deserialization of the "entities" section of the config file
        /// This is used in unit tests that validate the deserialization of the config files
        /// </summary>
        /// <param name="runtimeConfig"></param>
        private Task ConfigFileDeserializationValidationHelper(string jsonString)
        {
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(jsonString, out RuntimeConfig runtimeConfig), "Deserialization of the config file failed.");
            return Verify(runtimeConfig);
        }

        /// <summary>
        /// This function verifies command line configuration provider takes higher
        /// precedence than default configuration file dab-config.json
        /// </summary>
        [TestMethod("Validates command line configuration provider."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestCommandLineConfigurationProvider()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            string[] args = new[]
            {
            $"--ConfigFileName={CONFIGFILE_NAME}." +
            $"{COSMOS_ENVIRONMENT}{CONFIG_EXTENSION}"
        };

            TestServer server = new(Program.CreateWebHostBuilder(args));

            ValidateCosmosDbSetup(server);
        }

        /// <summary>
        /// This function verifies the environment variable DAB_ENVIRONMENT
        /// takes precedence than ASPNETCORE_ENVIRONMENT for the configuration file.
        /// </summary>
        [TestMethod("Validates precedence is given to DAB_ENVIRONMENT environment variable name."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestRuntimeEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(
                ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                RUNTIME_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);

            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        /// <summary>
        /// This method tests the config properties like data-source, runtime settings and entities.
        /// </summary>
        [TestMethod("Validates the runtime configuration file properties."), TestCategory(TestCategory.MSSQL)]
        public void TestConfigPropertiesAreValid()
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystemRuntimeConfigLoader configPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configPath);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            RuntimeConfigValidator configValidator =
                new(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object);

            configValidator.ValidateConfigProperties();
        }

        /// <summary>
        /// This method tests that config file is validated correctly and no exceptions are thrown.
        /// This tests gets the json from the integration test config file and then uses that
        /// to validate the complete config file.
        /// </summary>
        [TestMethod("Validates the complete config."), TestCategory(TestCategory.MSSQL)]
        public async Task TestConfigIsValid()
        {
            // Fetch the MS_SQL integration test config file.
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystemRuntimeConfigLoader testConfigPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfig configuration = TestHelper.GetRuntimeConfigProvider(testConfigPath).GetConfig();
            const string CUSTOM_CONFIG = "custom-config.json";

            MockFileSystem fileSystem = new();

            // write it to the custom-config file and add it to the filesystem.
            fileSystem.AddFile(CUSTOM_CONFIG, new MockFileData(configuration.ToJson()));
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem);
            configLoader.UpdateConfigFilePath(CUSTOM_CONFIG);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configLoader);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            RuntimeConfigValidator configValidator =
                new(
                    configProvider,
                    fileSystem,
                    configValidatorLogger.Object,
                    true);

            try
            {
                // Run the validate on the custom config json file.
                Assert.IsTrue(await configValidator.TryValidateConfig(CUSTOM_CONFIG, TestHelper.ProvisionLoggerFactory()));
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }
        }

        /// <summary>
        /// Test to verify that provided invalid value of depth-limit in the config file should
        /// result in validation failure during `dab validate` and `dab start`.
        /// </summary>
        [DataTestMethod]
        [DataRow(0, DisplayName = "[FAIL]: Invalid Value: 0 for depth-limit.")]
        [DataRow(-2, DisplayName = "[FAIL]: Invalid Value: -2 for depth-limit.")]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestValidateConfigForInvalidDepthLimit(int? depthLimit)
        {
            await ValidateConfigWithDepthLimit(depthLimit, expectedSuccess: false);
        }

        /// <summary>
        /// Test to verify that provided valid value of depth-limit in the config file should not
        /// result in any validation failure during `dab validate` and `dab start`.
        /// -1 and null are special values.
        /// -1 can be set to remove the depth limit, while `null` is the default value which means no depth limit check.
        /// </summary>
        [DataTestMethod]
        [DataRow(-1, DisplayName = "[PASS]: Valid Value: -1 to disable depth limit")]
        [DataRow(2, DisplayName = "[PASS]: Valid Value: 2 for depth-limit.")]
        [DataRow(2147483647, DisplayName = "[PASS]: Valid Value: Using Int32.MaxValue(2147483647) for depth-limit.")]
        [DataRow(null, DisplayName = "[PASS]: Default Value: null for depth-limit.")]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestValidateConfigForValidDepthLimit(int? depthLimit)
        {
            await ValidateConfigWithDepthLimit(depthLimit, expectedSuccess: true);
        }

        /// <summary>
        /// This method validates that depth-limit outside the valid range should fail validation
        /// during `dab validate` and `dab start`.     
        /// </summary>
        /// <param name="depthLimit"></param>
        /// <param name="expectedSuccess"></param>
        private static async Task ValidateConfigWithDepthLimit(int? depthLimit, bool expectedSuccess)
        {
            // Arrange: Common setup logic
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            const string CUSTOM_CONFIG = "custom-config.json";
            FileSystemRuntimeConfigLoader testConfigPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfig configuration = TestHelper.GetRuntimeConfigProvider(testConfigPath).GetConfig();
            configuration = configuration with
            {
                Runtime = configuration.Runtime with
                {
                    GraphQL = configuration.Runtime.GraphQL with { DepthLimit = depthLimit, UserProvidedDepthLimit = true }
                }
            };

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(CUSTOM_CONFIG, new MockFileData(configuration.ToJson()));
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem);
            configLoader.UpdateConfigFilePath(CUSTOM_CONFIG);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configLoader);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            RuntimeConfigValidator configValidator = new(configProvider, fileSystem, configValidatorLogger.Object, true);

            // Act
            bool isSuccess = await configValidator.TryValidateConfig(CUSTOM_CONFIG, TestHelper.ProvisionLoggerFactory());

            // Assert based on expected success
            Assert.AreEqual(expectedSuccess, isSuccess);
        }

        /// <summary>
        /// This test method checks a valid config's entities against
        /// the database and ensures they are valid.
        /// </summary>
        [TestMethod("Validation passes for valid entities against database."), TestCategory(TestCategory.MSSQL)]
        public async Task TestSqlMetadataForValidConfigEntities()
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystemRuntimeConfigLoader configPath = TestHelper.GetRuntimeConfigLoader();
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configPath);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            ILoggerFactory mockLoggerFactory = TestHelper.ProvisionLoggerFactory();

            RuntimeConfigValidator configValidator =
                new(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object,
                    isValidateOnly: true);

            configValidator.ValidateRelationshipConfigCorrectness(configProvider.GetConfig());
            await configValidator.ValidateEntitiesMetadata(configProvider.GetConfig(), mockLoggerFactory);
            Assert.IsTrue(configValidator.ConfigValidationExceptions.IsNullOrEmpty());
        }

        /// <summary>
        /// This test method checks a valid config's entities against
        /// the database and ensures they are valid.
        /// The config contains an entity source object not present in the database.
        /// It also contains an entity whose source is incorrectly specified as a stored procedure.
        /// </summary>
        [TestMethod("Validation fails for invalid entities against database."), TestCategory(TestCategory.MSSQL)]
        public async Task TestSqlMetadataForInvalidConfigEntities()
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL),
                Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new());

            // creating an entity with invalid table name
            Entity entityWithInvalidSourceName = new(
                Source: new("bokos", EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "book", Plural: "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            Entity entityWithInvalidSourceType = new(
                Source: new("publishers", EntitySourceType.StoredProcedure, null, null),
                Rest: null,
                GraphQL: new(Singular: "publisher", Plural: "publishers"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_AUTHENTICATED) },
                Relationships: null,
                Mappings: null
                );

            configuration = configuration with
            {
                Entities = new RuntimeEntities(new Dictionary<string, Entity>()
                    {
                        { "Book", entityWithInvalidSourceName },
                        { "Publisher", entityWithInvalidSourceType}
                    })
            };

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            FileSystemRuntimeConfigLoader configLoader = TestHelper.GetRuntimeConfigLoader();
            configLoader.UpdateConfigFilePath(CUSTOM_CONFIG);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configLoader);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            RuntimeConfigValidator configValidator =
                new(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object,
                    isValidateOnly: true);

            ILoggerFactory mockLoggerFactory = TestHelper.ProvisionLoggerFactory();

            configValidator.ValidateRelationshipConfigCorrectness(configProvider.GetConfig());
            await configValidator.ValidateEntitiesMetadata(configProvider.GetConfig(), mockLoggerFactory);

            Assert.IsTrue(configValidator.ConfigValidationExceptions.Any());
            Assert.AreEqual(2, configValidator.ConfigValidationExceptions.Count);
            List<Exception> exceptionsList = configValidator.ConfigValidationExceptions;
            Assert.AreEqual("Cannot obtain Schema for entity Book with underlying database "
                + "object source: dbo.bokos due to: Invalid object name 'dbo.bokos'.", exceptionsList[0].Message);
            Assert.AreEqual("No stored procedure definition found for the given database object publishers", exceptionsList[1].Message);
        }

        /// <summary>
        /// This Test validates that when the entities in the runtime config have source object as null,
        /// the validation exception handler collects the message and exits gracefully.
        /// </summary>
        [TestMethod("Validate Exception handling for Entities with Source object as null."), TestCategory(TestCategory.MSSQL)]
        public async Task TestSqlMetadataValidationForEntitiesWithInvalidSource()
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL),
                Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new());

            // creating an entity with invalid table name
            Entity entityWithInvalidSource = new(
                Source: new(null, EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "book", Plural: "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            // creating an entity with invalid source object and adding relationship with an entity with invalid source
            Entity entityWithInvalidSourceAndRelationship = new(
                Source: new(null, EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "publisher", Plural: "publishers"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: new Dictionary<string, EntityRelationship>() { {"books", new (
                    Cardinality: Cardinality.Many,
                    TargetEntity: "Book",
                    SourceFields: null,
                    TargetFields: null,
                    LinkingObject: null,
                    LinkingSourceFields: null,
                    LinkingTargetFields: null
                    )}},
                Mappings: null
                );

            configuration = configuration with
            {
                Entities = new RuntimeEntities(new Dictionary<string, Entity>()
                    {
                        { "Book", entityWithInvalidSource },
                        { "Publisher", entityWithInvalidSourceAndRelationship}
                    })
            };

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            FileSystemRuntimeConfigLoader configLoader = TestHelper.GetRuntimeConfigLoader();
            configLoader.UpdateConfigFilePath(CUSTOM_CONFIG);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configLoader);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            RuntimeConfigValidator configValidator =
                new(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object,
                    isValidateOnly: true);

            ILoggerFactory mockLoggerFactory = TestHelper.ProvisionLoggerFactory();

            try
            {
                configValidator.ValidateRelationshipConfigCorrectness(configProvider.GetConfig());
                await configValidator.ValidateEntitiesMetadata(configProvider.GetConfig(), mockLoggerFactory);
            }
            catch
            {
                Assert.Fail("Execution of dab validate should not result in unhandled exceptions.");
            }

            Assert.IsTrue(configValidator.ConfigValidationExceptions.Any());
            List<string> exceptionMessagesList = configValidator.ConfigValidationExceptions.Select(x => x.Message).ToList();
            Assert.IsTrue(exceptionMessagesList.Contains("The entity Book does not have a valid source object."));
            Assert.IsTrue(exceptionMessagesList.Contains("The entity Publisher does not have a valid source object."));
            Assert.IsTrue(exceptionMessagesList.Contains("Table Definition for Book has not been inferred."));
            Assert.IsTrue(exceptionMessagesList.Contains("Table Definition for Publisher has not been inferred."));
            Assert.IsTrue(exceptionMessagesList.Contains("Could not infer database object for source entity: Publisher in relationship: books. Check if the entity: Publisher is correctly defined in the config."));
            Assert.IsTrue(exceptionMessagesList.Contains("Could not infer database object for target entity: Book in relationship: books. Check if the entity: Book is correctly defined in the config."));
        }

        /// <summary>
        /// This test method validates a sample DAB runtime config file against DAB's JSON schema definition.
        /// It asserts that the validation is successful and there are no validation failures.
        /// It also verifies that the expected log message is logged.
        /// </summary>
        [TestMethod("Validates the config file schema."), TestCategory(TestCategory.MSSQL)]
        public async Task TestConfigSchemaIsValid()
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystemRuntimeConfigLoader configLoader = TestHelper.GetRuntimeConfigLoader();

            Mock<ILogger<JsonConfigSchemaValidator>> schemaValidatorLogger = new();

            string jsonSchema = File.ReadAllText("dab.draft.schema.json");
            string jsonData = File.ReadAllText(configLoader.ConfigFilePath);

            JsonConfigSchemaValidator jsonSchemaValidator = new(schemaValidatorLogger.Object, new MockFileSystem());

            JsonSchemaValidationResult result = await jsonSchemaValidator.ValidateJsonConfigWithSchemaAsync(jsonSchema, jsonData);
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.ValidationErrors.IsNullOrEmpty());
            schemaValidatorLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"The config satisfies the schema requirements.")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()),
                Times.Once);
        }

        /// <summary>
        /// This test tries to validate a runtime config file that is not compliant with the runtime config JSON schema.
        /// It validates no additional properties are defined in the config file.
        /// The config file used here contains `data-source-file` instead of `data-source-files`,
        /// and `graphql` property in runtime is written as `GraphQL` in the Global runtime section.
        /// It also contains an entity where `rest` property is written as `rst`.
        /// </summary>
        [TestMethod("Validates the invalid config file schema."), TestCategory(TestCategory.MSSQL)]
        public async Task TestConfigSchemaIsInvalid()
        {
            Mock<ILogger<JsonConfigSchemaValidator>> schemaValidatorLogger = new();

            string jsonSchema = File.ReadAllText("dab.draft.schema.json");

            JsonConfigSchemaValidator jsonSchemaValidator = new(schemaValidatorLogger.Object, new MockFileSystem());
            JsonSchemaValidationResult result = await jsonSchemaValidator.ValidateJsonConfigWithSchemaAsync(jsonSchema, CONFIG_WITH_INVALID_SCHEMA);
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(3, result.ValidationErrors.Count);

            string errorMessage = result.ErrorMessage;
            Assert.IsTrue(errorMessage.Contains("Total schema validation errors: 3"));
            Assert.IsTrue(errorMessage.Contains("NoAdditionalPropertiesAllowed: #/data-source-file at 7:31"));
            Assert.IsTrue(errorMessage.Contains("NoAdditionalPropertiesAllowed: #/runtime.Graphql at 13:26"));
            Assert.IsTrue(errorMessage.Contains("AdditionalPropertiesNotValid: #/entities.Publisher\n"
                    + "{\n  NoAdditionalPropertiesAllowed: #/entities.Publisher.rst\n}\n at 32:30"));
        }

        /// <summary>
        /// DAB config doesn't support additional properties in it's config. This test validates that
        /// a config file with additional properties fails the schema validation but still has no effect on engine startup.
        /// </summary>
        [TestMethod("Validates the config with custom properties works with the engine."), TestCategory(TestCategory.MSSQL)]
        public async Task TestEngineCanStartConfigWithCustomProperties()
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            string customProperty = @"
                {
                    ""description"": ""This is a custom property""
                }
            ";

            string combinedJson = TestHelper.AddPropertiesToJson(config.ToJson(), customProperty);

            Mock<ILogger<JsonConfigSchemaValidator>> schemaValidatorLogger = new();

            string jsonSchema = File.ReadAllText("dab.draft.schema.json");

            JsonConfigSchemaValidator jsonSchemaValidator = new(schemaValidatorLogger.Object, new MockFileSystem());
            JsonSchemaValidationResult result = await jsonSchemaValidator.ValidateJsonConfigWithSchemaAsync(jsonSchema, combinedJson);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.ErrorMessage.Contains("Total schema validation errors: 1"));
            Assert.IsTrue(result.ErrorMessage.Contains("NoAdditionalPropertiesAllowed: #/description"));

            File.WriteAllText(CUSTOM_CONFIG, combinedJson);
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            // Non-Hosted Scenario
            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);

                HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);
            }
        }

        /// <summary>
        /// This test checks that the GetJsonSchema method of the JsonConfigSchemaValidator class
        /// correctly downloads a JSON schema from a given URL, and that the downloaded schema matches the expected schema.
        /// </summary>
        [TestMethod]
        public async Task GetJsonSchema_DownloadsSchemaFromUrl()
        {
            // Arrange
            Mock<HttpMessageHandler> handlerMock = new(MockBehavior.Strict);
            string jsonSchemaContent = "{\"type\": \"object\", \"properties\": {\"property1\": {\"type\": \"string\"}}}";
            handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonSchemaContent, Encoding.UTF8, "application/json"),
            })
            .Verifiable();

            HttpClient mockHttpClient = new(handlerMock.Object);
            Mock<ILogger<JsonConfigSchemaValidator>> schemaValidatorLogger = new();
            JsonConfigSchemaValidator jsonConfigSchemaValidator = new(schemaValidatorLogger.Object, new MockFileSystem(), mockHttpClient);

            string url = "http://example.com/schema.json";
            RuntimeConfig runtimeConfig = new(
                Schema: url,
                DataSource: new(DatabaseType.MSSQL, "connectionString", null),
                new RuntimeEntities(new Dictionary<string, Entity>())
            );

            // Act
            string receivedJsonSchema = await jsonConfigSchemaValidator.GetJsonSchema(runtimeConfig);

            // Assert
            Assert.AreEqual(jsonSchemaContent, receivedJsonSchema);
            handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(1),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get
                && req.RequestUri == new Uri(url)),
            ItExpr.IsAny<CancellationToken>());
        }

        /// <summary>
        /// This test checks that even when the schema download fails, the GetJsonSchema method
        /// fetches the schema from the package succesfully.
        /// </summary>
        [TestMethod]
        public async Task GetJsonSchema_DownloadsSchemaFromUrlFailure()
        {
            // Arrange
            Mock<HttpMessageHandler> handlerMock = new(MockBehavior.Strict);
            handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.InternalServerError,    // Simulate a failure
                Content = new StringContent("", Encoding.UTF8, "application/json"),
            })
            .Verifiable();

            HttpClient mockHttpClient = new(handlerMock.Object);
            Mock<ILogger<JsonConfigSchemaValidator>> schemaValidatorLogger = new();
            JsonConfigSchemaValidator jsonConfigSchemaValidator = new(schemaValidatorLogger.Object, new MockFileSystem(), mockHttpClient);

            string url = "http://example.com/schema.json";
            RuntimeConfig runtimeConfig = new(
                Schema: url,
                DataSource: new(DatabaseType.MSSQL, "connectionString", null),
                new RuntimeEntities(new Dictionary<string, Entity>())
            );

            // Act
            string receivedJsonSchema = await jsonConfigSchemaValidator.GetJsonSchema(runtimeConfig);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(receivedJsonSchema));

            // Sanity check to ensure the schema is valid
            Assert.IsTrue(receivedJsonSchema.Contains("$schema"));
            Assert.IsTrue(receivedJsonSchema.Contains("data-source"));
            Assert.IsTrue(receivedJsonSchema.Contains("entities"));
        }

        /// <summary>
        /// Set the connection string to an invalid value and expect the service to be unavailable
        /// since without this env var, it would be available - guaranteeing this env variable
        /// has highest precedence irrespective of what the connection string is in the config file.
        /// Verifying the Exception thrown.
        /// </summary>
        [TestMethod($"Validates that environment variable {RUNTIME_ENV_CONNECTION_STRING} has highest precedence."), TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void TestConnectionStringEnvVarHasHighestPrecedence()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                RUNTIME_ENV_CONNECTION_STRING,
                "Invalid Connection String");

            try
            {
                TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
                _ = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
                Assert.Fail($"{RUNTIME_ENV_CONNECTION_STRING} is not given highest precedence");
            }
            catch (Exception e)
            {
                Assert.AreEqual(typeof(ArgumentException), e.GetType());
                Assert.AreEqual(
                    $"Format of the initialization string does not conform to specification starting at index 0.",
                    e.Message);
            }
        }

        /// <summary>
        /// Test to verify the precedence logic for config file based on Environment variables.
        /// </summary>
        [DataTestMethod]
        [DataRow("HostTest", "Test", false, $"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}", DisplayName = "hosting and dab environment set, without considering overrides.")]
        [DataRow("HostTest", "", false, $"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, without considering overrides.")]
        [DataRow("", "Test1", false, $"{CONFIGFILE_NAME}.Test1{CONFIG_EXTENSION}", DisplayName = "only dab environment set, without considering overrides.")]
        [DataRow("", "Test2", true, $"{CONFIGFILE_NAME}.Test2.overrides{CONFIG_EXTENSION}", DisplayName = "only dab environment set, considering overrides.")]
        [DataRow("HostTest1", "", true, $"{CONFIGFILE_NAME}.HostTest1.overrides{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, considering overrides.")]
        public void TestGetConfigFileNameForEnvironment(
            string hostingEnvironmentValue,
            string environmentValue,
            bool considerOverrides,
            string expectedRuntimeConfigFile)
        {
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(expectedRuntimeConfigFile, new MockFileData(string.Empty));
            FileSystemRuntimeConfigLoader runtimeConfigLoader = new(fileSystem);

            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, hostingEnvironmentValue);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            string actualRuntimeConfigFile = runtimeConfigLoader.GetFileNameForEnvironment(hostingEnvironmentValue, considerOverrides);
            Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        }

        /// <summary>
        /// Test different graphql endpoints in different host modes
        /// when accessed interactively via browser.
        /// </summary>
        /// <param name="endpoint">The endpoint route</param>
        /// <param name="hostMode">The mode in which the service is executing.</param>
        /// <param name="expectedStatusCode">Expected Status Code.</param>
        /// <param name="expectedContent">The expected phrase in the response body.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/graphql/", HostMode.Development, HttpStatusCode.OK, "Banana Cake Pop",
            DisplayName = "GraphQL endpoint with no query in development mode.")]
        [DataRow("/graphql", HostMode.Production, HttpStatusCode.NotFound,
            DisplayName = "GraphQL endpoint with no query in production mode.")]
        [DataRow("/graphql/ui", HostMode.Development, HttpStatusCode.NotFound,
            DisplayName = "Default BananaCakePop in development mode.")]
        [DataRow("/graphql/ui", HostMode.Production, HttpStatusCode.NotFound,
            DisplayName = "Default BananaCakePop in production mode.")]
        [DataRow("/graphql?query={book_by_pk(id: 1){title}}",
            HostMode.Development, HttpStatusCode.OK,
            DisplayName = "GraphQL endpoint with query in development mode.")]
        [DataRow("/graphql?query={book_by_pk(id: 1){title}}",
            HostMode.Production, HttpStatusCode.OK, "data",
            DisplayName = "GraphQL endpoint with query in production mode.")]
        [DataRow(RestController.REDIRECTED_ROUTE, HostMode.Development, HttpStatusCode.BadRequest,
            "GraphQL request redirected to favicon.ico.",
            DisplayName = "Redirected endpoint in development mode.")]
        [DataRow(RestController.REDIRECTED_ROUTE, HostMode.Production, HttpStatusCode.BadRequest,
            "GraphQL request redirected to favicon.ico.",
            DisplayName = "Redirected endpoint in production mode.")]
        public async Task TestInteractiveGraphQLEndpoints(
            string endpoint,
            HostMode HostMode,
            HttpStatusCode expectedStatusCode,
            string expectedContent = "")
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            RuntimeConfig configWithCustomHostMode = config with
            {
                Runtime = config.Runtime with
                {
                    Host = config.Runtime.Host with { Mode = HostMode }
                }
            };
            File.WriteAllText(CUSTOM_CONFIG, configWithCustomHostMode.ToJson());
            string[] args = new[]
            {
            $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            {
                HttpRequestMessage request = new(HttpMethod.Get, endpoint);

                // Adding the following headers simulates an interactive browser request.
                request.Headers.Add("user-agent", BROWSER_USER_AGENT_HEADER);
                request.Headers.Add("accept", BROWSER_ACCEPT_HEADER);

                HttpResponseMessage response = await client.SendAsync(request);
                Assert.AreEqual(expectedStatusCode, response.StatusCode);
                string actualBody = await response.Content.ReadAsStringAsync();
                Assert.IsTrue(actualBody.Contains(expectedContent));
            }
        }

        /// <summary>
        /// Tests that the custom path rewriting middleware properly rewrites the
        /// first segment of a path (/segment1/.../segmentN) when the segment matches
        /// the custom configured GraphQLEndpoint.
        /// Note: The GraphQL service is always internally mapped to /graphql
        /// </summary>
        /// <param name="graphQLConfiguredPath">The custom configured GraphQL path in configuration</param>
        /// <param name="requestPath">The path used in the web request executed in the test.</param>
        /// <param name="expectedStatusCode">Expected Http success/error code</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/graphql", "/gql", HttpStatusCode.BadRequest, DisplayName = "Request to non-configured graphQL endpoint is handled by REST controller.")]
        [DataRow("/graphql", "/graphql", HttpStatusCode.OK, DisplayName = "Request to configured default GraphQL endpoint succeeds, path not rewritten.")]
        [DataRow("/gql", "/gql/additionalURLsegment", HttpStatusCode.OK, DisplayName = "GraphQL request path (with extra segments) rewritten to match internally set GraphQL endpoint /graphql.")]
        [DataRow("/gql", "/gql", HttpStatusCode.OK, DisplayName = "GraphQL request path rewritten to match internally set GraphQL endpoint /graphql.")]
        [DataRow("/gql", "/api/book", HttpStatusCode.NotFound, DisplayName = "Non-GraphQL request's path is not rewritten and is handled by REST controller.")]
        [DataRow("/gql", "/graphql", HttpStatusCode.NotFound, DisplayName = "Requests to default/internally set graphQL endpoint fail when configured endpoint differs.")]
        public async Task TestPathRewriteMiddlewareForGraphQL(
            string graphQLConfiguredPath,
            string requestPath,
            HttpStatusCode expectedStatusCode)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Path: graphQLConfiguredPath);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL),
                Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, new());
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[] { $"--ConfigFileName={CUSTOM_CONFIG}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

            var payload = new { query };

            HttpRequestMessage request = new(HttpMethod.Post, requestPath)
            {
                Content = JsonContent.Create(payload)
            };

            HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            Assert.AreEqual(expectedStatusCode, response.StatusCode);
        }

        /// <summary>
        /// Validates the error message that is returned for REST requests with incorrect parameter type
        /// when the engine is running in Production mode. The error messages in Production mode is
        /// very generic to not reveal information about the underlying database objects backing the entity.
        /// This test runs against a MsSql database. However, generic error messages will be returned in Production
        /// mode when run against PostgreSql and MySql databases.
        /// </summary>
        /// <param name="requestType">Type of REST request</param>
        /// <param name="requestPath">Endpoint for the REST request</param>
        /// <param name="expectedErrorMessage">Right error message that should be shown to the end user</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(SupportedHttpVerb.Get, "/api/Book/id/one", null, "Invalid value provided for field: id", DisplayName = "Validates the error message for a GET request with incorrect primary key parameter type on a table in production mode")]
        [DataRow(SupportedHttpVerb.Get, "/api/books_view_all/id/one", null, "Invalid value provided for field: id", DisplayName = "Validates the error message for a GET request with incorrect primary key parameter type on a view in production mode")]
        [DataRow(SupportedHttpVerb.Get, "/api/GetBook?id=one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a GET request on a stored-procedure with incorrect parameter type in production mode")]
        [DataRow(SupportedHttpVerb.Get, "/api/GQLmappings/column1/one", null, "Invalid value provided for field: column1", DisplayName = "Validates the error message for a GET request with incorrect primary key parameter type with alias defined for primary key column on a table in production mode")]
        [DataRow(SupportedHttpVerb.Post, "/api/Book", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a POST request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Put, "/api/Book/id/one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a PUT request with incorrect primary key parameter type on a table in production mode")]
        [DataRow(SupportedHttpVerb.Put, "/api/Book/id/1", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a bad PUT request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Patch, "/api/Book/id/one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a PATCH request with incorrect primary key parameter type on a table in production mode")]
        [DataRow(SupportedHttpVerb.Patch, "/api/Book/id/1", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a PATCH request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Delete, "/api/Book/id/one", REQUEST_BODY_WITH_CORRECT_PARAM_TYPES, "Invalid value provided for field: id", DisplayName = "Validates the error message for a DELETE request with incorrect primary key parameter type on a table in production mode")]
        public async Task TestGenericErrorMessageForRestApiInProductionMode(
            SupportedHttpVerb requestType,
            string requestPath,
            string requestBody,
            string expectedErrorMessage)
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            TestHelper.ConstructNewConfigWithSpecifiedHostMode(CUSTOM_CONFIG, HostMode.Production, TestCategory.MSSQL);
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(requestType);
                HttpRequestMessage request;
                if (requestType is SupportedHttpVerb.Get || requestType is SupportedHttpVerb.Delete)
                {
                    request = new(httpMethod, requestPath);
                }
                else
                {
                    request = new(httpMethod, requestPath)
                    {
                        Content = JsonContent.Create(requestBody)
                    };
                }

                HttpResponseMessage response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.IsTrue(body.Contains(expectedErrorMessage));
            }
        }

        /// <summary>
        /// Validates the REST HTTP methods that are enabled for Stored Procedures when
        /// some of the default fields are absent in the config file.
        /// When methods section is not defined explicitly in the config file, only POST
        /// method should be enabled for Stored Procedures.
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(SP_CONFIG_WITH_NO_REST_SETTINGS, SupportedHttpVerb.Post, "/api/GetBooks", HttpStatusCode.Created, DisplayName = "SP - REST POST enabled when no REST section is present")]
        [DataRow(SP_CONFIG_WITH_NO_REST_SETTINGS, SupportedHttpVerb.Get, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST GET disabled when no REST section is present")]
        [DataRow(SP_CONFIG_WITH_NO_REST_SETTINGS, SupportedHttpVerb.Patch, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PATCH disabled when no REST section is present")]
        [DataRow(SP_CONFIG_WITH_NO_REST_SETTINGS, SupportedHttpVerb.Put, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PUT disabled when no REST section is present")]
        [DataRow(SP_CONFIG_WITH_NO_REST_SETTINGS, SupportedHttpVerb.Delete, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST DELETE disabled when no REST section is present")]
        [DataRow(SP_CONFIG_WITH_ONLY_PATH_IN_REST_SETTINGS, SupportedHttpVerb.Post, "/api/get_books/", HttpStatusCode.Created, DisplayName = "SP - REST POST enabled when only a custom path is defined")]
        [DataRow(SP_CONFIG_WITH_ONLY_PATH_IN_REST_SETTINGS, SupportedHttpVerb.Get, "/api/get_books/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST GET disabled when only a custom path is defined")]
        [DataRow(SP_CONFIG_WITH_ONLY_PATH_IN_REST_SETTINGS, SupportedHttpVerb.Patch, "/api/get_books/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PATCH disabled when only a custom path is defined")]
        [DataRow(SP_CONFIG_WITH_ONLY_PATH_IN_REST_SETTINGS, SupportedHttpVerb.Delete, "/api/get_books/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST DELETE disabled when only a custom path is defined")]
        [DataRow(SP_CONFIG_WITH_ONLY_PATH_IN_REST_SETTINGS, SupportedHttpVerb.Put, "/api/get_books/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PUT disabled when a custom path is defined")]
        [DataRow(SP_CONFIG_WITH_JUST_METHODS_IN_REST_SETTINGS, SupportedHttpVerb.Post, "/api/GetBooks/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST POST disabled by not specifying in the methods section")]
        [DataRow(SP_CONFIG_WITH_JUST_METHODS_IN_REST_SETTINGS, SupportedHttpVerb.Get, "/api/GetBooks/", HttpStatusCode.OK, DisplayName = "SP - REST GET enabled by specifying in the methods section")]
        [DataRow(SP_CONFIG_WITH_JUST_METHODS_IN_REST_SETTINGS, SupportedHttpVerb.Patch, "/api/GetBooks/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PATCH disabled by not specifying in the methods section")]
        [DataRow(SP_CONFIG_WITH_JUST_METHODS_IN_REST_SETTINGS, SupportedHttpVerb.Put, "/api/GetBooks/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PUT disabled by not specifying in the methods section")]
        [DataRow(SP_CONFIG_WITH_JUST_METHODS_IN_REST_SETTINGS, SupportedHttpVerb.Delete, "/api/GetBooks/", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST DELETE disabled by not specifying in the methods section")]
        [DataRow(SP_CONFIG_WITH_REST_DISABLED, SupportedHttpVerb.Get, "/api/GetBooks", HttpStatusCode.NotFound, DisplayName = "SP - REST GET disabled by configuring enabled as false")]
        [DataRow(SP_CONFIG_WITH_REST_DISABLED, SupportedHttpVerb.Post, "/api/GetBooks", HttpStatusCode.NotFound, DisplayName = "SP - REST POST disabled by configuring enabled as false")]
        [DataRow(SP_CONFIG_WITH_REST_DISABLED, SupportedHttpVerb.Patch, "/api/GetBooks", HttpStatusCode.NotFound, DisplayName = "SP - REST PATCH disabled by configuring enabled as false")]
        [DataRow(SP_CONFIG_WITH_REST_DISABLED, SupportedHttpVerb.Put, "/api/GetBooks", HttpStatusCode.NotFound, DisplayName = "SP - REST PUT disabled by configuring enabled as false")]
        [DataRow(SP_CONFIG_WITH_REST_DISABLED, SupportedHttpVerb.Delete, "/api/GetBooks", HttpStatusCode.NotFound, DisplayName = "SP - REST DELETE disabled by configuring enabled as false")]
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Get, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST GET is disabled when enabled flag is configured to true")]
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Post, "/api/GetBooks", HttpStatusCode.Created, DisplayName = "SP - REST POST is enabled when enabled flag is configured to true")]
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Patch, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PATCH is disabled when enabled flag is configured to true")]
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Put, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PUT is disabled when enabled flag is configured to true")]
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Delete, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST DELETE is disabled when enabled flag is configured to true")]
        public async Task TestSPRestDefaultsForManuallyConstructedConfigs(
           string entityJson,
           SupportedHttpVerb requestType,
           string requestPath,
           HttpStatusCode expectedResponseStatusCode)
        {
            string configJson = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, entityJson);
            RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig, logger: null, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL));
            string configFileName = "custom-config.json";
            File.WriteAllText(configFileName, deserializedConfig.ToJson());
            string[] args = new[]
            {
                    $"--ConfigFileName={configFileName}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(requestType);
                HttpRequestMessage request = new(httpMethod, requestPath);
                HttpResponseMessage response = await client.SendAsync(request);
                Assert.AreEqual(expectedResponseStatusCode, response.StatusCode);
            }
        }

        /// <summary>
        /// Validates that deserialization of config file is successful for the following scenarios:
        /// 1. Multiple Mutations section is null
        /// {
        ///     "multiple-mutations": null
        /// }
        ///
        /// 2. Multiple Mutations section is empty.
        /// {
        ///     "multiple-mutations": {}
        /// }
        ///
        /// 3. Create field within Multiple Mutation section is null.
        /// {
        ///     "multiple-mutations": {
        ///         "create": null
        ///     }
        /// }
        ///
        /// 4. Create field within Multiple Mutation section is empty.
        /// {
        ///     "multiple-mutations": {
        ///         "create": {}
        ///     }
        /// }
        ///
        /// For all the above mentioned scenarios, the expected value for MultipleMutationOptions field is null.
        /// </summary>
        /// <param name="baseConfig">Base Config Json string.</param>
        [DataTestMethod]
        [DataRow(TestHelper.BASE_CONFIG_NULL_MULTIPLE_MUTATIONS_FIELD, DisplayName = "MultipleMutationOptions field deserialized as null when multiple mutation section is null")]
        [DataRow(TestHelper.BASE_CONFIG_EMPTY_MULTIPLE_MUTATIONS_FIELD, DisplayName = "MultipleMutationOptions field deserialized as null when multiple mutation section is empty")]
        [DataRow(TestHelper.BASE_CONFIG_NULL_MULTIPLE_CREATE_FIELD, DisplayName = "MultipleMutationOptions field deserialized as null when create field within multiple mutation section is null")]
        [DataRow(TestHelper.BASE_CONFIG_EMPTY_MULTIPLE_CREATE_FIELD, DisplayName = "MultipleMutationOptions field deserialized as null when create field within multiple mutation section is empty")]
        public void ValidateDeserializationOfConfigWithNullOrEmptyInvalidMultipleMutationSection(string baseConfig)
        {
            string configJson = TestHelper.AddPropertiesToJson(baseConfig, BOOK_ENTITY_JSON);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig));
            Assert.IsNotNull(deserializedConfig.Runtime);
            Assert.IsNotNull(deserializedConfig.Runtime.GraphQL);
            Assert.IsNull(deserializedConfig.Runtime.GraphQL.MultipleMutationOptions);
        }

        /// <summary>
        /// Sanity check to validate that DAB engine starts successfully when used with a config file without the multiple
        /// mutations feature flag section.
        /// The runtime graphql section of the config file used looks like this:
        ///
        /// "graphql": {
        ///    "path": "/graphql",
        ///    "allow-introspection": true
        ///  }
        ///
        /// Without the multiple mutations feature flag section, DAB engine should be able to
        ///  1. Successfully deserialize the config file without multiple mutation section.
        ///  2. Process REST and GraphQL API requests.
        ///
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task SanityTestForRestAndGQLRequestsWithoutMultipleMutationFeatureFlagSection()
        {
            // The configuration file is constructed by merging hard-coded JSON strings to simulate the scenario where users manually edit the
            // configuration file (instead of using CLI).
            string configJson = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, BOOK_ENTITY_JSON);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig, logger: null, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL)));
            string configFileName = "custom-config.json";
            File.WriteAllText(configFileName, deserializedConfig.ToJson());
            string[] args = new[]
            {
                    $"--ConfigFileName={configFileName}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                try
                {

                    // Perform a REST GET API request to validate that REST GET API requests are executed correctly.
                    HttpRequestMessage restRequest = new(HttpMethod.Get, "api/Book");
                    HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                    Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);

                    // Perform a GraphQL API request to validate that DAB engine executes GraphQL requests successfully.
                    string query = @"{
                        book_by_pk(id: 1) {
                           id,
                           title,
                           publisher_id
                        }
                    }";

                    object payload = new { query };

                    HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                    {
                        Content = JsonContent.Create(payload)
                    };

                    HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                    Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);
                    Assert.IsNotNull(graphQLResponse.Content);
                    string body = await graphQLResponse.Content.ReadAsStringAsync();
                    Assert.IsFalse(body.Contains("errors"));
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Unexpected exception : {ex}");
                }
            }
        }

        /// <summary>
        /// Test to validate that when an entity which will return a paginated response is queried, and a custom runtime base route is configured in the runtime configuration,
        /// then the generated nextLink in the response would contain the rest base-route just before the rest path. For the subsequent query, the rest base-route will be trimmed
        /// by the upstream before the request lands at DAB.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestRuntimeBaseRouteInNextLinkForPaginatedRestResponse()
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            string runtimeBaseRoute = "/base-route";
            TestHelper.ConstructNewConfigWithSpecifiedHostMode(CUSTOM_CONFIG, HostMode.Production, TestCategory.MSSQL, runtimeBaseRoute: runtimeBaseRoute);
            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string requestPath = "/api/MappedBookmarks";
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(SupportedHttpVerb.Get);
                HttpRequestMessage request = new(httpMethod, requestPath);

                HttpResponseMessage response = await client.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                Assert.IsTrue(response.StatusCode is HttpStatusCode.OK);

                JsonElement responseElement = JsonSerializer.Deserialize<JsonElement>(responseBody);
                JsonElement responseValue = responseElement.GetProperty(SqlTestHelper.jsonResultTopLevelKey);
                string nextLink = responseElement.GetProperty("nextLink").ToString();

                // Assert that we got an array response with length equal to the maximum allowed records in a paginated response.
                Assert.AreEqual(JsonValueKind.Array, responseValue.ValueKind);
                Assert.AreEqual(100, responseValue.GetArrayLength());

                // Assert that the nextLink contains the rest base-route just before the request path.
                StringAssert.Contains(nextLink, runtimeBaseRoute + requestPath);
            }
        }

        /// <summary>
        /// Tests that the when Rest or GraphQL is disabled Globally,
        /// any requests made will get a 404 response.
        /// </summary>
        /// <param name="isRestEnabled">The custom configured REST enabled property in configuration.</param>
        /// <param name="isGraphQLEnabled">The custom configured GraphQL enabled property in configuration.</param>
        /// <param name="expectedStatusCodeForREST">Expected HTTP status code code for the Rest request</param>
        /// <param name="expectedStatusCodeForGraphQL">Expected HTTP status code code for the GraphQL request</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(true, true, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Both Rest and GraphQL endpoints enabled globally")]
        [DataRow(true, false, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest enabled and GraphQL endpoints disabled globally")]
        [DataRow(false, true, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest disabled and GraphQL endpoints enabled globally")]
        [DataRow(true, true, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Both Rest and GraphQL endpoints enabled globally")]
        [DataRow(true, false, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest enabled and GraphQL endpoints disabled globally")]
        [DataRow(false, true, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest disabled and GraphQL endpoints enabled globally")]
        public async Task TestGlobalFlagToEnableRestAndGraphQLForHostedAndNonHostedEnvironment(
            bool isRestEnabled,
            bool isGraphQLEnabled,
            HttpStatusCode expectedStatusCodeForREST,
            HttpStatusCode expectedStatusCodeForGraphQL,
            string configurationEndpoint)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: isGraphQLEnabled);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: isRestEnabled);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            // Non-Hosted Scenario
            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(expectedStatusCodeForGraphQL, graphQLResponse.StatusCode);

                HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(expectedStatusCodeForREST, restResponse.StatusCode);
            }

            // Hosted Scenario
            // Instantiate new server with no runtime config for post-startup configuration hydration tests.
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);

                HttpResponseMessage postResult =
                await client.PostAsync(configurationEndpoint, content);
                Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

                HttpStatusCode restResponseCode = await GetRestResponsePostConfigHydration(client);

                Assert.AreEqual(expected: expectedStatusCodeForREST, actual: restResponseCode);

                HttpStatusCode graphqlResponseCode = await GetGraphQLResponsePostConfigHydration(client);

                Assert.AreEqual(expected: expectedStatusCodeForGraphQL, actual: graphqlResponseCode);

            }
        }

        /// <summary>
        /// For mutation operations, both the respective operation(create/update/delete) + read permissions are needed to receive a valid response.
        /// In this test, Anonymous role is configured with only create permission.
        /// So, a create mutation executed in the context of Anonymous role is expected to result in
        /// 1) Creation of a new item in the database
        /// 2) An error response containing the error message : "The mutation operation {operation_name} was successful but the current user is unauthorized to view the response due to lack of read permissions"
        ///
        /// A create mutation operation in the context of Anonymous role is executed and the expected error message is validated.
        /// Authenticated role has read permission configured. A pk query is executed in the context of Authenticated role to validate that a new
        /// record was created in the database.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task ValidateErrorMessageForMutationWithoutReadPermission()
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            EntityAction createAction = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new());

            EntityAction readAction = new(
                Action: EntityActionOperation.Read,
                Fields: null,
                Policy: new());

            EntityAction deleteAction = new(
                Action: EntityActionOperation.Delete,
                Fields: null,
                Policy: new());

            EntityPermission[] permissions = new[] {new EntityPermission( Role: AuthorizationResolver.ROLE_ANONYMOUS , Actions: new[] { createAction }),
                       new EntityPermission( Role: AuthorizationResolver.ROLE_AUTHENTICATED , Actions: new[] { readAction, createAction, deleteAction })};

            Entity entity = new(Source: new("stocks", EntitySourceType.Table, null, null),
                                  Rest: null,
                                  GraphQL: new(Singular: "Stock", Plural: "Stocks"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Stock";
            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName);

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            string authToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken();
            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                try
                {
                    // A create mutation operation is executed in the context of Anonymous role. The Anonymous role has create action configured but lacks
                    // read action. As a result, a new record should be created in the database but the mutation operation should return an error message.
                    string graphQLMutation = @"
                            mutation {
                              createStock(
                                item: {
                                  categoryid: 5001
                                  pieceid: 5001
                                  categoryName: ""SciFi""
                                  piecesAvailable: 100
                                  piecesRequired: 50
                                }
                              ) {
                                categoryid
                                pieceid
                              }
                            }";

                    JsonElement mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: graphQLMutation,
                        queryName: "createStock",
                        variables: null,
                        clientRoleHeader: null
                        );

                    Assert.IsNotNull(mutationResponse);
                    Assert.IsTrue(mutationResponse.ToString().Contains("The mutation operation createStock was successful but the current user is unauthorized to view the response due to lack of read permissions"));

                    // pk_query is executed in the context of Authenticated role to validate that the create mutation executed in the context of Anonymous role
                    // resulted in the creation of a new record in the database.
                    string graphQLQuery = @"
                        {
                          stock_by_pk(categoryid: 5001, pieceid: 5001) {
                            categoryid
                            pieceid
                            categoryName
                          }
                        }";
                    string queryName = "stock_by_pk";

                    ValidateMutationSucceededAtDbLayer(server, client, graphQLQuery, queryName, authToken, AuthorizationResolver.ROLE_AUTHENTICATED);
                }
                finally
                {
                    // Clean-up steps. The record created by the create mutation operation is deleted to reset the database
                    // back to its original state.
                    string deleteMutation = @"
                        mutation {
                            deleteStock(categoryid: 5001, pieceid: 5001) {
                            categoryid
                            pieceid
                            }
                        }";

                    _ = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: deleteMutation,
                        queryName: "deleteStock",
                        variables: null,
                        authToken: authToken,
                        clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);
                }
            }
        }

        /// <summary>
        /// Multiple mutation operations are disabled through the configuration properties.
        ///
        /// Test to validate that when multiple-create is disabled:
        /// 1. Including a relationship field in the input for create mutation for an entity returns an exception as when multiple mutations are disabled,
        /// we don't add fields for relationships in the input type schema and hence users should not be able to do insertion in the related entities.
        ///
        /// 2. Excluding all the relationship fields i.e. performing insertion in just the top-level entity executes successfully.
        ///
        /// 3. Relationship fields are marked as optional fields in the schema when multiple create operation is enabled. However, when multiple create operations
        /// are disabled, the relationship fields should continue to be marked as required fields.
        /// With multiple create operation disabled, executing a create mutation operation without a relationship field ("publisher_id" in createbook mutation operation) should be caught by
        /// HotChocolate since it is a required field.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task ValidateMultipleCreateAndCreateMutationWhenMultipleCreateOperationIsDisabled()
        {
            // Generate a custom config file with multiple create operation disabled.
            RuntimeConfig runtimeConfig = InitialzieRuntimeConfigForMultipleCreateTests(isMultipleCreateOperationEnabled: false);

            const string CUSTOM_CONFIG = "custom-config.json";

            File.WriteAllText(CUSTOM_CONFIG, runtimeConfig.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // When multiple create operation is disabled, fields belonging to related entities are not generated for the input type objects of create operation.
                // Executing a create mutation with fields belonging to related entities should be caught by Hotchocolate as unrecognized fields.
                string pointMultipleCreateOperation = @"mutation createbook{
                                                            createbook(item: { title: ""Book #1"", publishers: { name: ""The First Publisher"" } }) {
                                                                id
                                                                title
                                                            }
                                                        }";

                JsonElement mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(client,
                                                                                                    server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                                                                    query: pointMultipleCreateOperation,
                                                                                                    queryName: "createbook",
                                                                                                    variables: null,
                                                                                                    clientRoleHeader: null);

                Assert.IsNotNull(mutationResponse);

                SqlTestHelper.TestForErrorInGraphQLResponse(mutationResponse.ToString(),
                                                            message: "The specified input object field `publishers` does not exist.",
                                                            path: @"[""createbook""]");

                // When multiple create operation is enabled, two types of create mutation operations are generated 1) Point create mutation operation 2) Many type create mutation operation.
                // When multiple create operation is disabled, only point create mutation operation is generated.
                // With multiple create operation disabled, executing a many type multiple create operation should be caught by HotChocolate as the many type mutation operation should not exist in the schema.
                string manyTypeMultipleCreateOperation = @"mutation {
                                                              createbooks(
                                                                items: [
                                                                  { title: ""Book #1"", publishers: { name: ""Publisher #1"" } }
                                                                  { title: ""Book #2"", publisher_id: 1234 }
                                                                ]
                                                              ) {
                                                                items {
                                                                  id
                                                                  title
                                                                }
                                                              }
                                                            }";

                mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(client,
                                                                                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                                                        query: manyTypeMultipleCreateOperation,
                                                                                        queryName: "createbook",
                                                                                        variables: null,
                                                                                        clientRoleHeader: null);

                Assert.IsNotNull(mutationResponse);
                SqlTestHelper.TestForErrorInGraphQLResponse(mutationResponse.ToString(),
                                                            message: "The field `createbooks` does not exist on the type `Mutation`.");

                // Sanity test to validate that executing a point create mutation with multiple create operation disabled,
                // a) Creates the new item successfully.
                // b) Returns the expected response.
                string pointCreateOperation = @"mutation createbook{
                                                            createbook(item: { title: ""Book #1"", publisher_id: 1234 }) {
                                                                title
                                                                publisher_id
                                                            }
                                                        }";

                mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(client,
                                                                                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                                                        query: pointCreateOperation,
                                                                                        queryName: "createbook",
                                                                                        variables: null,
                                                                                        clientRoleHeader: null);

                string expectedResponse = @"{ ""title"":""Book #1"",""publisher_id"":1234}";

                Assert.IsNotNull(mutationResponse);
                SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, mutationResponse.ToString());

                // When  a create multiple operation is enabled, the "publisher_id" field will be generated as an optional field in the schema. But, when multiple create operation is disabled,
                // "publisher_id" should be a required field.
                // With multiple create operation disabled, executing a createbook mutation operation without the "publisher_id" field is expected to be caught by HotChocolate
                // as the schema should be generated with "publisher_id" as a required field.
                string pointCreateOperationWithMissingFields = @"mutation createbook{
                                                                    createbook(item: { title: ""Book #1""}) {
                                                                        title
                                                                        publisher_id
                                                                    }
                                                                }";

                mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(client,
                                                                                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                                                        query: pointCreateOperationWithMissingFields,
                                                                                        queryName: "createbook",
                                                                                        variables: null,
                                                                                        clientRoleHeader: null);

                Assert.IsNotNull(mutationResponse);
                SqlTestHelper.TestForErrorInGraphQLResponse(response: mutationResponse.ToString(),
                                                            message: "`publisher_id` is a required field and cannot be null.");
            }
        }

        /// <summary>
        /// When multiple create operation is enabled, the relationship fields are generated as optional fields in the schema.
        /// However, when not providing the relationship field as well the related object in the create mutation request should result in an error from the database layer.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task ValidateCreateMutationWithMissingFieldsFailWithMultipleCreateEnabled()
        {
            // Multiple create operations are enabled.
            RuntimeConfig runtimeConfig = InitialzieRuntimeConfigForMultipleCreateTests(isMultipleCreateOperationEnabled: true);

            const string CUSTOM_CONFIG = "custom-config.json";

            File.WriteAllText(CUSTOM_CONFIG, runtimeConfig.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {

                // When  a create multiple operation is enabled, the "publisher_id" field will generated as an optional field in the schema. But, when multiple create operation is disabled,
                // "publisher_id" should be a required field.
                // With multiple create operation disabled, executing a createbook mutation operation without the "publisher_id" field is expected to be caught by HotChocolate
                // as the schema should be generated with "publisher_id" as a required field.
                string pointCreateOperationWithMissingFields = @"mutation createbook{
                                                                    createbook(item: { title: ""Book #1""}) {
                                                                        title
                                                                        publisher_id
                                                                    }
                                                                }";

                JsonElement mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(client,
                                                                                                    server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                                                                    query: pointCreateOperationWithMissingFields,
                                                                                                    queryName: "createbook",
                                                                                                    variables: null,
                                                                                                    clientRoleHeader: null);

                Assert.IsNotNull(mutationResponse);
                SqlTestHelper.TestForErrorInGraphQLResponse(response: mutationResponse.ToString(),
                                                            message: "Missing value for required column: publisher_id for entity: Book at level: 1.");
            }
        }

        /// <summary>
        /// For mutation operations, the respective mutation operation type(create/update/delete) + read permissions are needed to receive a valid response.
        /// For graphQL requests, if read permission is configured for Anonymous role, then it is inherited by other roles.
        /// In this test, Anonymous role has read permission configured. Authenticated role has only create permission configured.
        /// A create mutation operation is executed in the context of Authenticated role and the response is expected to have no errors because
        /// the read permission is inherited from Anonymous role.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task ValidateInheritanceOfReadPermissionFromAnonymous()
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            EntityAction createAction = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new());

            EntityAction readAction = new(
                Action: EntityActionOperation.Read,
                Fields: null,
                Policy: new());

            EntityAction deleteAction = new(
                Action: EntityActionOperation.Delete,
                Fields: null,
                Policy: new());

            EntityPermission[] permissions = new[] {new EntityPermission( Role: AuthorizationResolver.ROLE_ANONYMOUS , Actions: new[] { createAction, readAction, deleteAction }),
                       new EntityPermission( Role: AuthorizationResolver.ROLE_AUTHENTICATED , Actions: new[] { createAction })};

            Entity entity = new(Source: new("stocks", EntitySourceType.Table, null, null),
                                  Rest: null,
                                  GraphQL: new(Singular: "Stock", Plural: "Stocks"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Stock";
            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName);

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                try
                {
                    // A create mutation operation is executed in the context of Authenticated role and the response is expected to be a valid
                    // response without any errors.
                    string graphQLMutation = @"
                        mutation {
                          createStock(
                            item: {
                              categoryid: 5001
                              pieceid: 5001
                              categoryName: ""SciFi""
                              piecesAvailable: 100
                              piecesRequired: 50
                            }
                          ) {
                            categoryid
                            pieceid
                          }
                        }";

                    JsonElement mutationResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: graphQLMutation,
                        queryName: "createStock",
                        variables: null,
                        authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(),
                        clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED
                        );

                    Assert.IsNotNull(mutationResponse);
                    Assert.IsFalse(mutationResponse.TryGetProperty("errors", out _));
                }
                finally
                {
                    // Clean-up steps. The record created by the create mutation operation is deleted to reset the database
                    // back to its original state.
                    string deleteMutation = @"
                        mutation {
                            deleteStock(categoryid: 5001, pieceid: 5001) {
                            categoryid
                            pieceid
                            }
                        }";

                    _ = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                        client,
                        server.Services.GetRequiredService<RuntimeConfigProvider>(),
                        query: deleteMutation,
                        queryName: "deleteStock",
                        variables: null,
                        clientRoleHeader: null);
                }
            }
        }

        /// <summary>
        /// Helper method to validate that the mutation operation succeded at the database layer by executing a graphQL pk query.
        /// </summary>
        /// <param name="server">Test server created for the test</param>
        /// <param name="client">HTTP client</param>
        /// <param name="query">GraphQL query/mutation text</param>
        /// <param name="queryName">GraphQL query/mutation name</param>
        /// <param name="authToken">Auth token for the graphQL request</param>
        private static async void ValidateMutationSucceededAtDbLayer(TestServer server, HttpClient client, string query, string queryName, string authToken, string clientRoleHeader)
        {
            JsonElement queryResponse = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                                                client,
                                                server.Services.GetRequiredService<RuntimeConfigProvider>(),
                                                query: query,
                                                queryName: queryName,
                                                variables: null,
                                                authToken: authToken,
                                                clientRoleHeader: clientRoleHeader);

            Assert.IsNotNull(queryResponse);
            Assert.IsFalse(queryResponse.TryGetProperty("errors", out _));
        }

        /// <summary>
        /// Validates the Location header field returned for a POST request when a 201 response is returned. The idea behind returning
        /// a Location header is to provide a URL against which a GET request can be performed to fetch the details of the new item.
        /// Base Route is not configured in the config file used for this test. If base-route is configured, the Location header URL should contain the base-route.
        /// This test performs a POST request, and in the event that it results in a 201 response, it performs a subsequent GET request
        /// with the Location header to validate the correctness of the URL.
        /// </summary>
        /// <param name="entityType">Type of the entity</param>
        /// <param name="requestPath">Request path for performing POST API requests on the entity</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(EntitySourceType.Table, "/api/Book", DisplayName = "Location Header validation - Table, Base Route not configured")]
        [DataRow(EntitySourceType.StoredProcedure, "/api/GetBooks", DisplayName = "Location Header validation - Stored Procedures, Base Route not configured")]
        public async Task ValidateLocationHeaderFieldForPostRequests(EntitySourceType entityType, string requestPath)
        {

            GraphQLRuntimeOptions graphqlOptions = new(Enabled: false);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: true);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration;

            if (entityType is EntitySourceType.StoredProcedure)
            {
                Entity entity = new(Source: new("get_books", EntitySourceType.StoredProcedure, null, null),
                              Rest: new(new SupportedHttpVerb[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }),
                              GraphQL: null,
                              Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                              Relationships: null,
                              Mappings: null
                             );

                string entityName = "GetBooks";
                configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName);
            }
            else
            {
                configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions);
            }

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(SupportedHttpVerb.Post);
                HttpRequestMessage request = new(httpMethod, requestPath);
                if (entityType is not EntitySourceType.StoredProcedure)
                {
                    string requestBody = @"{
                        ""title"": ""Harry Potter and the Order of Phoenix"",
                        ""publisher_id"": 1234
                    }";

                    JsonElement requestBodyElement = JsonDocument.Parse(requestBody).RootElement.Clone();
                    request = new(httpMethod, requestPath)
                    {
                        Content = JsonContent.Create(requestBodyElement)
                    };
                }

                HttpResponseMessage response = await client.SendAsync(request);

                // Location header field is expected only when POST request results in the creation of a new item
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

                string locationHeader = response.Headers.Location.AbsoluteUri;

                // GET request performed using the Location header should be successful.
                HttpRequestMessage followUpRequest = new(HttpMethod.Get, response.Headers.Location);
                HttpResponseMessage followUpResponse = await client.SendAsync(followUpRequest);
                Assert.AreEqual(HttpStatusCode.OK, followUpResponse.StatusCode);

                // Delete the new record created as part of this test
                if (entityType is EntitySourceType.Table)
                {
                    HttpRequestMessage cleanupRequest = new(HttpMethod.Delete, locationHeader);
                    await client.SendAsync(cleanupRequest);
                }
            }
        }

        /// <summary>
        /// Validates the Location header field returned for a POST request when it results in a 201 response. The idea behind returning
        /// a Location header is to provide a URL against which a GET request can be performed to fetch the details of the new item.
        /// Base Route is configured in the config file used for this test. So, it is expected that the Location header returned will contain the base-route.
        /// This test performs a POST request, and checks if it results in a 201 response. If so, the test validates the correctness of the Location header in two steps.
        /// Since base-route has significance only in the SWA-DAB integrated scenario and this test is executed against DAB running independently,
        /// a subsequent GET request against the Location header will result in an error. So, the correctness of the base-route returned is validated with the help of
        /// an expected location header value. The correctness of the PK part of the Location string is validated by performing a GET request after stripping off
        /// the base-route from the Location URL.
        /// </summary>
        /// <param name="entityType">Type of the entity</param>
        /// <param name="requestPath">Request path for performing POST API requests on the entity</param>
        /// <param name="baseRoute">Configured base route</param>
        /// <param name="expectedLocationHeader">Expected value for Location field in the response header. Since, the PK of the new record is not known beforehand,
        /// the expectedLocationHeader excludes the PK. Because of this, the actual location header is validated by checking if it starts with the expectedLocationHeader.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(EntitySourceType.Table, "/api/Book", "/data-api", "http://localhost/data-api/api/Book/id/", DisplayName = "Location Header validation - Table, Base Route configured")]
        [DataRow(EntitySourceType.StoredProcedure, "/api/GetBooks", "/data-api", "http://localhost/data-api/api/GetBooks", DisplayName = "Location Header validation - Stored Procedure, Base Route configured")]
        public async Task ValidateLocationHeaderWhenBaseRouteIsConfigured(
            EntitySourceType entityType,
            string requestPath,
            string baseRoute,
            string expectedLocationHeader)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: false);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: true);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration;

            if (entityType is EntitySourceType.StoredProcedure)
            {
                Entity entity = new(Source: new("get_books", EntitySourceType.StoredProcedure, null, null),
                              Rest: new(new SupportedHttpVerb[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }),
                              GraphQL: null,
                              Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                              Relationships: null,
                              Mappings: null
                             );

                string entityName = "GetBooks";
                configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, entityName);
            }
            else
            {
                configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions);
            }

            const string CUSTOM_CONFIG = "custom-config.json";

            AuthenticationOptions AuthenticationOptions = new(Provider: EasyAuthType.StaticWebApps.ToString(), null);
            HostOptions staticWebAppsHostOptions = new(null, AuthenticationOptions);

            RuntimeOptions runtimeOptions = configuration.Runtime;
            RuntimeOptions baseRouteEnabledRuntimeOptions = new(runtimeOptions?.Rest, runtimeOptions?.GraphQL, staticWebAppsHostOptions, "/data-api");
            RuntimeConfig baseRouteEnabledConfig = configuration with { Runtime = baseRouteEnabledRuntimeOptions };
            File.WriteAllText(CUSTOM_CONFIG, baseRouteEnabledConfig.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(SupportedHttpVerb.Post);
                HttpRequestMessage request = new(httpMethod, requestPath);
                if (entityType is not EntitySourceType.StoredProcedure)
                {
                    string requestBody = @"{
                        ""title"": ""Harry Potter and the Order of Phoenix"",
                        ""publisher_id"": 1234
                    }";

                    JsonElement requestBodyElement = JsonDocument.Parse(requestBody).RootElement.Clone();
                    request = new(httpMethod, requestPath)
                    {
                        Content = JsonContent.Create(requestBodyElement)
                    };
                }

                HttpResponseMessage response = await client.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

                string locationHeader = response.Headers.Location.AbsoluteUri;
                Assert.IsTrue(locationHeader.StartsWith(expectedLocationHeader));

                // The URL to perform the GET request is constructed by skipping the base-route.
                // Base Route field is applicable only in SWA-DAB integrated scenario. When DAB engine is run independently, all the
                // APIs are hosted on /api. But, the returned Location header in this test will contain the configured base-route. So, this needs to be
                // removed before performing a subsequent GET request.
                string path = response.Headers.Location.AbsolutePath;
                string completeUrl = path.Substring(baseRoute.Length);

                HttpRequestMessage followUpRequest = new(HttpMethod.Get, completeUrl);
                HttpResponseMessage followUpResponse = await client.SendAsync(followUpRequest);
                Assert.AreEqual(HttpStatusCode.OK, followUpResponse.StatusCode);

                // Delete the new record created as part of this test
                if (entityType is EntitySourceType.Table)
                {
                    HttpRequestMessage cleanupRequest = new(HttpMethod.Delete, completeUrl);
                    await client.SendAsync(cleanupRequest);
                }

            }
        }

        /// <summary>
        /// Test to validate that when the property rest.request-body-strict is absent from the rest runtime section in config file, DAB runs in strict mode.
        /// In strict mode, presence of extra fields in the request body is not permitted and leads to HTTP 400 - BadRequest error.
        /// </summary>
        /// <param name="includeExtraneousFieldInRequestBody">Boolean value indicating whether or not to include extraneous field in request body.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(false, DisplayName = "Mutation operation passes when no extraneous field is included in request body and rest.request-body-strict is omitted from the rest runtime section in the config file.")]
        [DataRow(true, DisplayName = "Mutation operation fails when an extraneous field is included in request body and rest.request-body-strict is omitted from the rest runtime section in the config file.")]
        public async Task ValidateStrictModeAsDefaultForRestRequestBody(bool includeExtraneousFieldInRequestBody)
        {
            string entityJson = @"
            {
                ""entities"": {
                    ""Book"": {
                        ""source"": {
                        ""object"": ""books"",
                        ""type"": ""table""
                        },
                        ""permissions"": [
                        {
                            ""role"": ""anonymous"",
                            ""actions"": [
                            {
                                ""action"": ""*""
                            }
                            ]
                        }
                        ]
                    }
                }
            }";

            // The BASE_CONFIG omits the rest.request-body-strict option in the runtime section.
            string configJson = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, entityJson);
            RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig, logger: null, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL));
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, deserializedConfig.ToJson());
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpMethod httpMethod = SqlTestHelper.ConvertRestMethodToHttpMethod(SupportedHttpVerb.Post);
                string requestBody = @"{
                        ""title"": ""Harry Potter and the Order of Phoenix"",
                        ""publisher_id"": 1234";

                if (includeExtraneousFieldInRequestBody)
                {
                    requestBody += @",
                    ""extraField"": 12";
                }

                requestBody += "}";
                JsonElement requestBodyElement = JsonDocument.Parse(requestBody).RootElement.Clone();
                HttpRequestMessage request = new(httpMethod, "api/Book")
                {
                    Content = JsonContent.Create(requestBodyElement)
                };

                HttpResponseMessage response = await client.SendAsync(request);
                if (includeExtraneousFieldInRequestBody)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    // Assert that including an extraneous field in request body while operating in strict mode leads to a bad request exception.
                    Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
                    Assert.IsTrue(responseBody.Contains("Invalid request body. Contained unexpected fields in body: extraField"));
                }
                else
                {
                    // When no extraneous fields are included in request body, the operation executes successfully.
                    Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
                    string locationHeader = response.Headers.Location.AbsoluteUri;

                    // Delete the new record created as part of this test.
                    HttpRequestMessage cleanupRequest = new(HttpMethod.Delete, locationHeader);
                    await client.SendAsync(cleanupRequest);
                }
            }
        }

        /// <summary>
        /// Engine supports config with some views that do not have keyfields specified in the config for MsSQL.
        /// This Test validates that support. It creates a custom config with a view and no keyfields specified.
        /// It checks both Rest and GraphQL queries are tested to return Success.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestEngineSupportViewsWithoutKeyFieldsInConfigForMsSQL()
        {
            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);
            Entity viewEntity = new(
                Source: new("books_view_all", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("", ""),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
            );

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new(), viewEntity, "books_view_all");

            const string CUSTOM_CONFIG = "custom-config.json";

            File.WriteAllText(
                CUSTOM_CONFIG,
                configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    books_view_alls {
                        items{
                            id
                            title
                        }
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);
                string body = await graphQLResponse.Content.ReadAsStringAsync();
                Assert.IsFalse(body.Contains("errors")); // In GraphQL, All errors end up in the errors array, no matter what kind of error they are.

                HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/books_view_all");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);
            }
        }

        /// <summary>
        /// In CosmosDB NoSQL, we store data in the form of JSON. Practically, JSON can be very complex.
        /// But DAB doesn't support JSON with circular references e.g if 'Character.Moon' is a valid JSON Path, then
        /// 'Moon.Character' should not be there, DAB would throw an exception during the load itself.
        /// </summary>
        /// <exception cref="ApplicationException"></exception>
        [TestMethod, TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(GRAPHQL_SCHEMA_WITH_CYCLE_OBJECT, DisplayName = "When Circular Reference is there with Object type (i.e. 'Moon' in 'Character' Entity")]
        [DataRow(GRAPHQL_SCHEMA_WITH_CYCLE_ARRAY, DisplayName = "When Circular Reference is there with Array type (i.e. '[Moon]' in 'Character' Entity")]
        public void ValidateGraphQLSchemaForCircularReference(string schema)
        {
            // Read the base config from the file system
            TestHelper.SetupDatabaseEnvironment(TestCategory.COSMOSDBNOSQL);
            FileSystemRuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
            if (!baseLoader.TryLoadKnownConfig(out RuntimeConfig baseConfig))
            {
                throw new ApplicationException("Failed to load the default CosmosDB_NoSQL config and cannot continue with tests.");
            }

            // Setup a mock file system, and use that one with the loader/provider for the config
            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
            {
                { @"../schema.gql", new MockFileData(schema) },
                { DEFAULT_CONFIG_FILE_NAME, new MockFileData(baseConfig.ToJson()) }
            });
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);

            DataApiBuilderException exception =
                Assert.ThrowsException<DataApiBuilderException>(() => new CosmosSqlMetadataProvider(provider, fileSystem));
            Assert.AreEqual("Circular reference detected in the provided GraphQL schema for entity 'Character'.", exception.Message);
            Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// GraphQL Schema types defined -> Character and Planet
        /// DAB runtime config entities defined -> Planet(Not defined: Character)
        /// Mismatch of entities and types between provided GraphQL schema file and DAB config results in actionable error message.
        /// </summary>
        /// <exception cref="ApplicationException"></exception>
        [TestMethod, TestCategory(TestCategory.COSMOSDBNOSQL)]
        public void ValidateGraphQLSchemaEntityPresentInConfig()
        {
            string GRAPHQL_SCHEMA = @"
type Character {
    id : ID,
    name : String,
}

type Planet @model(name:""PlanetAlias"") {
    id : ID!,
    name : String,
    characters : [Character]
}";
            // Read the base config from the file system
            TestHelper.SetupDatabaseEnvironment(TestCategory.COSMOSDBNOSQL);
            FileSystemRuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
            if (!baseLoader.TryLoadKnownConfig(out RuntimeConfig baseConfig))
            {
                throw new ApplicationException("Failed to load the default CosmosDB_NoSQL config and cannot continue with tests.");
            }

            Dictionary<string, Entity> entities = new(baseConfig.Entities);
            entities.Remove("Character");

            RuntimeConfig runtimeConfig = new(Schema: baseConfig.Schema,
                                             DataSource: baseConfig.DataSource,
                                             Runtime: baseConfig.Runtime,
                                             Entities: new(entities));

            // Setup a mock file system, and use that one with the loader/provider for the config
            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
            {
                { @"../schema.gql", new MockFileData(GRAPHQL_SCHEMA) },
                { DEFAULT_CONFIG_FILE_NAME, new MockFileData(runtimeConfig.ToJson()) }
            });
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);

            DataApiBuilderException exception =
                Assert.ThrowsException<DataApiBuilderException>(() => new CosmosSqlMetadataProvider(provider, fileSystem));
            Assert.AreEqual("The entity 'Character' was not found in the runtime config.", exception.Message);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, exception.SubStatusCode);
        }

        /// <summary>
        /// Tests that Startup.cs properly handles EasyAuth authentication configuration.
        /// AppService as Identity Provider while in Production mode will result in startup error.
        /// An Azure AppService environment has environment variables on the host which indicate
        /// the environment is, in fact, an AppService environment.
        /// </summary>
        /// <param name="hostMode">HostMode in Runtime config - Development or Production.</param>
        /// <param name="authType">EasyAuth auth type - AppService or StaticWebApps.</param>
        /// <param name="setEnvVars">Whether to set the AppService host environment variables.</param>
        /// <param name="expectError">Whether an error is expected.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(HostMode.Development, EasyAuthType.AppService, false, false, DisplayName = "AppService Dev - No EnvVars - No Error")]
        [DataRow(HostMode.Development, EasyAuthType.AppService, true, false, DisplayName = "AppService Dev - EnvVars - No Error")]
        [DataRow(HostMode.Production, EasyAuthType.AppService, false, true, DisplayName = "AppService Prod - No EnvVars - Error")]
        [DataRow(HostMode.Production, EasyAuthType.AppService, true, false, DisplayName = "AppService Prod - EnvVars - Error")]
        [DataRow(HostMode.Development, EasyAuthType.StaticWebApps, false, false, DisplayName = "SWA Dev - No EnvVars - No Error")]
        [DataRow(HostMode.Development, EasyAuthType.StaticWebApps, true, false, DisplayName = "SWA Dev - EnvVars - No Error")]
        [DataRow(HostMode.Production, EasyAuthType.StaticWebApps, false, false, DisplayName = "SWA Prod - No EnvVars - No Error")]
        [DataRow(HostMode.Production, EasyAuthType.StaticWebApps, true, false, DisplayName = "SWA Prod - EnvVars - No Error")]
        public void TestProductionModeAppServiceEnvironmentCheck(HostMode hostMode, EasyAuthType authType, bool setEnvVars, bool expectError)
        {
            // Clears or sets App Service Environment Variables based on test input.
            Environment.SetEnvironmentVariable(AppServiceAuthenticationInfo.APPSERVICESAUTH_ENABLED_ENVVAR, setEnvVars ? "true" : null);
            Environment.SetEnvironmentVariable(AppServiceAuthenticationInfo.APPSERVICESAUTH_IDENTITYPROVIDER_ENVVAR, setEnvVars ? "AzureActiveDirectory" : null);
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);

            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);

            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(loader);
            RuntimeConfig config = configProvider.GetConfig();

            // Setup configuration
            AuthenticationOptions AuthenticationOptions = new(Provider: authType.ToString(), null);
            RuntimeOptions runtimeOptions = new(
                Rest: new(),
                GraphQL: new(),
                Host: new(null, AuthenticationOptions, hostMode)
            );
            RuntimeConfig configWithCustomHostMode = config with { Runtime = runtimeOptions };

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configWithCustomHostMode.ToJson());
            string[] args = new[]
            {
            $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            // This test only checks for startup errors, so no requests are sent to the test server.
            try
            {
                using TestServer server = new(Program.CreateWebHostBuilder(args));
                Assert.IsFalse(expectError, message: "Expected error faulting AppService config in production mode.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(expectError, message: ex.Message);
                Assert.AreEqual(AppServiceAuthenticationInfo.APPSERVICE_PROD_MISSING_ENV_CONFIG, ex.Message);
            }
        }

        /// <summary>
        /// Integration test that validates schema introspection requests fail
        /// when allow-introspection is false in the runtime configuration.
        /// TestCategory is required for CI/CD pipeline to inject a connection string.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow(false, true, "Introspection is not allowed for the current request.", CONFIGURATION_ENDPOINT, DisplayName = "Disabled introspection returns GraphQL error.")]
        [DataRow(true, false, null, CONFIGURATION_ENDPOINT, DisplayName = "Enabled introspection does not return introspection forbidden error.")]
        [DataRow(false, true, "Introspection is not allowed for the current request.", CONFIGURATION_ENDPOINT_V2, DisplayName = "Disabled introspection returns GraphQL error.")]
        [DataRow(true, false, null, CONFIGURATION_ENDPOINT_V2, DisplayName = "Enabled introspection does not return introspection forbidden error.")]
        public async Task TestSchemaIntrospectionQuery(bool enableIntrospection, bool expectError, string errorMessage, string configurationEndpoint)
        {
            GraphQLRuntimeOptions graphqlOptions = new(AllowIntrospection: enableIntrospection);
            RestRuntimeOptions restRuntimeOptions = new();

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
            $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
            }

            // Instantiate new server with no runtime config for post-startup configuration hydration tests.
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);
                HttpStatusCode responseCode = await HydratePostStartupConfiguration(client, content, configurationEndpoint);

                Assert.AreEqual(expected: HttpStatusCode.OK, actual: responseCode, message: "Configuration hydration failed.");

                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
            }
        }

        /// <summary>
        /// Indirectly tests IsGraphQLReservedName(). Runtime config provided to engine which will
        /// trigger SqlMetadataProvider PopulateSourceDefinitionAsync() to pull column metadata from
        /// the table "graphql_incompatible." That table contains columns which collide with reserved GraphQL
        /// introspection field names which begin with double underscore (__).
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow(true, true, "__typeName", "__introspectionField", true, DisplayName = "Name violation, fails since no proper mapping set.")]
        [DataRow(true, true, "__typeName", "columnMapping", false, DisplayName = "Name violation, but OK since proper mapping set.")]
        [DataRow(false, true, null, null, false, DisplayName = "Name violation, but OK since GraphQL globally disabled.")]
        [DataRow(true, false, null, null, false, DisplayName = "Name violation, but OK since GraphQL disabled for entity.")]
        public void TestInvalidDatabaseColumnNameHandling(
            bool globalGraphQLEnabled,
            bool entityGraphQLEnabled,
            string columnName,
            string columnMapping,
            bool expectError)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: globalGraphQLEnabled);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: true);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            // Configure Entity for testing
            Dictionary<string, string> mappings = new()
        {
            { "__introspectionName", "conformingIntrospectionName" }
        };

            if (!string.IsNullOrWhiteSpace(columnMapping))
            {
                mappings.Add(columnName, columnMapping);
            }

            Entity entity = new(
                Source: new("graphql_incompatible", EntitySourceType.Table, null, null),
                Rest: new(Enabled: false),
                GraphQL: new("graphql_incompatible", "graphql_incompatibles", entityGraphQLEnabled),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: mappings
            );

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, entity, "graphqlNameCompat");
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
            $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            try
            {
                using TestServer server = new(Program.CreateWebHostBuilder(args));
                Assert.IsFalse(expectError, message: "Expected startup to fail.");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(expectError, message: "Startup was not expected to fail. " + ex.Message);
            }
        }

        /// <summary>
        /// Test different Swagger endpoints in different host modes when accessed interactively via browser.
        /// Two pass request scheme:
        /// 1 - Send get request to expected Swagger endpoint /swagger
        /// Response - Internally Swagger sends HTTP 301 Moved Permanently with Location header
        /// pointing to exact Swagger page (/swagger/index.html)
        /// 2 - Send GET request to path referred to by Location header in previous response
        /// Response - Successful loading of SwaggerUI HTML, with reference to endpoint used
        /// to retrieve OpenAPI document. This test ensures that Swagger components load, but
        /// does not confirm that a proper OpenAPI document was created.
        /// </summary>
        /// <param name="customRestPath">The custom REST route</param>
        /// <param name="hostModeType">The mode in which the service is executing.</param>
        /// <param name="expectsError">Whether to expect an error.</param>
        /// <param name="expectedStatusCode">Expected Status Code.</param>
        /// <param name="expectedOpenApiTargetContent">Snippet of expected HTML to be emitted from successful page load.
        /// This should note the openapi route that Swagger will use to retrieve the OpenAPI document.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/api", HostMode.Development, false, HttpStatusCode.OK, "{\"urls\":[{\"url\":\"/api/openapi\"", DisplayName = "SwaggerUI enabled in development mode.")]
        [DataRow("/custompath", HostMode.Development, false, HttpStatusCode.OK, "{\"urls\":[{\"url\":\"/custompath/openapi\"", DisplayName = "SwaggerUI enabled with custom REST path in development mode.")]
        [DataRow("/api", HostMode.Production, true, HttpStatusCode.BadRequest, "", DisplayName = "SwaggerUI disabled in production mode.")]
        [DataRow("/custompath", HostMode.Production, true, HttpStatusCode.BadRequest, "", DisplayName = "SwaggerUI disabled in production mode with custom REST path.")]
        public async Task OpenApi_InteractiveSwaggerUI(
            string customRestPath,
            HostMode hostModeType,
            bool expectsError,
            HttpStatusCode expectedStatusCode,
            string expectedOpenApiTargetContent)
        {
            string swaggerEndpoint = "/swagger";
            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(
                dataSource: dataSource,
                graphqlOptions: new(),
                restOptions: new(Path: customRestPath));

            configuration = configuration
                with
            {
                Runtime = configuration.Runtime
                with
                {
                    Host = configuration.Runtime?.Host
                with
                    { Mode = hostModeType }
                }
            };
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(
                CUSTOM_CONFIG,
                configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpRequestMessage initialRequest = new(HttpMethod.Get, swaggerEndpoint);

                // Adding the following headers simulates an interactive browser request.
                initialRequest.Headers.Add("user-agent", BROWSER_USER_AGENT_HEADER);
                initialRequest.Headers.Add("accept", BROWSER_ACCEPT_HEADER);

                HttpResponseMessage response = await client.SendAsync(initialRequest);
                if (expectsError)
                {
                    // Redirect(HTTP 301) and follow up request to the returned path
                    // do not occur in a failure scenario. Only HTTP 400 (Bad Request)
                    // is expected.
                    Assert.AreEqual(expectedStatusCode, response.StatusCode);
                }
                else
                {
                    // Swagger endpoint internally configured to reroute from /swagger to /swagger/index.html
                    Assert.AreEqual(HttpStatusCode.MovedPermanently, response.StatusCode);

                    HttpRequestMessage followUpRequest = new(HttpMethod.Get, response.Headers.Location);
                    HttpResponseMessage followUpResponse = await client.SendAsync(followUpRequest);
                    Assert.AreEqual(expectedStatusCode, followUpResponse.StatusCode);

                    // Validate that Swagger requests OpenAPI document using REST path defined in runtime config.
                    string actualBody = await followUpResponse.Content.ReadAsStringAsync();
                    Assert.AreEqual(true, actualBody.Contains(expectedOpenApiTargetContent));
                }
            }
        }

        /// <summary>
        /// Test different loglevel values that are avaliable by deserializing RuntimeConfig with specified LogLevel
        /// and checks if value exists properly inside the deserialized RuntimeConfig.
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(LogLevel.Trace, DisplayName = "Validates that log level Trace deserialized correctly")]
        [DataRow(LogLevel.Debug, DisplayName = "Validates log level Debug deserialized correctly")]
        [DataRow(LogLevel.Information, DisplayName = "Validates log level Information deserialized correctly")]
        [DataRow(LogLevel.Warning, DisplayName = "Validates log level Warning deserialized correctly")]
        [DataRow(LogLevel.Error, DisplayName = "Validates log level Error deserialized correctly")]
        [DataRow(LogLevel.Critical, DisplayName = "Validates log level Critical deserialized correctly")]
        [DataRow(LogLevel.None, DisplayName = "Validates log level None deserialized correctly")]
        [DataRow(null, DisplayName = "Validates log level Null deserialized correctly")]
        public void TestExistingLogLevels(LogLevel expectedLevel)
        {
            RuntimeConfig configWithCustomLogLevel = InitializeRuntimeWithLogLevel(expectedLevel);

            string configWithCustomLogLevelJson = configWithCustomLogLevel.ToJson();
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(configWithCustomLogLevelJson, out RuntimeConfig deserializedRuntimeConfig));

            Assert.AreEqual(expectedLevel, deserializedRuntimeConfig.Runtime.LoggerLevel.Value);
        }

        /// <summary>
        /// Test different loglevel values that do not exist to ensure that the build fails when they are trying to be set up
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(-1, DisplayName = "Validates that a negative log level value, fails to build")]
        [DataRow(7, DisplayName = "Validates that a positive log level value that does not exist, fails to build")]
        [DataRow(12, DisplayName = "Validates that a bigger positive log level value that does not exist, fails to build")]
        public void TestNonExistingLogLevels(LogLevel expectedLevel)
        {
            RuntimeConfig configWithCustomLogLevel = InitializeRuntimeWithLogLevel(expectedLevel);

            // Try should fail and go to catch exception
            try
            {
                string configWithCustomLogLevelJson = configWithCustomLogLevel.ToJson();
                Assert.Fail();
            }
            // Catch verifies that the exception is due to LogLevel having a value that does not exist
            catch (Exception ex)
            {
                Assert.AreEqual(typeof(KeyNotFoundException), ex.GetType());
            }
        }

        /// <summary>
        /// Tests different loglevel values to see if they are serialized correctly to the Json config
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(LogLevel.Debug)]
        [DataRow(LogLevel.Warning)]
        [DataRow(LogLevel.None)]
        [DataRow(null)]
        public void LogLevelSerialization(LogLevel expectedLevel)
        {
            RuntimeConfig configWithCustomLogLevel = InitializeRuntimeWithLogLevel(expectedLevel);
            string configWithCustomLogLevelJson = configWithCustomLogLevel.ToJson();
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(configWithCustomLogLevelJson, out RuntimeConfig deserializedRuntimeConfig));

            string serializedConfig = deserializedRuntimeConfig.ToJson();

            using (JsonDocument parsedDocument = JsonDocument.Parse(serializedConfig))
            {
                JsonElement root = parsedDocument.RootElement;

                //Validate log-level property exists in runtime
                JsonElement runtimeElement = root.GetProperty("runtime");
                bool logLevelPropertyExists = runtimeElement.TryGetProperty("log-level", out JsonElement logLevelElement);
                Assert.AreEqual(expected: true, actual: logLevelPropertyExists);

                //Validate level property inside log-level is of expected value
                bool levelPropertyExists = logLevelElement.TryGetProperty("level", out JsonElement levelElement);
                Assert.AreEqual(expected: true, actual: levelPropertyExists);
                Assert.AreEqual(expectedLevel.ToString().ToLower(), levelElement.GetString());
            }
        }

        /// <summary>
        /// Helper method to create RuntimeConfig with specificed LogLevel value
        /// </summary>
        private static RuntimeConfig InitializeRuntimeWithLogLevel(LogLevel? expectedLevel)
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);

            FileSystemRuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
            baseLoader.TryLoadKnownConfig(out RuntimeConfig baseConfig);

            LogLevelOptions logLevelOptions = new(Value: expectedLevel);
            RuntimeConfig config = new(
                Schema: baseConfig.Schema,
                DataSource: baseConfig.DataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Host: new(null, null),
                    LoggerLevel: logLevelOptions
                ),
                Entities: baseConfig.Entities
            );

            return config;
        }

        /// <summary>
        /// Validates the OpenAPI documentor behavior when enabling and disabling the global REST endpoint
        /// for the DAB engine.
        /// Global REST enabled:
        /// - GET to /openapi returns the created OpenAPI document and succeeds with 200 OK.
        /// Global REST disabled:
        /// - GET to /openapi fails with 404 Not Found.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, false, DisplayName = "Global REST endpoint enabled - successful OpenAPI doc retrieval")]
        [DataRow(false, true, DisplayName = "Global REST endpoint disabled - OpenAPI doc does not exist - HTTP404 NotFound.")]
        [TestCategory(TestCategory.MSSQL)]
        public async Task OpenApi_GlobalEntityRestPath(bool globalRestEnabled, bool expectsError)
        {
            // At least one entity is required in the runtime config for the engine to start.
            // Even though this entity is not under test, it must be supplied to the config
            // file creation function.
            Entity requiredEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(Enabled: false),
                GraphQL: new("book", "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
        {
            { "Book", requiredEntity }
        };

            CreateCustomConfigFile(globalRestEnabled: globalRestEnabled, entityMap);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
        };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            // Setup and send GET request
            HttpRequestMessage readOpenApiDocumentRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{OPENAPI_DOCUMENT_ENDPOINT}");
            HttpResponseMessage response = await client.SendAsync(readOpenApiDocumentRequest);

            // Validate response
            if (expectsError)
            {
                Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            }
            else
            {
                // Process response body
                string responseBody = await response.Content.ReadAsStringAsync();
                Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);

                // Validate response body
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                ValidateOpenApiDocTopLevelPropertiesExist(responseProperties);
            }
        }

        /// <summary>
        /// Simulates a GET request to DAB's health check endpoint ('/') and validates the contents of the response.
        /// The expected format of the response is:
        /// {
        ///     "status": "Healthy",
        ///     "version": "0.12.0",
        ///     "appName": "dab_oss_0.12.0"
        /// }
        /// - the 'version' property format is 'major.minor.patch'
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task HealthEndpoint_ValidateContents()
        {
            // Arrange
            // At least one entity is required in the runtime config for the engine to start.
            // Even though this entity is not under test, it must be supplied enable successfull
            // config file creation.
            Entity requiredEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(Enabled: false),
                GraphQL: new("book", "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", requiredEntity }
            };

            CreateCustomConfigFile(globalRestEnabled: true, entityMap);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            // Setup and send GET request to root path.
            HttpRequestMessage getHealthEndpointContents = new(HttpMethod.Get, $"/");

            // Act - Exercise the health check endpoint code by requesting the health endpoint path '/'.
            HttpResponseMessage response = await client.SendAsync(getHealthEndpointContents);

            // Assert - Process response body and validate contents.
            // Validate HTTP return code.
            string responseBody = await response.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);
            Assert.AreEqual(expected: HttpStatusCode.OK, actual: response.StatusCode, message: "Received unexpected HTTP code from health check endpoint.");

            // Validate value of 'status' property in reponse.
            if (responseProperties.TryGetValue(key: "status", out JsonElement statusValue))
            {
                Assert.AreEqual(
                    expected: "Healthy",
                    actual: statusValue.ToString(),
                    message: "Expected endpoint to report 'Healthy'.");
            }
            else
            {
                Assert.Fail();
            }

            // Validate value of 'version' property in response.
            if (responseProperties.TryGetValue(key: DabHealthCheck.DAB_VERSION_KEY, out JsonElement versionValue))
            {
                Assert.AreEqual(
                    expected: ProductInfo.GetProductVersion(),
                    actual: versionValue.ToString(),
                    message: "Unexpected or missing version value.");
            }
            else
            {
                Assert.Fail();
            }

            // Validate value of 'app-name' property in response.
            if (responseProperties.TryGetValue(key: DabHealthCheck.DAB_APPNAME_KEY, out JsonElement appNameValue))
            {
                Assert.AreEqual(
                    expected: ProductInfo.GetDataApiBuilderUserAgent(),
                    actual: appNameValue.ToString(),
                    message: "Unexpected or missing DAB user agent string.");
            }
            else
            {
                Assert.Fail();
            }
        }

        /// <summary>
        /// Validates the behavior of the OpenApiDocumentor when the runtime config has entities with
        /// REST endpoint enabled and disabled.
        /// Enabled -> path should be created
        /// Disabled -> path not created and is excluded from OpenApi document.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task OpenApi_EntityLevelRestEndpoint()
        {
            // Create the entities under test.
            Entity restEnabledEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new("", "", false),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Entity restDisabledEntity = new(
                Source: new("publishers", EntitySourceType.Table, null, null),
                Rest: new(Enabled: false),
                GraphQL: new("publisher", "publishers", true),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
        {
            { "Book", restEnabledEntity },
            { "Publisher", restDisabledEntity }
        };

            CreateCustomConfigFile(globalRestEnabled: true, entityMap);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
        };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            // Setup and send GET request
            HttpRequestMessage readOpenApiDocumentRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{OpenApiDocumentor.OPENAPI_ROUTE}");
            HttpResponseMessage response = await client.SendAsync(readOpenApiDocumentRequest);

            // Parse response metadata
            string responseBody = await response.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);

            // Validate response metadata
            ValidateOpenApiDocTopLevelPropertiesExist(responseProperties);
            JsonElement pathsElement = responseProperties[OpenApiDocumentorConstants.TOPLEVELPROPERTY_PATHS];

            // Validate that paths were created for the entity with REST enabled.
            Assert.IsTrue(pathsElement.TryGetProperty("/Book", out _));
            Assert.IsTrue(pathsElement.TryGetProperty("/Book/id/{id}", out _));

            // Validate that paths were not created for the entity with REST disabled.
            Assert.IsFalse(pathsElement.TryGetProperty("/Publisher", out _));
            Assert.IsFalse(pathsElement.TryGetProperty("/Publisher/id/{id}", out _));

            JsonElement componentsElement = responseProperties[OpenApiDocumentorConstants.TOPLEVELPROPERTY_COMPONENTS];
            Assert.IsTrue(componentsElement.TryGetProperty(OpenApiDocumentorConstants.PROPERTY_SCHEMAS, out JsonElement componentSchemasElement));
            // Validate that components were created for the entity with REST enabled.
            Assert.IsTrue(componentSchemasElement.TryGetProperty("Book_NoPK", out _));
            Assert.IsTrue(componentSchemasElement.TryGetProperty("Book", out _));

            // Validate that components were not created for the entity with REST disabled.
            Assert.IsFalse(componentSchemasElement.TryGetProperty("Publisher_NoPK", out _));
            Assert.IsFalse(componentSchemasElement.TryGetProperty("Publisher", out _));
        }

        /// <summary>
        /// This test validates that DAB properly creates and returns a nextLink with a single $after
        /// query parameter when sending paging requests.
        /// The first request initiates a paging workload, meaning the response is expected to have a nextLink.
        /// The validation occurs after the second request which uses the previously acquired nextLink
        /// This test ensures that the second request's response body contains the expected nextLink which:
        /// - is base64 encoded and NOT URI escaped e.g. the trailing "==" are not URI escaped to "%3D%3D"
        /// - is not the same as the first response's nextLink -> DAB is properly injecting a new $after query param
        /// and updating the new nextLink
        /// - does not contain a comma (,) indicating that the URI namevaluecollection tracking the query parameters
        /// did not come across two $after query parameters. This addresses a customer raised issue where two $after
        /// query parameters were returned by DAB.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task ValidateNextLinkUsage()
        {
            // Arrange - Setup test server with entity that has >1 record so that results can be paged.
            // A short cut to using an entity with >100 records is to just include the $first=1 filter
            // as done in this test, so that paging behavior can be invoked.

            const string ENTITY_NAME = "Bookmark";

            // At least one entity is required in the runtime config for the engine to start.
            // Even though this entity is not under test, it must be supplied to the config
            // file creation function.
            Entity requiredEntity = new(
                Source: new("bookmarks", EntitySourceType.Table, null, null),
                Rest: new(Enabled: true),
                GraphQL: new(Singular: "", Plural: "", Enabled: false),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { ENTITY_NAME, requiredEntity }
            };

            CreateCustomConfigFile(globalRestEnabled: true, entityMap);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            // Setup and send GET request
            HttpRequestMessage initialPaginationRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{ENTITY_NAME}?$first=1");
            HttpResponseMessage initialPaginationResponse = await client.SendAsync(initialPaginationRequest);

            // Process response body for first request and get the nextLink to use on subsequent request
            // which represents what this test is validating.
            string responseBody = await initialPaginationResponse.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);
            string nextLinkUri = responseProperties["nextLink"].ToString();

            // Act - Submit request with nextLink uri as target and capture response

            HttpRequestMessage followNextLinkRequest = new(HttpMethod.Get, nextLinkUri);
            HttpResponseMessage followNextLinkResponse = await client.SendAsync(followNextLinkRequest);

            // Assert

            Assert.AreEqual(HttpStatusCode.OK, followNextLinkResponse.StatusCode, message: "Expected request to succeed.");

            // Process the response body and inspect the "nextLink" property for expected contents.
            string followNextLinkResponseBody = await followNextLinkResponse.Content.ReadAsStringAsync();
            Dictionary<string, JsonElement> followNextLinkResponseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(followNextLinkResponseBody);

            string followUpResponseNextLink = followNextLinkResponseProperties["nextLink"].ToString();
            Uri nextLink = new(uriString: followUpResponseNextLink);
            NameValueCollection parsedQueryParameters = HttpUtility.ParseQueryString(query: nextLink.Query);
            Assert.AreEqual(expected: false, actual: parsedQueryParameters["$after"].Contains(','), message: "nextLink erroneously contained two $after query parameters that were joined by HttpUtility.ParseQueryString(queryString).");
            Assert.AreNotEqual(notExpected: nextLinkUri, actual: followUpResponseNextLink, message: "The follow up request erroneously returned the same nextLink value.");

            // Do not use SqlPaginationUtils.Base64Encode()/Decode() here to eliminate test dependency on engine code to perform an assert.
            try
            {
                Convert.FromBase64String(parsedQueryParameters["$after"]);
            }
            catch (FormatException)
            {
                Assert.Fail(message: "$after query parameter was not a valid base64 encoded value.");
            }
        }

        /// <summary>
        /// Tests the enforcement of depth limit restrictions on GraphQL queries and mutations in non-hosted mode.
        /// Verifies that requests exceeding the specified depth limit result in a BadRequest, 
        /// while requests within the limit succeed with the expected status code.
        /// Also verifies that the error message contains the current and allowed max depth limit value.
        /// Example:
        /// Query:
        /// query book_by_pk{
        ///     book_by_pk(id: 1) {         // depth: 1
        ///         id,                     // depth: 2
        ///         title,                  // depth: 2
        ///         publisher_id            // depth: 2
        ///     }
        /// }
        /// Mutation:
        /// mutation createbook {
        ///    createbook(item: { title: ""Book #1"", publisher_id: 1234 }) {         // depth: 1
        ///       title,                                                              // depth: 2
        ///       publisher_id                                                        // depth: 2
        ///   }
        /// </summary>
        /// <param name="depthLimit">The maximum allowed depth for GraphQL queries and mutations.</param>
        /// <param name="operationType">Indicates whether the operation is a mutation or a query.</param>
        /// <param name="expectedStatusCodeForGraphQL">The expected HTTP status code for the operation.</param>
        [DataTestMethod]
        [DataRow(1, GraphQLOperation.Query, HttpStatusCode.BadRequest, DisplayName = "Failed Query execution when max depth limit is set to 1")]
        [DataRow(2, GraphQLOperation.Query, HttpStatusCode.OK, DisplayName = "Query execution successful when max depth limit is set to 2")]
        [DataRow(1, GraphQLOperation.Mutation, HttpStatusCode.BadRequest, DisplayName = "Failed Mutation execution when max depth limit is set to 1")]
        [DataRow(2, GraphQLOperation.Mutation, HttpStatusCode.OK, DisplayName = "Mutation execution successful when max depth limit is set to 2")]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestDepthLimitRestrictionOnGraphQLInNonHostedMode(
            int depthLimit,
            GraphQLOperation operationType,
            HttpStatusCode expectedStatusCodeForGraphQL)
        {
            // Arrange
            GraphQLRuntimeOptions graphqlOptions = new(DepthLimit: depthLimit);
            graphqlOptions = graphqlOptions with { UserProvidedDepthLimit = true };

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restOptions: new());
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query;
                if (operationType is GraphQLOperation.Mutation)
                {
                    // requested mutation operation has depth of 2
                    query = @"mutation createbook{
                                createbook(item: { title: ""Book #1"", publisher_id: 1234 }) {
                                    title
                                    publisher_id
                                }
                            }";
                }
                else
                {
                    // requested query operation has depth of 2
                    query = @"query book_by_pk{
                                book_by_pk(id: 1) {
                                    id,
                                    title,
                                    publisher_id
                                }
                            }";
                }

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                // Act
                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);

                // Assert
                Assert.AreEqual(expectedStatusCodeForGraphQL, graphQLResponse.StatusCode);
                string body = await graphQLResponse.Content.ReadAsStringAsync();
                JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(body);
                if (graphQLResponse.StatusCode == HttpStatusCode.OK)
                {
                    Assert.IsTrue(responseJson.TryGetProperty("data", out JsonElement data), "The response should contain data.");
                    Assert.IsFalse(data.TryGetProperty("errors", out _), "The response should not contain any errors.");
                }
                else
                {
                    Assert.IsTrue(responseJson.TryGetProperty("errors", out JsonElement data), "The response should contain errors.");
                    Assert.IsTrue(data.EnumerateArray().Any(), "The response should contain at least one error.");
                    Assert.IsTrue(data.EnumerateArray().FirstOrDefault().TryGetProperty("message", out JsonElement message), "The error should contain a message.");
                    string errorMessage = message.GetString();
                    string expectedErrorMessage = $"The GraphQL document has an execution depth of 2 which exceeds the max allowed execution depth of {depthLimit}.";
                    Assert.AreEqual(expectedErrorMessage, errorMessage, "The error message should contain the current and allowed max depth limit value.");
                }
            }
        }

        /// <summary>
        /// This test verifies that the depth-limit specified for GraphQL does not affect introspection queries.
        /// In this test, we have specified the depth limit as 2 and we are sending introspection query with depth 6.
        /// The expected result is that the query should be successful and should not return any errors.
        /// Example:
        /// {
        ///    __schema {               // depth: 1
        ///       types {               // depth: 2
        ///         name                // depth: 3
        ///         fields {            // depth: 3
        ///           name              // depth: 4
        ///           type {            // depth: 4
        ///            name             // depth: 5
        ///            kind             // depth: 5
        ///            ofType {         // depth: 5
        ///              name           // depth: 6
        ///              kind           // depth: 6
        ///             }
        ///         }
        ///     }
        /// }
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod]
        public async Task TestGraphQLIntrospectionQueriesAreNotImpactedByDepthLimit()
        {
            // Arrange
            GraphQLRuntimeOptions graphqlOptions = new(DepthLimit: 2);
            graphqlOptions = graphqlOptions with { UserProvidedDepthLimit = true };

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restOptions: new());
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // nested depth:6
                string query = @"{
                                    __schema {
                                        types {
                                        name
                                        fields {
                                            name
                                            type {
                                                name
                                                kind
                                                    ofType {
                                                        name
                                                        kind
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                // Act
                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);

                // Assert
                Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);
                string body = await graphQLResponse.Content.ReadAsStringAsync();

                JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(body);
                Assert.IsNotNull(responseJson, "The response should be a valid JSON.");
                Assert.IsTrue(responseJson.TryGetProperty("data", out JsonElement data), "The response should contain data.");
                Assert.IsFalse(data.TryGetProperty("errors", out _), "The response should not contain any errors.");
                Assert.IsTrue(responseJson.GetProperty("data").TryGetProperty("__schema", out JsonElement schema));
                Assert.IsNotNull(schema, "The response should contain schema information.");
            }
        }

        /// <summary>
        /// Tests the behavior of GraphQL queries in non-hosted mode when the depth limit is explicitly set to -1 or null.
        /// Setting the depth limit to -1 is intended to disable the depth limit check, allowing queries of any depth.
        /// Using null as default value of dab which also disables the depth limit check.
        /// This test verifies that queries are processed successfully without any errors under these configurations.
        /// Example Query:
        /// {
        ///     book_by_pk(id: 1) {         // depth: 1
        ///         id,                     // depth: 2
        ///         title,                  // depth: 2
        ///         publisher_id            // depth: 2
        ///     }
        /// }
        /// </summary>
        /// <param name="depthLimit"> </param>
        [DataTestMethod]
        [DataRow(-1, DisplayName = "Setting -1 for depth-limit will disable the depth limit")]
        [DataRow(null, DisplayName = "Using default value: null for depth-limit which also disables the depth limit check")]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestNoDepthLimitOnGrahQLInNonHostedMode(int? depthLimit)
        {
            // Arrange
            GraphQLRuntimeOptions graphqlOptions = new(DepthLimit: depthLimit);
            graphqlOptions = graphqlOptions with { UserProvidedDepthLimit = true };

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restOptions: new());
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // requested query operation has depth of 2
                string query = @"{
                                    book_by_pk(id: 1) {
                                        id,
                                        title,
                                        publisher_id
                                    }
                                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                // Act
                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);

                // Assert
                Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);
                string body = await graphQLResponse.Content.ReadAsStringAsync();

                JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(body);
                Assert.IsNotNull(responseJson, "The response should be a valid JSON.");
                Assert.IsTrue(responseJson.TryGetProperty("data", out JsonElement data), "The response should contain data.");
                Assert.IsFalse(data.TryGetProperty("errors", out _), "The response should not contain any errors.");
                Assert.IsTrue(data.TryGetProperty("book_by_pk", out _), "The response data should contain book_by_pk data.");
            }
        }

        /// <summary>
        /// Helper function to write custom configuration file. with minimal REST/GraphQL global settings
        /// using the supplied entities.
        /// </summary>
        /// <param name="globalRestEnabled">flag to enable or disabled REST globally.</param>
        /// <param name="entityMap">Collection of entityName -> Entity object.</param>
        private static void CreateCustomConfigFile(bool globalRestEnabled, Dictionary<string, Entity> entityMap)
        {
            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(Enabled: globalRestEnabled),
                    GraphQL: new(),
                    Host: new(null, null)
                ),
                Entities: new(entityMap));

            File.WriteAllText(
                path: CUSTOM_CONFIG_FILENAME,
                contents: runtimeConfig.ToJson());
        }

        /// <summary>
        /// Validates that all the OpenAPI description document's top level properties exist.
        /// A failure here indicates that there was an undetected failure creating the OpenAPI document.
        /// </summary>
        /// <param name="responseComponents">Represent a deserialized JSON result from retrieving the OpenAPI document</param>
        private static void ValidateOpenApiDocTopLevelPropertiesExist(Dictionary<string, JsonElement> responseProperties)
        {
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_OPENAPI));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_INFO));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_SERVERS));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_PATHS));
            Assert.IsTrue(responseProperties.ContainsKey(OpenApiDocumentorConstants.TOPLEVELPROPERTY_COMPONENTS));
        }

        /// <summary>
        /// Validates that schema introspection requests fail when allow-introspection is false in the runtime configuration.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        private static async Task ExecuteGraphQLIntrospectionQueries(TestServer server, HttpClient client, bool expectError)
        {
            string graphQLQueryName = "__schema";
            string graphQLQuery = @"{
                __schema {
                    types {
                        name
                    }
                }
            }";

            string expectedErrorMessageFragment = "Introspection is not allowed for the current request.";

            try
            {
                RuntimeConfigProvider configProvider = server.Services.GetRequiredService<RuntimeConfigProvider>();

                JsonElement actual = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                    client,
                    configProvider,
                    query: graphQLQuery,
                    queryName: graphQLQueryName,
                    variables: null,
                    clientRoleHeader: null
                    );

                if (expectError)
                {
                    SqlTestHelper.TestForErrorInGraphQLResponse(
                        response: actual.ToString(),
                        message: expectedErrorMessageFragment,
                        statusCode: ErrorCodes.Validation.IntrospectionNotAllowed
                    );
                }
            }
            catch (Exception ex)
            {
                // ExecuteGraphQLRequestAsync will raise an exception when no "data" key
                // exists in the GraphQL JSON response.
                Assert.Fail(message: "No schema metadata in GraphQL response." + ex.Message);
            }
        }

        private static JsonContent GetJsonContentForCosmosConfigRequest(string endpoint, string config = null, bool useAccessToken = false)
        {
            if (CONFIGURATION_ENDPOINT == endpoint)
            {
                ConfigurationPostParameters configParams = GetCosmosConfigurationParameters();
                if (config is not null)
                {
                    configParams = configParams with { Configuration = config };
                }

                if (useAccessToken)
                {
                    configParams = configParams with
                    {
                        ConnectionString = "AccountEndpoint=https://localhost:8081/;",
                        AccessToken = GenerateMockJwtToken()
                    };
                }

                return JsonContent.Create(configParams);
            }
            else if (CONFIGURATION_ENDPOINT_V2 == endpoint)
            {
                ConfigurationPostParametersV2 configParams = GetCosmosConfigurationParametersV2();
                if (config != null)
                {
                    configParams = configParams with { Configuration = config };
                }

                if (useAccessToken)
                {
                    // With an invalid access token, when a new instance of CosmosClient is created with that token, it
                    // won't throw an exception.  But when a graphql request is coming in, that's when it throws a 401
                    // exception. To prevent this, CosmosClientProvider parses the token and retrieves the "exp" property
                    // from the token, if it's not valid, then we will throw an exception from our code before it
                    // initiating a client. Uses a valid fake JWT access token for testing purposes.
                    RuntimeConfig overrides = new(
                        Schema: null,
                        DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, "AccountEndpoint=https://localhost:8081/;", new()),
                        Runtime: null,
                        Entities: new(new Dictionary<string, Entity>()));

                    configParams = configParams with
                    {
                        ConfigurationOverrides = overrides.ToJson(),
                        AccessToken = GenerateMockJwtToken()
                    };
                }

                return JsonContent.Create(configParams);
            }
            else
            {
                throw new ArgumentException($"Unexpected configuration endpoint. {endpoint}");
            }
        }

        private static string GenerateMockJwtToken()
        {
            string mySecret = "PlaceholderPlaceholderPlaceholder";
            SymmetricSecurityKey mySecurityKey = new(Encoding.ASCII.GetBytes(mySecret));

            JwtSecurityTokenHandler tokenHandler = new();
            SecurityTokenDescriptor tokenDescriptor = new()
            {
                Subject = new ClaimsIdentity(new Claim[] { }),
                Expires = DateTime.UtcNow.AddMinutes(5),
                Issuer = "http://mysite.com",
                Audience = "http://myaudience.com",
                SigningCredentials = new SigningCredentials(mySecurityKey, SecurityAlgorithms.HmacSha256Signature)
            };

            SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private static ConfigurationPostParameters GetCosmosConfigurationParameters()
        {
            RuntimeConfig configuration = ReadCosmosConfigurationFromFile();
            return new(
                configuration.ToJson(),
                File.ReadAllText("schema.gql"),
                $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}",
                AccessToken: null);
        }

        private static ConfigurationPostParametersV2 GetCosmosConfigurationParametersV2()
        {
            RuntimeConfig configuration = ReadCosmosConfigurationFromFile();
            RuntimeConfig overrides = new(
                Schema: null,
                DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}", new()),
                Runtime: null,
                Entities: new(new Dictionary<string, Entity>()));

            return new(
                configuration.ToJson(),
                overrides.ToJson(),
                File.ReadAllText("schema.gql"),
                AccessToken: null);
        }

        /// <summary>
        /// Helper used to create the post-startup configuration payload sent to configuration controller.
        /// Adds entity used to hydrate authorization resolver post-startup and validate that hydration succeeds.
        /// Additional pre-processing performed acquire database connection string from a local file.
        /// </summary>
        /// <returns>ConfigurationPostParameters object.</returns>
        private static JsonContent GetPostStartupConfigParams(string environment, RuntimeConfig runtimeConfig, string configurationEndpoint)
        {
            string connectionString = GetConnectionStringFromEnvironmentConfig(environment);

            string serializedConfiguration = runtimeConfig.ToJson();

            if (configurationEndpoint == CONFIGURATION_ENDPOINT)
            {
                ConfigurationPostParameters returnParams = new(
                    Configuration: serializedConfiguration,
                    Schema: null,
                    ConnectionString: connectionString,
                    AccessToken: null);
                return JsonContent.Create(returnParams);
            }
            else if (configurationEndpoint == CONFIGURATION_ENDPOINT_V2)
            {
                RuntimeConfig overrides = new(
                    Schema: null,
                    DataSource: new DataSource(DatabaseType.MSSQL, connectionString, new()),
                    Entities: new(new Dictionary<string, Entity>()),
                    Runtime: null);

                ConfigurationPostParametersV2 returnParams = new(
                    Configuration: serializedConfiguration,
                    ConfigurationOverrides: overrides.ToJson(),
                    Schema: null,
                    AccessToken: null);

                return JsonContent.Create(returnParams);
            }
            else
            {
                throw new InvalidOperationException("Invalid configurationEndpoint");
            }
        }

        /// <summary>
        /// Hydrates configuration after engine has started and triggers service instantiation
        /// by executing HTTP requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <param name="config">Post-startup configuration</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config</returns>
        private static async Task<HttpStatusCode> HydratePostStartupConfiguration(HttpClient httpClient, JsonContent content, string configurationEndpoint)
        {
            // Hydrate configuration post-startup
            HttpResponseMessage postResult =
                await httpClient.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            return await GetRestResponsePostConfigHydration(httpClient);
        }

        /// <summary>
        /// Executing REST requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config,
        /// else the response code from the REST request</returns>
        private static async Task<HttpStatusCode> GetRestResponsePostConfigHydration(HttpClient httpClient)
        {
            // Retry request RETRY_COUNT times in 1 second increments to allow required services
            // time to instantiate and hydrate permissions.
            int retryCount = RETRY_COUNT;
            HttpStatusCode responseCode = HttpStatusCode.ServiceUnavailable;
            while (retryCount > 0)
            {
                // Spot test authorization resolver utilization to ensure configuration is used.
                HttpResponseMessage postConfigHydrationResult =
                    await httpClient.GetAsync($"api/{POST_STARTUP_CONFIG_ENTITY}");
                responseCode = postConfigHydrationResult.StatusCode;

                if (postConfigHydrationResult.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    retryCount--;
                    Thread.Sleep(TimeSpan.FromSeconds(RETRY_WAIT_SECONDS));
                    continue;
                }

                break;
            }

            return responseCode;
        }

        /// <summary>
        /// Executing GraphQL POST requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config,
        /// else the response code from the GRAPHQL request</returns>
        private static async Task<HttpStatusCode> GetGraphQLResponsePostConfigHydration(HttpClient httpClient)
        {
            // Retry request RETRY_COUNT times in 1 second increments to allow required services
            // time to instantiate and hydrate permissions.
            int retryCount = RETRY_COUNT;
            HttpStatusCode responseCode = HttpStatusCode.ServiceUnavailable;
            while (retryCount > 0)
            {
                string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

                object payload = new { query };

                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await httpClient.SendAsync(graphQLRequest);
                responseCode = graphQLResponse.StatusCode;

                if (responseCode == HttpStatusCode.ServiceUnavailable)
                {
                    retryCount--;
                    Thread.Sleep(TimeSpan.FromSeconds(RETRY_WAIT_SECONDS));
                    continue;
                }

                break;
            }

            return responseCode;
        }

        /// <summary>
        /// Helper  method to instantiate RuntimeConfig object needed for multiple create tests.
        /// </summary>
        /// <returns></returns>
        public static RuntimeConfig InitialzieRuntimeConfigForMultipleCreateTests(bool isMultipleCreateOperationEnabled)
        {
            // Multiple create operations are enabled.
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true, MultipleMutationOptions: new(new(enabled: isMultipleCreateOperationEnabled)));

            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            EntityAction createAction = new(
                Action: EntityActionOperation.Create,
                Fields: null,
                Policy: new());

            EntityAction readAction = new(
                Action: EntityActionOperation.Read,
                Fields: null,
                Policy: new());

            EntityPermission[] permissions = new[] { new EntityPermission(Role: AuthorizationResolver.ROLE_ANONYMOUS, Actions: new[] { readAction, createAction }) };

            EntityRelationship bookRelationship = new(Cardinality: Cardinality.One,
                                                      TargetEntity: "Publisher",
                                                      SourceFields: new string[] { },
                                                      TargetFields: new string[] { },
                                                      LinkingObject: null,
                                                      LinkingSourceFields: null,
                                                      LinkingTargetFields: null);

            Entity bookEntity = new(Source: new("books", EntitySourceType.Table, null, null),
                                    Rest: null,
                                    GraphQL: new(Singular: "book", Plural: "books"),
                                    Permissions: permissions,
                                    Relationships: new Dictionary<string, EntityRelationship>() { { "publishers", bookRelationship } },
                                    Mappings: null);

            string bookEntityName = "Book";

            Dictionary<string, Entity> entityMap = new()
            {
                { bookEntityName, bookEntity }
            };

            EntityRelationship publisherRelationship = new(Cardinality: Cardinality.Many,
                                                           TargetEntity: "Book",
                                                           SourceFields: new string[] { },
                                                           TargetFields: new string[] { },
                                                           LinkingObject: null,
                                                           LinkingSourceFields: null,
                                                           LinkingTargetFields: null);

            Entity publisherEntity = new(
                Source: new("publishers", EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "publisher", Plural: "publishers"),
                Permissions: permissions,
                Relationships: new Dictionary<string, EntityRelationship>() { { "books", publisherRelationship } },
                Mappings: null);

            entityMap.Add("Publisher", publisherEntity);

            RuntimeConfig runtimeConfig = new(Schema: "IntegrationTestMinimalSchema",
                                              DataSource: dataSource,
                                              Runtime: new(restRuntimeOptions, graphqlOptions, Host: new(Cors: null, Authentication: null, Mode: HostMode.Development), Cache: null),
                                              Entities: new(entityMap));
            return runtimeConfig;
        }

        /// <summary>
        /// Instantiate minimal runtime config with custom global settings.
        /// </summary>
        /// <param name="dataSource">DataSource to pull connection string required for engine start.</param>
        /// <returns></returns>
        public static RuntimeConfig InitMinimalRuntimeConfig(
            DataSource dataSource,
            GraphQLRuntimeOptions graphqlOptions,
            RestRuntimeOptions restOptions,
            Entity entity = null,
            string entityName = null,
            EntityCacheOptions cacheOptions = null
            )
        {
            entity ??= new(
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "book", Plural: "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            entityName ??= "Book";

            Dictionary<string, Entity> entityMap = new()
        {
            { entityName, entity }
        };

            // Adding an entity with only Authorized Access
            Entity anotherEntity = new(
                Source: new("publishers", EntitySourceType.Table, null, null),
                Rest: null,
                GraphQL: new(Singular: "publisher", Plural: "publishers"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_AUTHENTICATED) },
                Relationships: null,
                Mappings: null
                );
            entityMap.Add("Publisher", anotherEntity);

            return new(
                Schema: "IntegrationTestMinimalSchema",
                DataSource: dataSource,
                Runtime: new(restOptions, graphqlOptions,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development), Cache: cacheOptions),
                Entities: new(entityMap)
            );
        }

        /// <summary>
        /// Gets PermissionSetting object allowed to perform all actions.
        /// </summary>
        /// <param name="roleName">Name of role to assign to permission</param>
        /// <returns>PermissionSetting</returns>
        public static EntityPermission GetMinimalPermissionConfig(string roleName)
        {
            EntityAction actionForRole = new(
                Action: EntityActionOperation.All,
                Fields: null,
                Policy: new()
            );

            return new EntityPermission(
                Role: roleName,
                Actions: new[] { actionForRole }
            );
        }

        /// <summary>
        /// Reads configuration file for defined environment to acquire the connection string.
        /// CI/CD Pipelines and local environments may not have connection string set as environment variable.
        /// </summary>
        /// <param name="environment">Environment such as TestCategory.MSSQL</param>
        /// <returns>Connection string</returns>
        public static string GetConnectionStringFromEnvironmentConfig(string environment)
        {
            FileSystem fileSystem = new();
            string sqlFile = new FileSystemRuntimeConfigLoader(fileSystem).GetFileNameForEnvironment(environment, considerOverrides: true);
            string configPayload = File.ReadAllText(sqlFile);

            RuntimeConfigLoader.TryParseConfig(configPayload, out RuntimeConfig runtimeConfig, replaceEnvVar: true);

            return runtimeConfig.DataSource.ConnectionString;
        }

        private static void ValidateCosmosDbSetup(TestServer server)
        {
            QueryEngineFactory queryEngineFactory = (QueryEngineFactory)server.Services.GetService(typeof(IQueryEngineFactory));
            Assert.IsInstanceOfType(queryEngineFactory.GetQueryEngine(DatabaseType.CosmosDB_NoSQL), typeof(CosmosQueryEngine));

            MutationEngineFactory mutationEngineFactory = (MutationEngineFactory)server.Services.GetService(typeof(IMutationEngineFactory));
            Assert.IsInstanceOfType(mutationEngineFactory.GetMutationEngine(DatabaseType.CosmosDB_NoSQL), typeof(CosmosMutationEngine));

            MetadataProviderFactory metadataProviderFactory = (MetadataProviderFactory)server.Services.GetService(typeof(IMetadataProviderFactory));
            Assert.IsTrue(metadataProviderFactory.ListMetadataProviders().Any(x => x.GetType() == typeof(CosmosSqlMetadataProvider)));

            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Clients);
            Assert.IsTrue(cosmosClientProvider.Clients.Any());
        }

        /// <summary>
        /// Create basic runtime config with given DatabaseType and connectionString with no entity.
        /// </summary>
        /// <returns></returns>
        private static RuntimeConfig CreateBasicRuntimeConfigWithNoEntity(
            DatabaseType dbType = DatabaseType.MSSQL,
            string connectionString = "")
        {
            DataSource dataSource = new(dbType, connectionString, new());

            RuntimeConfig runtimeConfig = new(
                Schema: "testSchema.json",
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Host: new(null, null)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            return runtimeConfig;
        }

        private bool HandleException<T>(Exception e) where T : Exception
        {
            if (e is AggregateException aggregateException)
            {
                aggregateException.Handle(HandleException<T>);
                return true;
            }
            else if (e is T)
            {
                return true;
            }

            return false;
        }
    }
}
