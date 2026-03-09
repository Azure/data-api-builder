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
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using Serilog;
using VerifyMSTest;
using static Azure.DataApiBuilder.Config.FileSystemRuntimeConfigLoader;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.AppServiceAuthentication;
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
        private const int RETRY_WAIT_SECONDS = 2;

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
                    ""provider"": ""AppService""
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

        public const string CONFIG_FILE_WITH_NO_OPTIONAL_FIELD = @"{
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
                                    },
                                    ""entities"":{ }
                                }";

        public const string CONFIG_FILE_WITH_NO_AUTHENTICATION_FIELD = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""cors"": {
                                                ""origins"": [
                                                    ""http://localhost:5000""
                                                ],
                                                ""allow-credentials"": false
                                            }
                                        }
                                    },
                                    ""entities"":{ }
                                }";

        public const string CONFIG_FILE_WITH_UNKNOWN_AUTHENTICATION_PROVIDER = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""cors"": {
                                                ""origins"": [
                                                    ""http://localhost:5000""
                                                ],
                                                ""allow-credentials"": false
                                            },
                                            ""authentication"": {
                                                ""provider"": ""UnknownProvider""
                                            }
                                        }
                                    },
                                    ""entities"":{ }
                                }";

        public const string CONFIG_FILE_WITH_MISSING_JWT_PROPERTY = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""cors"": {
                                                ""origins"": [
                                                    ""http://localhost:5000""
                                                ],
                                                ""allow-credentials"": false
                                            },
                                            ""authentication"": {
                                                ""provider"": ""EntraID""
                                            }
                                        }
                                    },
                                    ""entities"":{ }
                                }";

        public const string CONFIG_FILE_WITH_MISSING_JWT_CHILD_PROPERTIES = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""cors"": {
                                                ""origins"": [
                                                    ""http://localhost:5000""
                                                ],
                                                ""allow-credentials"": false
                                            },
                                            ""authentication"": {
                                                ""provider"": ""EntraID"",
                                                ""jwt"": { }
                                            }
                                        }
                                    },
                                    ""entities"":{ }
                                }";

        public const string CONFIG_FILE_WITH_AUTHENTICATION_PROVIDER_THAT_SHOULD_NOT_HAVE_JWT = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""cors"": {
                                                ""origins"": [
                                                    ""http://localhost:5000""
                                                ],
                                                ""allow-credentials"": false
                                            },
                                            ""authentication"": {
                                                ""provider"": ""Simulator"",
                                                ""jwt"": { ""audience"": ""https://example.com"", ""issuer"": ""https://example.com"" }
                                            }
                                        }
                                    },
                                    ""entities"":{ }
                                }";
        public const string CONFIG_FILE_WITH_NO_CORS_FIELD = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""authentication"": {
                                                ""provider"": ""AppService""
                                            }
                                        }
                                    },
                                    ""entities"":{ }
                                }";

        public const string CONFIG_FILE_WITH_BOOLEAN_AS_ENV = @"{
                                    // Link for latest draft schema.
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                        ""database-type"": ""mssql"",
                                        ""connection-string"": ""sample-conn-string"",
                                        ""health"": {
                                            ""enabled"": <REPLACE_VALUE>
                                        }
                                    },
                                    ""runtime"": {
                                        ""health"": {
                                            ""enabled"": <REPLACE_VALUE>
                                        },
                                        ""rest"": {
                                            ""enabled"": <REPLACE_VALUE>,
                                            ""path"": ""/api""
                                        },
                                        ""graphql"": {
                                            ""enabled"": <REPLACE_VALUE>,
                                            ""path"": ""/graphql"",
                                            ""allow-introspection"": true
                                        },
                                        ""host"": {
                                            ""authentication"": {
                                                ""provider"": ""AppService""
                                            }
                                        },
                                        ""telemetry"": {
                                            ""application-insights"":{
                                                ""enabled"":  <REPLACE_VALUE>,
                                                ""connection-string"":""sample-ai-connection-string""
                                            }

                                        }

                                    },
                                    ""entities"":{ }
                                }";

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            if (File.Exists(CUSTOM_CONFIG_FILENAME))
            {
                File.Delete(CUSTOM_CONFIG_FILENAME);
            }

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
                    server = new(Program.CreateWebHostFromInMemoryUpdatableConfBuilder(args));
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
                json: runtimeConfig.ToJson(),
                config: out RuntimeConfig updatedRuntimeConfig,
                replacementSettings: new(doReplaceEnvVar: true));

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
        [DataRow("Host=foo;Username=testuser;", "Host=foo;Username=testuser;Application Name=", true, DisplayName = "[PGSQL]:DAB adds DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to non-provided connection string property 'ApplicationName'.")]
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
                json: runtimeConfig.ToJson(),
                config: out RuntimeConfig updatedRuntimeConfig,
                replacementSettings: new(doReplaceEnvVar: true));

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
                json: runtimeConfig.ToJson(),
                config: out RuntimeConfig updatedRuntimeConfig,
                replacementSettings: new(doReplaceEnvVar: true));

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
        /// Validates that DAB supplements the CosmosDB database connection strings with the property "Database" and
        /// 1. Adds the property/value "Database=config_db" when the env var COSMOSDB_DATABASE_NAME is not set.
        /// 2. Adds the property/value "Database=dab_hosted_Major.Minor.Patch" when the env var COSMOSDB_DATABASE_NAME is set to "dab_hosted".
        /// (COSMOSDB_DATABASE_NAME is set in hosted scenario or when user sets the value.)
        /// NOTE: "#pragma warning disable format" is used here to avoid removing intentional, readability promoting spacing in DataRow display names.
        /// </summary>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Database.</param>
        /// <param name="cosmosDbEnvOverride">Whether COSMOSDB_DATABASE_NAME is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        #pragma warning disable format
        [DataTestMethod]
        [DataRow("AccountEndpoint=https://localhost:8081/;", "AccountEndpoint=https://localhost:8081/;Database=config_db", false, DisplayName = "[CosmosDB]: DAB adds version 'dab_oss_major_minor_patch' to non-provided connection string property 'Database'.")]
        [DataRow("AccountEndpoint=https://localhost:8081/;Database=CustDbName", "AccountEndpoint=https://localhost:8081/;Database=CustDbName", false, DisplayName = "[CosmosDB]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'Database' property.")]
        [DataRow("AccountEndpoint=https://localhost:8081/;App=CustDbName" , "AccountEndpoint=https://localhost:8081/;Database=CustDbName", false, DisplayName = "[CosmosDB]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'App' property and resolves property to 'Database'.")]
        [DataRow("AccountEndpoint=https://localhost:8081/;", "AccountEndpoint=https://localhost:8081/;Database=dab_hosted", true , DisplayName = "[CosmosDB]: DAB adds COSMOSDB_DATABASE_NAME value 'dab_hosted' and version suffix '_major_minor_patch' to non-provided connection string property 'Database'.")]
        [DataRow("AccountEndpoint=https://localhost:8081/;Database=CustDbName", "AccountEndpoint=https://localhost:8081/;Database=CustDbName", true , DisplayName = "[CosmosDB]: DAB appends COSMOSDB_DATABASE_NAME value 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'Database' property.")]
        [DataRow("AccountEndpoint=https://localhost:8081/;App=CustDbName" , "AccountEndpoint=https://localhost:8081/;Database=CustDbName", true , DisplayName = "[CosmosDB]: DAB appends version string 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'App' property and resolves property to 'Database'.")]
        #pragma warning restore format
        public void CosmosDbConnStringSupplementedWithDbProperty(
            string configProvidedConnString,
            string expectedDabModifiedConnString,
            bool cosmosDbEnvOverride)
        {
            // Explicitly set the COSMOSDB_DATABASE_NAME to null to ensure that the COSMOSDB_DATABASE_NAME is not set.
            if (cosmosDbEnvOverride)
            {
                Environment.SetEnvironmentVariable(ProductInfo.COSMOSDB_DATABASE_NAME, "dab_hosted");
            }
            else
            {
                Environment.SetEnvironmentVariable(ProductInfo.COSMOSDB_DATABASE_NAME, null);
            }

            // Resolve assembly version. Not possible to do in DataRow as DataRows expect compile-time constants.
            string resolvedAssemblyVersion = ProductInfo.GetDataApiBuilderUserAgent();
            expectedDabModifiedConnString += resolvedAssemblyVersion;

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(DatabaseType.CosmosDB_NoSQL, configProvidedConnString);
            bool configParsed = RuntimeConfigLoader.TryParseConfig(
                json: runtimeConfig.ToJson(),
                config: out RuntimeConfig updatedRuntimeConfig,
                replacementSettings: new(doReplaceEnvVar: true));

            // Assert
            Assert.AreEqual(
                expected: true,
                actual: configParsed,
                message: "Runtime config unexpectedly failed parsing.");
            Assert.AreEqual(
                expected: expectedDabModifiedConnString,
                actual: updatedRuntimeConfig.DataSource.ConnectionString,
                message: "DAB did not properly set the 'Database' connection string property.");
        }

        /// <summary>
        /// Invalidates the config if the required datasource property is missing.
        /// Validates that an appropriate error message is returned in the response.
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestMissingDataSourceInConfig()
        {
            string configMissingDataSource = @"{
                ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
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
                        ""cors"": {
                            ""origins"": [
                                ""http://localhost:5000""
                            ],
                            ""allow-credentials"": false
                        }
                    }
                },
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

            string configWithInvalidDataSource = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, configMissingDataSource);

            // Only need to check if exception is thrown. The exact message/content is not important.
            try
            {
                RuntimeConfigLoader.TryParseConfig(configWithInvalidDataSource, out RuntimeConfig runtimeConfig, replacementSettings: new());
            }
            catch (Exception e)
            {
                Assert.AreEqual("The following required properties are missing from the configuration: data-source.", e.Message);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (e as DataApiBuilderException).StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, (e as DataApiBuilderException).SubStatusCode);
                return;
            }

            Assert.Fail("Config with missing data-source did not result in an exception.");
        }

        /// <summary>
        /// Validates that the config file with an invalid JSON schema returns a 400 Bad Request response.
        /// The test confirms that the error is detected and reported by the configuration loader.
        /// And also tests that a valid subsequent configuration can be applied after a bad config.
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestInvalidConfigFileSchema()
        {
            string badConfig = @"{
                                    ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                    ""data-source"": {
                                    ""database-type"": ""mssql"",
                                    ""connection-string"": ""sample-conn-string""
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
                                            ""cors"": {
                                                ""origins"": [
                                                    ""http://localhost:5000""
                                                ],
                                                ""allow-credentials"": false
                                            },
                                            ""authentication"": {
                                                ""provider"": ""AppService""
                                            },
                                            ""mode"": ""development""
                                        }
                                    },
                                    ""entities"": { }
                                }";

            // Use a bad config with an extra comma before closing brace '}' to invalidate the JSON.
            string configWithInvalidJsonSchema = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, badConfig);
            try
            {
                RuntimeConfigLoader.TryParseConfig(configWithInvalidJsonSchema, out RuntimeConfig runtimeConfig, replacementSettings: new());
                Assert.Fail("Config with invalid JSON schema did not result in an exception.");
            }
            catch (JsonException jsonEx)
            {
                // Expected exception
                Assert.IsTrue(jsonEx.Message.Contains("Line 17, column 8"), jsonEx.Message);
            }

            // Sanity test to validate that a correct config file can be loaded after a bad config file.
            string goodConfig = TestHelper.BASE_CONFIG;
            RuntimeConfigLoader.TryParseConfig(goodConfig, out RuntimeConfig runtimeConfig2, replacementSettings: new());
            Assert.IsNotNull(runtimeConfig2);
            Assert.AreEqual(DatabaseType.MSSQL, runtimeConfig2.DataSource.DatabaseType);
        }

        /// <summary>
        /// This test validates the following scenario:
        /// 1. Start with a valid config with a single entity.
        /// 2. Update the config to add a new entity.
        /// 3. Validate that the new entity is correctly added and the old entity is unaffected.
        /// 4. Validate that the changes are reflected in the OpenAPI document.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestHotSwapAddNewEntityToConfig()
        {
            // 1. Start with a valid config with a single entity.
            string initialEntityConfig = @"{
                                                ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                                ""data-source"": {
                                                ""database-type"": ""mssql"",
                                                ""connection-string"": ""sample-conn-string""
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
                                                        ""cors"": {
                                                            ""origins"": [
                                                                ""http://localhost:5000""
                                                            ],
                                                            ""allow-credentials"": false
                                                        }
                                                    }
                                                },
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

            // 2. Update the config to add a new entity.
            string updatedEntityConfig = @"{
                                                ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                                ""data-source"": {
                                                ""database-type"": ""mssql"",
                                                ""connection-string"": ""sample-conn-string""
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
                                                        ""cors"": {
                                                            ""origins"": [
                                                                ""http://localhost:5000""
                                                            ],
                                                            ""allow-credentials"": false
                                                        }
                                                    }
                                                },
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
                                                    },
                                                    ""Author"": {
                                                        ""source"": {
                                                            ""object"": ""authors"",
                                                            ""type"": ""table""
                                                        },
                                                        ""graphql"": {
                                                            ""enabled"": true,
                                                            ""type"": {
                                                                ""singular"": ""author"",
                                                                ""plural"": ""authors""
                                                            }
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

            string validConfigFile = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, initialEntityConfig);
            RuntimeConfigLoader.TryParseConfig(validConfigFile, out RuntimeConfig runtimeConfig, replacementSettings: new());

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, runtimeConfig.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            // Act
            // validate initial state
            await ValidateServiceConfigState(server, client, dataSource, "Book", expectBookExists: true, expectAuthorExists: false);

            // 2. Update the config to add a new entity.
            File.WriteAllText(CUSTOM_CONFIG, updatedEntityConfig);

            // Simulate a pause to allow the service to process the config update.
            await Task.Delay(5000);

            // validate updated state
            await ValidateServiceConfigState(server, client, dataSource, entityName: "Book", expectBookExists: true, expectAuthorExists: true);
        }

        /// <summary>
        /// Performs GET requests for both REST and GraphQL and validates the response.
        /// The expected response is that the REST request succeeds but the GraphQL request fails
        /// with an appropriate authorization error message.
        /// </summary>
        /// <param name="server">Test server created for the test</param>
        /// <param name="client">HTTP client</param>
        /// <param name="graphqlQuery">GraphQL query text</param>
        /// <param name="queryName">GraphQL query name</param>
        /// <param name="authToken">Auth token for the requests</param>
        /// <param name="clientRoleHeader">Client role header for the requests</param>
        private static async Task ValidateRestAndGraphQLAccessWithAuth(
            TestServer server,
            HttpClient client,
            string graphqlQuery,
            string queryName,
            string authToken,
            string clientRoleHeader)
        {
            // REST request
            HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
            HttpResponseMessage restResponse = await client.SendAsync(restRequest);
            Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);

            // GraphQL request
            object payload = new { query = graphqlQuery };
            HttpRequestMessage graphqlRequest = new(HttpMethod.Post, "/graphql")
            {
                Content = JsonContent.Create(payload)
            };

            HttpResponseMessage graphqlResponse = await client.SendAsync(graphqlRequest);
            Assert.AreEqual(HttpStatusCode.Forbidden, graphqlResponse.StatusCode);
            string body = await graphqlResponse.Content.ReadAsStringAsync();
            Assert.IsTrue(body.Contains("not have permission"), body);
        }

        /// <summary>
        /// Validate that the service is configured correctly with the expected entities and paths,
        /// and that the service responds as expected to requests for these entities.
        /// </summary>
        /// <param name="server">The test server</param>
        /// <param name="client">The HTTP client</param>
        /// <param name="dataSource">The data source used in the runtime config</param>
        /// <param name="entityName">The name of the entity to check</param>
        /// <param name="expectBookExists">Whether the book entity should exist</param>
        /// <param name="expectAuthorExists">Whether the author entity should exist</param>
        private static async Task ValidateServiceConfigState(TestServer server, HttpClient client, DataSource dataSource, string entityName, bool expectBookExists, bool expectAuthorExists)
        {
            RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();
            RuntimeConfig config = configProvider.GetConfig();

            // Validate number of entities
            Assert.AreEqual(2, config.Entities.Entities.Count, "Number of entities is not what is expected");

            // Validate REST API response
            HttpResponseMessage restResponse = await client.GetAsync($"/api/{entityName}");
            Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode, "REST request did not return OK");

            string restResponseBody = await restResponse.Content.ReadAsStringAsync();
            Assert.IsTrue(restResponseBody.Contains("items"), "REST response does not contain expected items array");

            // Validate GraphQL API response
            string graphqlQuery = $@"{{
                {entityName.ToCamelCase()} {{
                    items {{
                        id
                        title
                    }}
                }}
            }}";

            object payload = new { query = graphqlQuery };
            HttpRequestMessage graphqlRequest = new(HttpMethod.Post, "/graphql")
            {
                Content = JsonContent.Create(payload)
            };

            HttpResponseMessage graphqlResponse = await client.SendAsync(graphqlRequest);
            Assert.AreEqual(HttpStatusCode.OK, graphqlResponse.StatusCode, "GraphQL request did not return OK");

            string graphqlResponseBody = await graphqlResponse.Content.ReadAsStringAsync();
            Assert.IsTrue(graphqlResponseBody.Contains("data"), "GraphQL response does not contain expected data field");
            Assert.IsTrue(graphqlResponseBody.Contains($"\"{entityName}\": {{"), "GraphQL response does not contain expected entity field");

            // Specific checks for book entity
            if (expectBookExists)
            {
                Assert.IsTrue(graphqlResponseBody.Contains("id"), "GraphQL response for book does not contain expected id field");
                Assert.IsTrue(graphqlResponseBody.Contains("title"), "GraphQL response for book does not contain expected title field");
            }

            // Specific checks for author entity
            if (expectAuthorExists)
            {
                Assert.IsTrue(graphqlResponseBody.Contains("publisher_id"), "GraphQL response for author does not contain expected publisher_id field");
            }
        }

        /// <summary>
        /// Regression test for issue #376
        /// Validates that a config with an entity having a source set to an empty string
        /// does not cause the engine to crash and burn.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestEmptySourceConfigDoesNotCauseCrash()
        {
            string emptySourceConfig = @"{
                                            ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                            ""data-source"": {
                                            ""database-type"": ""mssql"",
                                            ""connection-string"": ""sample-conn-string""
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
                                                    ""cors"": {
                                                        ""origins"": [
                                                            ""http://localhost:5000""
                                                        ],
                                                        ""allow-credentials"": false
                                                    }
                                                }
                                            },
                                            ""entities"": {
                                                ""Book"": {
                                                    ""source"": {
                                                        ""object"": """",
                                                        ""type"": ""table""
                                                    },
                                                    ""graphql"": {
                                                        ""enabled"": true,
                                                        ""type"": {
                                                            ""singular"": ""book"",
                                                            ""plural"": ""books""
                                                        }
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

            string configWithEmptySource = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, emptySourceConfig);
            RuntimeConfigLoader.TryParseConfig(configWithEmptySource, out RuntimeConfig runtimeConfig, replacementSettings: new());

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, runtimeConfig.ToJson());

            using TestServer server = new(Program.CreateWebHostBuilder(new[] { $"--ConfigFileName={CUSTOM_CONFIG}" }));
            using HttpClient client = server.CreateClient();
            {
                // Act - calling the health endpoint should work if the engine has started successfully.
                HttpResponseMessage healthResponse = await client.GetAsync("/");
                Assert.AreEqual(HttpStatusCode.OK, healthResponse.StatusCode);
                string responseBody = await healthResponse.Content.ReadAsStringAsync();
                Assert.IsTrue(responseBody.Contains(@"""status"":""Healthy"""), responseBody);
            }
        }

        /// <summary>
        /// Regression test for issue #3012
        /// Validates that DAB doesn't fail with unhandled exceptions when there is an error in one of the config updated handlers.
        /// A bad request response is expected instead.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestConfigUpdateHandlerErrorDoesNotCauseCrash()
        {
            string initialConfig = @"{
                                        ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                        ""data-source"": {
                                        ""database-type"": ""mssql"",
                                        ""connection-string"": ""sample-conn-string""
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
                                                ""cors"": {
                                                    ""origins"": [
                                                        ""http://localhost:5000""
                                                    ],
                                                    ""allow-credentials"": false
                                                }
                                            }
                                        },
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

            string configWithBadHandler = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, @"{
                                                                                        ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                                                                        ""data-source"": {
                                                                                        ""database-type"": ""mssql"",
                                                                                        ""connection-string"": ""sample-conn-string""
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
                                                                                                ""cors"": {
                                                                                                    ""origins"": [
                                                                                                        ""http://localhost:5000""
                                                                                                    ],
                                                                                                    ""allow-credentials"": false
                                                                                                }
                                                                                            }
                                                                                        },
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
                                                                                        },
                                                                                        ""runtime-config-updated-handlers"": [
                                                                                            {
                                                                                                ""handler"": ""http://bad-url"",
                                                                                                ""status-code"": 400,
                                                                                                ""timeout-seconds"": 2
                                                                                            }
                                                                                        ]
                                                                                    }");

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();
            {
                // Simulate a short delay to allow the config update to be processed
                await Task.Delay(3000);

                string response = await (await client.GetAsync("/graphql")).Content.ReadAsStringAsync();
                Assert.IsTrue(response.Contains(@"""status"":""Healthy"""), response);
            }
        }

        /// <summary>
        /// Tests behavior when a config with same entity name but different case is applied.
        /// Start with a config with a single entity: Book. A second config with the same entity but different case:
        /// book is applied. Verify that the entity is updated to be book but that the original entity remains.
        /// Verify that the OpenAPI document reflects the current state of the config.
        /// </summary>
        /// <seealso cref="https://github.com/Azure/data-api-builder/issues/2928"/>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestHotSwapChangeEntityCaseInConfig()
        {
            // 1. Start with a valid config with a single entity.
            string initialConfig = @"{
                                        ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                        ""data-source"": {
                                        ""database-type"": ""mssql"",
                                        ""connection-string"": ""sample-conn-string""
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
                                                ""cors"": {
                                                    ""origins"": [
                                                        ""http://localhost:5000""
                                                    ],
                                                    ""allow-credentials"": false
                                                }
                                            }
                                        },
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

            // 2. Update the config to change entity name to different case.
            string updatedConfig = @"{
                                        ""$schema"":""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch-alpha/dab.draft.schema.json"",
                                        ""data-source"": {
                                        ""database-type"": ""mssql"",
                                        ""connection-string"": ""sample-conn-string""
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
                                                ""cors"": {
                                                    ""origins"": [
                                                        ""http://localhost:5000""
                                                    ],
                                                    ""allow-credentials"": false
                                                }
                                            }
                                        },
                                        ""entities"": {
                                            ""book"": {
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

            string validConfigFile = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, initialConfig);
            RuntimeConfigLoader.TryParseConfig(validConfigFile, out RuntimeConfig runtimeConfig, replacementSettings: new());

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, runtimeConfig.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            // Act
            // validate initial state
            await ValidateServiceConfigState(server, client, dataSource, "Book", expectBookExists: true, expectAuthorExists: false);

            // 2. Update the config to change entity name to different case.
            File.WriteAllText(CUSTOM_CONFIG, updatedConfig);

            // Simulate a pause to allow the service to process the config update.
            await Task.Delay(5000);

            // validate updated state
            await ValidateServiceConfigState(server, client, dataSource, entityName: "Book", expectBookExists: true, expectAuthorExists: true);
        }

        /// <summary>
        /// Conversion for string values that are to be used as URL path segments.
        /// Space, ?, #, [, ], {, }, |, \, ^, ~, and % are to be escaped.
        /// Escaped byte values are prefixed with a dot (.) to form %xx hex sequences.
        /// Escape sequence "%20" is converted to "+"
        /// </summary>
        /// <param name="uRI"></param>
        /// <param name="expectedConvertedURI"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static async Task ValidateUriEscaping(string uRI, string expectedConvertedURI, string message)
        {
            HttpRequestMessage request = new(HttpMethod.Get, uRI);
            HttpResponseMessage response = await new HttpClient().SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

            JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(body);
            _ = responseJson.TryGetProperty("data", out JsonElement data);
            _ = data.TryGetProperty("book_by_pk", out JsonElement bookByPK);

            Assert.AreEqual(expectedConvertedURI, bookByPK.GetProperty("id").ToString(), message);
        }

        /// <summary>
        /// Validate that the error response for REST requests with invalid request body (non-JSON) returns
        /// HTTP 400 - BadRequest and contains the right error message.
        /// </summary>
        /// <param name="requestType">Type of REST request</param>
        /// <param name="requestPath">Endpoint for the REST request</param>
        /// <param name="requestBody">Request body</param>
        /// <param name="expectedErrorMessage">Right error message that should be shown to the end user</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(SupportedHttpVerb.Post, "/api/Book", "invalid json", "Invalid request body. Contained unexpected fields in body: invalid json", DisplayName = "Malformed JSON in request body")]
        [DataRow(SupportedHttpVerb.Post, "/api/Book", "", "Invalid request body. Contained unexpected fields in body: ", DisplayName = "Empty JSON request body")]
        // PUT and PATCH with application/x-www-form-urlencoded
        [DataRow(SupportedHttpVerb.Put, "/api/Book/id/1", "id=1&title=New+Title", "Invalid request body. Contained unexpected fields in body: id, title", DisplayName = "Invalid request body for PUT operation")]
        [DataRow(SupportedHttpVerb.Patch, "/api/Book/id/1", "title=New+Title", "Invalid request body. Contained unexpected fields in body: title", DisplayName = "Invalid request body for PATCH operation")]
        public async Task TestInvalidRequestBodyErrorMessage(SupportedHttpVerb requestType, string requestPath, string requestBody, string expectedErrorMessage)
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
                Assert.IsTrue(body.Contains(expectedErrorMessage), body);
            }
        }
    }
}
