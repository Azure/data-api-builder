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
                // Retry file deletion to handle cases where a TestServer or file watcher
                // from the test hasn't fully released the file handle yet.
                int maxRetries = 5;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        File.Delete(CUSTOM_CONFIG_FILENAME);
                        break;
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        Thread.Sleep(200 * (i + 1));
                    }
                }
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

            // Resolve assembly version. Not possible to do in DataRow as DataRows expect compile-time constants.
            string resolvedAssemblyVersion = ProductInfo.GetDataApiBuilderUserAgent();
            expectedDabModifiedConnString += resolvedAssemblyVersion;

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
        /// Validates that DAB supplements the MongoDB database connection strings with the property "ApplicationName" and
        /// 1. Adds the property/value "Application Name=dab_oss_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is not set.
        /// 2. Adds the property/value "Application Name=dab_hosted_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is set to "dab_hosted".
        /// (DAB_APP_NAME_ENV is set in hosted scenario or when user sets the value.)
        /// NOTE: "#pragma warning disable format" is used here to avoid removing intentional, readability promoting spacing in DataRow display names.
        /// </summary>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Application Name.</param>
        /// <param name="dabEnvOverride">Whether DAB_APP_NAME_ENV is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        [DataTestMethod]
        [DataRow("mongodb://foo:27017"                             , "mongodb://foo:27017;Application Name="             , false, DisplayName = "[MONGODB]: DAB adds version 'dab_oss_major_minor_patch' to non-provided connection string property 'Application Name'.")]
        [DataRow("mongodb://foo:27017;Application Name=CustAppName;" , "mongodb://foo:27017;Application Name=CustAppName," , false, DisplayName = "[MONGODB]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'Application Name' property.")]
        [DataRow("mongodb://foo:27017;App=CustAppName;"              , "mongodb://foo:27017;Application Name=CustAppName," , false, DisplayName = "[MONGODB]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'App' property and resolves property to 'Application Name'.")]
        [DataRow("mongodb://foo:27017"                              , "mongodb://foo:27017;Application Name="             , true , DisplayName = "[MONGODB]: DAB adds DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to non-provided connection string property 'Application Name'.")]
        [DataRow("mongodb://foo:27017;Application Name=CustAppName;" , "mongodb://foo:27017;Application Name=CustAppName," , true , DisplayName = "[MONGODB]: DAB appends DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'Application Name' property.")]
        [DataRow("mongodb://foo:27017;App=CustAppName;"              , "mongodb://foo:27017;Application Name=CustAppName," , true , DisplayName = "[MONGODB]: DAB appends version string 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'App' property and resolves property to 'Application Name'.")]
        #pragma warning restore format
        public void MongoDbConnStringSupplementedWithAppNameProperty(
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

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(DatabaseType.MongoDB, configProvidedConnString);

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
        /// Validates that DAB supplements the CosmosDB database connection strings with the property "ApplicationName" and
        /// 1. Adds the property/value "Application Name=dab_oss_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is not set.
        /// 2. Adds the property/value "Application Name=dab_hosted_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is set to "dab_hosted".
        /// (DAB_APP_NAME_ENV is set in hosted scenario or when user sets the value.)
        /// NOTE: "#pragma warning disable format" is used here to avoid removing intentional, readability promoting spacing in DataRow display names.
        /// </summary>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Application Name.</param>
        /// <param name="dabEnvOverride">Whether DAB_APP_NAME_ENV is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        [DataTestMethod]
        [DataRow("AccountEndpoint=https://foo:8081/;AccountKey=secret"                             , "AccountEndpoint=https://foo:8081/;Application Name="             , false, DisplayName = "[COSMOSDB]: DAB adds version 'dab_oss_major_minor_patch' to non-provided connection string property 'Application Name'.")]
        [DataRow("AccountEndpoint=https://foo:8081/;Application Name=CustAppName;" , "AccountEndpoint=https://foo:8081/;Application Name=CustAppName," , false, DisplayName = "[COSMOSDB]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'Application Name' property.")]
        [DataRow("AccountEndpoint=https://foo:8081/;App=CustAppName;"              , "AccountEndpoint=https://foo:8081/;Application Name=CustAppName," , false, DisplayName = "[COSMOSDB]: DAB appends version 'dab_oss_major_minor_patch' to user supplied 'App' property and resolves property to 'Application Name'.")]
        [DataRow("AccountEndpoint=https://foo:8081/;Database=db"                              , "AccountEndpoint=https://foo:8081/;Application Name="             , true , DisplayName = "[COSMOSDB]: DAB adds DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to non-provided connection string property 'Application Name'.")]
        [DataRow("AccountEndpoint=https://foo:8081/;Application Name=CustAppName;" , "AccountEndpoint=https://foo:8081/;Application Name=CustAppName," , true , DisplayName = "[COSMOSDB]: DAB appends DAB_APP_NAME_ENV value 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'Application Name' property.")]
        [DataRow("AccountEndpoint=https://foo:8081/;App=CustAppName;"              , "AccountEndpoint=https://foo:8081/;Application Name=CustAppName," , true , DisplayName = "[COSMOSDB]: DAB appends version string 'dab_hosted' and version suffix '_major_minor_patch' to user supplied 'App' property and resolves property to 'Application Name'.")]
        #pragma warning restore format
        public void CosmosDbConnStringSupplementedWithAppNameProperty(
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

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(DatabaseType.CosmosDB_NoSQL, configProvidedConnString);

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
            Assert.IsTrue(EnumerableUtilities.IsNullOrEmpty(configValidator.ConfigValidationExceptions));
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

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new(), new());

            // creating an entity with invalid table name
            Entity entityWithInvalidSourceName = new(
                Source: new("bokos", EntitySourceType.Table, null, null),
                Fields: null,
                Rest: null,
                GraphQL: new(Singular: "book", Plural: "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            Entity entityWithInvalidSourceType = new(
                Source: new("publishers", EntitySourceType.StoredProcedure, null, null),
                Fields: null,
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

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new(), new());

            // creating an entity with invalid table name
            Entity entityWithInvalidSource = new(
                Source: new(null, EntitySourceType.Table, null, null),
                Fields: null,
                Rest: null,
                GraphQL: new(Singular: "book", Plural: "books"),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            // creating an entity with invalid source object and adding relationship with an entity with invalid source
            Entity entityWithInvalidSourceAndRelationship = new(
                Source: new(null, EntitySourceType.Table, null, null),
                Fields: null,
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
        /// Tests that DAB supplements the CosmosDB database connection strings with the property "ApplicationName" and
        /// 1. Adds the property/value "Application Name=dab_oss_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is not set.
        /// 2. Adds the property/value "Application Name=dab_hosted_Major.Minor.Patch" when the env var DAB_APP_NAME_ENV is set to "dab_hosted".
        /// (DAB_APP_NAME_ENV is set in hosted scenario or when user sets the value.)
        /// NOTE: "#pragma warning disable format" is used here to avoid removing intentional, readability promoting spacing in DataRow display names.
        /// </summary>
        /// <param name="configProvidedConnString">connection string provided in the config.</param>
        /// <param name="expectedDabModifiedConnString">Updated connection string with Application Name.</param>
        /// <param name="dabEnvOverride">Whether DAB_APP_NAME_ENV is set in environment. (Always present in hosted scenario or if user supplies value.)</param>
        public void CosmosDbConnStringSupplementedWithAppNameProperty(
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

            RuntimeConfig runtimeConfig = CreateBasicRuntimeConfigWithNoEntity(DatabaseType.CosmosDB_NoSQL, configProvidedConnString);

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
                // Retry file deletion to handle cases where a TestServer or file watcher
                // from the test hasn't fully released the file handle yet.
                int maxRetries = 5;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        File.Delete(CUSTOM_CONFIG_FILENAME);
                        break;
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        Thread.Sleep(200 * (i + 1));
                    }
                }
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

            // Resolve assembly version. Not possible to do in DataRow as DataRows expect compile-time constants.
            string resolvedAssemblyVersion = ProductInfo.GetDataApiBuilderUserAgent();
            expectedDabModifiedConnString += resolvedAssemblyVersion;

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
        /// For mutation operations, both the respective operation(create/update/delete) + read permissions are needed to receive a valid response.
        /// In this test, the Anonymous role is configured with only create permission.
        /// So, a create mutation executed in the context of the Anonymous role is expected to result in
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
            McpRuntimeOptions mcpRuntimeOptions = new(Enabled: false);

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
                                  Fields: null,
                                  Rest: null,
                                  GraphQL: new(Singular: "Stock", Plural: "Stocks"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Stock";
            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions, entity, entityName);

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
                    // Pre-clean to avoid PK violation if a previous run left the row behind.
                    string preCleanupDeleteMutation = @"
                        mutation {
                            deleteStock(categoryid: 5001, pieceid: 5001) {
                                categoryid
                                pieceid
                            }
                        }";

                    _ = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                    client,
                    server.Services.GetRequiredService<RuntimeConfigProvider>(),
                    query: preCleanupDeleteMutation,
                    queryName: "deleteStock",
                    variables: null,
                    authToken: AuthTestHelper.CreateAppServiceEasyAuthToken(),
                    clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);

                    // A create mutation operation is executed in the context of Anonymous role and the response is expected to be valid
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
                        authToken: null,
                        clientRoleHeader: AuthorizationResolver.ROLE_ANONYMOUS
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

                    ValidateMutationSucceededAtDbLayer(server, client, graphQLQuery, queryName, AuthTestHelper.CreateAppServiceEasyAuthToken(), AuthorizationResolver.ROLE_AUTHENTICATED);
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
                        authToken: AuthTestHelper.CreateAppServiceEasyAuthToken(),
                        clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);
                }
            }
        }

        /// <summary>
        /// Test to ensure that the built in ErrorHandler catches unexpected exceptions and transforms
        /// them to a serialized error response.
        /// The error message is validated to ensure it does not contain sensitive internal information.
        /// GraphQL response with errors contains the error code and message.
        /// REST responses with errors contain the problem-details structure.
        /// </summary>
        /// <param name="includeExceptionMessage">Whether to include the exception message in the response.</param>
        /// <param name="includeStackTrace">Whether to include the stack trace in the response.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(true, true, DisplayName = "Unexpected error occurred - Detailed error message and stack trace returned.")]
        [DataRow(true, false, DisplayName = "Unexpected error occurred - Generic error message returned.")]
        [DataRow(false, true, DisplayName = "Expected error - Detailed error message and stack trace returned.")]
        [DataRow(false, false, DisplayName = "Expected error - Generic error message returned.")]
        public async Task TestErrorHandlerBehaviorForUnexpectedErrors(bool includeExceptionMessage, bool includeStackTrace)
        {
            // Arrange
            string errorMessage = "errorMessage";
            string stackTrace = "stackTrace";
            string gqlErrorCode = "INTERNAL_SERVER_ERROR";

            const string CUSTOM_CONFIG = "custom-config.json";
            Dictionary<string, Entity> entityMap = new();
            entityMap.Add("test-entity",
                new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    Fields: null,
                    Rest: new(Enabled: true),
                    GraphQL: new("test-entity", "test-entities"),
                    Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                    Relationships: null,
                    Mappings: null));
            CreateCustomConfigFile(entityMap);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient()
            {
                BaseAddress = new Uri("http://localhost")
            };
            string query = @"{
                                book_by_pk(id: 1) {
                                    id
                                    title
                                    publisher_id
                                }
                            }";

            object payload = new { query };

            // Act
            // Send request that will result in an unexpected error
            HttpResponseMessage response = await client.PostAsync("/graphql", new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

            // Assert
            string responseBody = await response.Content.ReadAsStringAsync();
            JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(responseBody);

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.IsTrue(responseBody.Contains(gqlErrorCode), "The response should contain the error code.");
            if (includeExceptionMessage)
            {
                Assert.IsTrue(responseBody.Contains(errorMessage), "The response should contain the exception message.");
            }
            else
            {
                Assert.IsFalse(responseBody.Contains(errorMessage), "The response should NOT contain the exception message.");
            }

            if (includeStackTrace)
            {
                Assert.IsTrue(responseBody.Contains(stackTrace), "The response should contain the stack trace.");
            }
            else
            {
                Assert.IsFalse(responseBody.Contains(stackTrace), "The response should NOT contain the stack trace.");
            }

            // Ensure response doesn't contain sensitive information
            Assert.AreEqual(false, responseBody.Contains("ConnectionStrings"), "The response should not contain sensitive information like ConnectionStrings.");
            Assert.AreEqual(false, responseBody.Contains("/path/to/sensitive/file"), "The response should not contain sensitive information like file paths.");
        }

        /// <summary>
        /// This test method validates that a config with custom properties is still valid.
        /// The properties "description" and "extraProperty" are not part of the default config.
        /// However, their presence should not affect the config's validity.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        public async Task TestConfigWithCustomPropertiesIsValid()
        {
            string configJson = @"{
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
                                    ""entities"":{ },
                                    ""description"": ""This is a custom config for testing."",
                                    ""extraProperty"": ""Some extra value""
                                }";

            // Act
            bool configParsed = RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(configParsed, "Config with custom properties should be valid.");
        }

        /// <summary>
        /// This test method validates that an exception is thrown if there's a null model in filter parser.
        /// </summary>
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
            McpRuntimeOptions mcpOptions = new(Enabled: true);

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
                Fields: null,
                Rest: new(Enabled: false),
                GraphQL: new("graphql_incompatible", "graphql_incompatibles", entityGraphQLEnabled),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: mappings
            );

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpOptions, entity, "graphqlNameCompat");
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
                restOptions: new(Path: customRestPath),
                mcpOptions: new());

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
                path: CUSTOM_CONFIG,
                contents: configuration.ToJson());

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
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: true);
            McpRuntimeOptions mcpOptions = new(Enabled: true);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            // Configure Entity for testing
            Entity entity = new(
                Source: new("graphql_incompatible", EntitySourceType.Table, null, null),
                Fields: null,
                Rest: new(Enabled: false),
                GraphQL: new("graphql_incompatible", "graphql_incompatibles", enabled: true),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            entity = entity with
            {
                Source = new("books", EntitySourceType.Table, null, null)
            };

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpOptions, entity, "graphql_incompatible");
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using TestServer server = new(Program.CreateWebHostBuilder(args))
            {
                BaseAddress = new Uri("http://localhost")
            };
            using HttpClient client = server.CreateClient();

            // Act
            // Send request that matches the custom configured GraphQL path
            HttpRequestMessage request = new(HttpMethod.Get, requestPath);
            HttpResponseMessage response = await client.SendAsync(request);

            // Assert
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
        [DataRow(SupportedHttpVerb.Delete, "/api/Book/id/1", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a DELETE request with incorrect parameter type in the request body on a table in production mode")]
        [DataRow(SupportedHttpVerb.Post, "/api/GetBooks", REQUEST_BODY_WITH_INCORRECT_PARAM_TYPES, "Invalid value provided for field: publisher_id", DisplayName = "Validates the error message for a POST request with incorrect parameter type in the request body on a stored-procedure in production mode")]
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
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Put, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST PUT disabled when enabled flag is configured to true")]
        [DataRow(SP_CONFIG_WITH_JUST_REST_ENABLED, SupportedHttpVerb.Delete, "/api/GetBooks", HttpStatusCode.MethodNotAllowed, DisplayName = "SP - REST DELETE disabled when enabled flag is configured to true")]
        public async Task TestSPRestDefaultsForManuallyConstructedConfigs(
           string entityJson,
           SupportedHttpVerb requestType,
           string requestPath,
           HttpStatusCode expectedResponseStatusCode)
        {
            string configJson = TestHelper.AddPropertiesToJson(TestHelper.BASE_CONFIG, entityJson);
            RuntimeConfigLoader.TryParseConfig(
                configJson,
                out RuntimeConfig deserializedConfig,
                replacementSettings: new(),
                logger: null,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL));
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
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                configJson,
                out RuntimeConfig deserializedConfig,
                replacementSettings: new(),
                logger: null,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL)));
            string configFileName = "custom-config.json";
            File.WriteAllText(configFileName, deserializedConfig.ToJson());
            string[] args = new[]
            {
                    $"--ConfigFileName={configFileName}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // Act
                RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();
                using HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
                using HttpResponseMessage restResponse = await client.SendAsync(restRequest);

                string graphqlQuery = @"
                {
                    books {
                        items {
                            id
                            title
                        }
                    }
                }";

                object graphqlPayload = new { query = graphqlQuery };
                HttpRequestMessage graphqlRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(graphqlPayload)
                };
                HttpResponseMessage graphqlResponse = await client.SendAsync(graphqlRequest);

                // Assert
                string expectedResponseFragment = @"{""id"":1156,""title"":""The First Publisher""}";

                // Verify number of entities
                Assert.AreEqual(expectedEntityCount, configProvider.GetConfig().Entities.Entities.Count, "Number of generated entities is not what is expected");

                // Verify REST response
                Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode, "REST request to auto-generated entity should succeed");

                string restResponseBody = await restResponse.Content.ReadAsStringAsync();
                Assert.IsTrue(!string.IsNullOrEmpty(restResponseBody), "REST response should contain data");
                Assert.IsTrue(restResponseBody.Contains(expectedResponseFragment));

                // Verify GraphQL response
                Assert.AreEqual(HttpStatusCode.OK, graphqlResponse.StatusCode, "GraphQL request to auto-generated entity should succeed");

                string graphqlResponseBody = await graphqlResponse.Content.ReadAsStringAsync();
                Assert.IsTrue(!string.IsNullOrEmpty(graphqlResponseBody), "GraphQL response should contain data");
                Assert.IsFalse(graphqlResponseBody.Contains("errors"), "GraphQL response should not contain errors");
                Assert.IsTrue(graphqlResponseBody.Contains(expectedResponseFragment));
            }
        }

        /// <summary>
        /// Executing MCP POST requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config,	
        /// else the response code from the MCP request</returns>	
        public static async Task<HttpStatusCode> GetMcpResponse(HttpClient httpClient, McpRuntimeOptions mcp)
        {
            // Retry request RETRY_COUNT times in exponential increments to allow
            // required services time to instantiate and hydrate permissions because
            // the DAB services may take an unpredictable amount of time to become ready.
            //
            // The service might still fail due to the service not being available yet,
            // but it is highly unlikely to be the case.
            int retryCount = 0;
            HttpStatusCode responseCode = HttpStatusCode.ServiceUnavailable;
            while (retryCount < RETRY_COUNT)
            {
                // Minimal MCP request (initialize) - valid JSON-RPC request.
                // Using 'initialize' because 'tools/list' requires an active session
                // in the MCP Streamable HTTP transport (ModelContextProtocol 1.0.0).
                object payload = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new { },
                        clientInfo = new { name = "dab-test", version = "1.0.0" }
                    }
                };
                HttpRequestMessage mcpRequest = new(HttpMethod.Post, mcp.Path)
                {
                    Content = JsonContent.Create(payload)
                };
                mcpRequest.Headers.Add("Accept", "application/json, text/event-stream");

                HttpResponseMessage mcpResponse = await httpClient.SendAsync(mcpRequest);
                responseCode = mcpResponse.StatusCode;

                if (responseCode == HttpStatusCode.ServiceUnavailable || responseCode == HttpStatusCode.NotFound)
                {
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(RETRY_WAIT_SECONDS, retryCount)));
                    continue;
                }

                break;
            }

            return responseCode;
        }

        /// <summary>
        /// Helper  method to instantiate RuntimeConfig object needed for multiple create tests.
        /// </summary>
        public static RuntimeConfig InitialzieRuntimeConfigForMultipleCreateTests(bool isMultipleCreateOperationEnabled)
        {
            // Multiple create operations are enabled.
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: true, MultipleMutationOptions: new(new(enabled: isMultipleCreateOperationEnabled)));

            RestRuntimeOptions restRuntimeOptions = new(Enabled: false);

            McpRuntimeOptions mcpRuntimeOptions = new(Enabled: false);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

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
                                  Fields: null,
                                  Rest: null,
                                  GraphQL: new(Singular: "Stock", Plural: "Stocks"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Stock";
            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions, entity, entityName);

            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());
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
                // With multiple create operation disabled, executing a create mutation operation without the "publisher_id" field is expected to be caught by HotChocolate
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
            McpRuntimeOptions mcpRuntimeOptions = new(Enabled: false);

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
                                  Fields: null,
                                  Rest: null,
                                  GraphQL: new(Singular: "Stock", Plural: "Stocks"),
                                  Permissions: permissions,
                                  Relationships: null,
                                  Mappings: null);

            string entityName = "Stock";
            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions, entity, entityName);

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
                        authToken: null,
                        clientRoleHeader: AuthorizationResolver.ROLE_ANONYMOUS
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

                    ValidateMutationSucceededAtDbLayer(server, client, graphQLQuery, queryName, AuthTestHelper.CreateAppServiceEasyAuthToken(), AuthorizationResolver.ROLE_AUTHENTICATED);
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
                        authToken: AuthTestHelper.CreateAppServiceEasyAuthToken(),
                        clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);
                }
            }
        }

        /// <summary>
        /// Validates the Location header field returned for a POST request when a 201 response is returned. The idea behind returning
        /// a Location header is to provide a URL against which a GET request can be performed to fetch the details of the new item.
        /// Base Route is not configured in the config file used for this test. If base-route is configured, the Location header URL should contain the base-route.
        /// This test performs a POST request, and in the event that it results in a 201 response, it performs a subsequent GET request
        /// with the Location header to validate the correctness of the URL.
        /// Currently ignored as it is part of the setof flakey tests that are being investigated, see: https://github.com/Azure/data-api-builder/issues/2010
        /// </summary>
        /// <param name="entityType">Type of the entity</param>
        /// <param name="requestPath">Request path for performing POST API requests on the entity</param>
        /// <param name="baseRoute">Configured base route</param>
        /// <param name="expectedLocationHeader">Expected value for Location field in the response header. Since, the PK of the new record is not known beforehand,
        /// the expectedLocationHeader excludes the PK. Because of this, the actual location header is validated by checking if it starts with the expectedLocationHeader.</param>
        [Ignore]
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(EntitySourceType.Table, "/api/Book", "/data-api", "http://localhost/data-api/api/Book/id/", DisplayName = "Location Header validation - Table, Base Route not configured")]
        [DataRow(EntitySourceType.StoredProcedure, "/api/GetBooks", "/data-api", "http://localhost/data-api/api/GetBooks", DisplayName = "Location Header validation - Stored Procedures, Base Route not configured")]
        public async Task ValidateLocationHeaderFieldForPostRequests(EntitySourceType entityType, string requestPath, string baseRoute, string expectedLocationHeader)
        {

            GraphQLRuntimeOptions graphqlOptions = new(Enabled: false);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: true);
            McpRuntimeOptions mcpRuntimeOptions = new(Enabled: false);

            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration;

            if (entityType is EntitySourceType.StoredProcedure)
            {
                Entity entity = new(Source: new("get_books", EntitySourceType.StoredProcedure, null, null),
                              Fields: null,
                              Rest: new(new SupportedHttpVerb[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }),
                              GraphQL: null,
                              Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                              Relationships: null,
                              Mappings: null);

                string entityName = "GetBooks";
                configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions, entity, entityName);
            }
            else
            {
                configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions);
            }

            const string CUSTOM_CONFIG = "custom-config.json";

            Config.ObjectModel.AuthenticationOptions authenticationOptions = new(Provider: EasyAuthType.StaticWebApps.ToString(), null);
            HostOptions staticWebAppsHostOptions = new(null, authenticationOptions);

            RuntimeOptions baseRouteEnabledRuntimeOptions = new(runtimeOptions.Rest, runtimeOptions.GraphQL, runtimeOptions.Mcp, staticWebAppsHostOptions, "/data-api");
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
            RuntimeConfigLoader.TryParseConfig(
                configJson,
                out RuntimeConfig deserializedConfig,
                replacementSettings: new(),
                logger: null,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL));
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
                        ""publisher_id"": 1234 ";

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
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new("", ""),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
            );

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new(), new(), viewEntity, "books_view_all");

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
        /// Validates that DAB supports a configuration without authentication, as it's optional.
        /// Ensures both REST and GraphQL queries return success when authentication is not configured.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestEngineSupportConfigWithNoAuthentication()
        {
            DataSource dataSource = new(DatabaseType.MSSQL,
                GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = CreateBasicRuntimeConfigWithSingleEntityAndAuthOptions(dataSource: dataSource, authenticationOptions: null);

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
                    books {
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

                HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);
            }
        }

        /// <summary>
        /// In CosmosDB, we store data in the form of JSON. Practically, JSON can be very complex.
        /// But DAB doesn't support JSON with circular references e.g if 'Character.Moon' is a valid JSON Path, then
        /// 'Moon.Character' should not be there, DAB would throw an exception during the load itself.
        /// </summary>
        /// <exception cref="ApplicationException"></exception>
        [TestMethod, TestCategory(TestCategory.COSMOSDBNOSQL)]
        [DataRow(GRAPHQL_SCHEMA_WITH_CYCLE_OBJECT, DisplayName = "When Circular Reference is there with Object type (i.e. 'Moon' in 'Character' Entity")]
        [DataRow(GRAPHQL_SCHEMA_WITH_CYCLE_ARRAY, DisplayName = "When Circular Reference is there with Array type (i.e. '[Moon]' in 'Character' Entity")]
        public void ValidateGraphQLSchemaForCircularReference(string schema)
        {
            // Read the base config from the file
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
        /// Mismatch of entities and types between provided GraphQL schema and DAB config results in actionable error message.
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
    character: Character
}
";
            // Read the base config from the file
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
            McpRuntimeOptions mcpRuntimeOptions = new();

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions);
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
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdatableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);
                HttpStatusCode responseCode = await HydratePostStartupConfiguration(client, content, configurationEndpoint, configuration.Runtime.Rest);

                Assert.AreEqual(expected: HttpStatusCode.OK, actual: responseCode, message: "Configuration hydration failed.");

                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
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
                        DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}", new()),
                        Entities: new(new Dictionary<string, Entity>()),
                        Runtime: null);

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
                Entities: new(new Dictionary<string, Entity>()),
                Runtime: null);

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
                    Entities: new(new Dictionary<string, Entity>),
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
        /// Helper method to create RuntimeConfig with specificed LogLevel value
        /// </summary>
        private static RuntimeConfig InitializeRuntimeWithLogLevel(Dictionary<string, LogLevel?> logLevelOptions)
        {
            TestHelper.SetupDatabaseEnvironment(MSSQL_ENVIRONMENT);

            FileSystemRuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
            baseLoader.TryLoadKnownConfig(out RuntimeConfig baseConfig);

            RuntimeConfig config = new(
                Schema: baseConfig.Schema,
                DataSource: baseConfig.DataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: new(null, null),
                    Telemetry: new(LoggerLevel: logLevelOptions)
                ),
                Entities: baseConfig.Entities
            );

            return config;
        }

        /// <summary>
        /// Tests that between multiple log level filters,
        /// the one that is more specific is always given priority.
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(LogLevel.Debug, "Azure", LogLevel.Warning, "default", typeof(IQueryExecutor))]
        [DataRow(LogLevel.Information, "Azure.DataApiBuilder", LogLevel.Error, "Azure", typeof(IQueryExecutor))]
        [DataRow(LogLevel.Warning, "Azure.DataApiBuilder.Core", LogLevel.Critical, "Azure.DataApiBuilder", typeof(RuntimeConfigValidator))]
        [DataRow(LogLevel.Error, "Azure.DataApiBuilder.Core.Configurations", LogLevel.None, "Azure.DataApiBuilder.Core", typeof(RuntimeConfigValidator))]
        public void PriorityLogLevelFilters(LogLevel highPriLevel, string highPriFilter, LogLevel lowPriLevel, string lowPriFilter, Type type)
        {
            string classString = type.FullName;
            Startup.AddValidFilters();
            Dictionary<string, LogLevel?> logLevelOptions = new();
            logLevelOptions.Add(highPriFilter, highPriLevel);
            logLevelOptions.Add(lowPriFilter, lowPriLevel);
            RuntimeConfig configWithCustomLogLevel = InitializeRuntimeWithLogLevel(logLevelOptions);
            try
            {
                RuntimeConfigValidator.ValidateLoggerFilters(configWithCustomLogLevel);
            }
            catch
            {
                Assert.Fail();
            }

            string configWithCustomLogLevelJson = configWithCustomLogLevel.ToJson();
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(configWithCustomLogLevelJson, out RuntimeConfig deserializedRuntimeConfig));

            // If filters are not a subsection from the classString, then the test will not work.
            LogLevel actualLogLevel = deserializedRuntimeConfig.GetConfiguredLogLevel(classString);

            Assert.AreEqual(expected: highPriLevel, actual: actualLogLevel);
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
        [DataRow(true, true, true, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest, GraphQL, and MCP enabled globally")]
        [DataRow(true, true, false, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest and GraphQL enabled, MCP disabled globally")]
        [DataRow(true, false, true, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest enabled, GraphQL disabled, and MCP enabled globally")]
        [DataRow(true, false, false, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest enabled, GraphQL and MCP disabled globally")]
        [DataRow(false, true, true, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest disabled, GraphQL and MCP enabled globally")]
        [DataRow(false, true, false, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest disabled, GraphQL enabled, and MCP disabled globally")]
        [DataRow(false, false, true, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT, DisplayName = "V1 - Rest and GraphQL disabled, MCP enabled globally")]
        [DataRow(true, true, true, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest, GraphQL, and MCP enabled globally")]
        [DataRow(true, true, false, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest and GraphQL enabled, MCP disabled globally")]
        [DataRow(true, false, true, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest enabled, GraphQL disabled, and MCP enabled globally")]
        [DataRow(true, false, false, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest enabled, GraphQL and MCP disabled globally")]
        [DataRow(false, true, true, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest disabled, GraphQL and MCP enabled globally")]
        [DataRow(false, true, false, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.NotFound, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest disabled, GraphQL enabled, and MCP disabled globally")]
        [DataRow(false, false, true, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.OK, CONFIGURATION_ENDPOINT_V2, DisplayName = "V2 - Rest and GraphQL disabled, MCP enabled globally")]
        public async Task TestGlobalFlagToEnableRestGraphQLAndMcpForHostedAndNonHostedEnvironment(
            bool isRestEnabled,
            bool isGraphQLEnabled,
            bool isMcpEnabled,
            HttpStatusCode expectedStatusCodeForREST,
            HttpStatusCode expectedStatusCodeForGraphQL,
            HttpStatusCode expectedStatusCodeForMcp,
            string configurationEndpoint)
        {
            GraphQLRuntimeOptions graphqlOptions = new(AllowIntrospection: isGraphQLEnabled);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: isRestEnabled);
            McpRuntimeOptions mcpRuntimeOptions = new(Enabled: isMcpEnabled);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions);
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

                // GraphQL request
                HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
                Assert.AreEqual(expectedStatusCodeForGraphQL, graphQLResponse.StatusCode, "The GraphQL response is different from the expected result.");

                // REST request
                HttpRequestMessage restRequest = new(HttpMethod.Get, $"{configuration.Runtime.Rest.Path}/Book");
                HttpResponseMessage restResponse = await client.SendAsync(restRequest);
                Assert.AreEqual(expectedStatusCodeForREST, restResponse.StatusCode, "The REST response is different from the expected result.");

                // MCP request
                HttpStatusCode mcpResponseCode = await GetMcpResponse(client, configuration.Runtime.Mcp);
                Assert.AreEqual(expectedStatusCodeForMcp, mcpResponseCode, "The MCP response is different from the expected result.");
            }

            // Hosted Scenario
            // Instantiate new server with no runtime config for post-startup configuration hydration tests.
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdatableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                JsonContent content = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration, configurationEndpoint);

                HttpResponseMessage postResult = await client.PostAsync(configurationEndpoint, content);
                Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode, "The hydration post-response is different from the expected result.");

                HttpStatusCode restResponseCode = await GetRestResponsePostConfigHydration(client, configuration.Runtime.Rest);
                Assert.AreEqual(expected: expectedStatusCodeForREST, actual: restResponseCode, "The REST hydration post-response is different from the expected result.");

                HttpStatusCode graphqlResponseCode = await GetGraphQLResponsePostConfigHydration(client, configuration.Runtime.GraphQL);
                Assert.AreEqual(expected: expectedStatusCodeForGraphQL, actual: graphqlResponseCode, "The GraphQL hydration post-response is different from the expected result.");

                // TODO: Issue #3012 - Currently DAB is unable to start MCP with the hydration post-response.
                // This needs to be fixed before uncommenting the MCP check
                // HttpStatusCode mcpResponseCode = await GetMcpResponse(client, configuration.Runtime.Mcp);
                // Assert.AreEqual(expected: expectedStatusCodeForMcp, actual: mcpResponseCode, "The MCP hydration post-response is different from the expected result.");
            }
        }

        /// <summary>
        /// Tests that the when Rest or GraphQL is disabled Globally,
        /// any requests made will get a 404 response.
        /// </summary>
        /// <param name="isRestEnabled">The custom configured REST enabled property in configuration.</param>
        /// <param name="isGraphQLEnabled">The custom configured GraphQL enabled property in configuration.</param>
        /// <param name="expectedStatusCode">Expected HTTP status code code for the request</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(true, true, HttpStatusCode.OK, DisplayName = "Rest and GraphQL enabled globally")]
        [DataRow(true, false, HttpStatusCode.OK, DisplayName = "Rest enabled, GraphQL disabled globally")]
        [DataRow(false, true, HttpStatusCode.NotFound, DisplayName = "Rest disabled, GraphQL enabled globally")]
        [DataRow(false, false, HttpStatusCode.NotFound, DisplayName = "Rest and GraphQL disabled globally")]
        public async Task TestGlobalFlagToEnableRestGraphQL(
            bool isRestEnabled,
            bool isGraphQLEnabled,
            HttpStatusCode expectedStatusCode)
        {
            GraphQLRuntimeOptions graphqlOptions = new(Enabled: isGraphQLEnabled);
            RestRuntimeOptions restRuntimeOptions = new(Enabled: isRestEnabled);
            McpRuntimeOptions mcpRuntimeOptions = new(Enabled: false);

            DataSource dataSource = new(DatabaseType.MSSQL, GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restRuntimeOptions, mcpRuntimeOptions);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
        };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                // Setup and send GET request
                HttpRequestMessage readOpenApiDocumentRequest = new(HttpMethod.Get, $"{RestRuntimeOptions.DEFAULT_PATH}/{OpenApiDocumentor.OPENAPI_ROUTE}");
                HttpResponseMessage response = await client.SendAsync(readOpenApiDocumentRequest);

                // Assert response
                Assert.AreEqual(expectedStatusCode, response.StatusCode);
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
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new("", "", Enabled: false),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Entity restDisabledEntity = new(
                Source: new("publishers", EntitySourceType.Table, null, null),
                Fields: null,
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

            CreateCustomConfigFile(entityMap, enableGlobalRest: true);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
        };

            using TestServer server = new(Program.CreateWebHostBuilder(args))
            using (HttpClient client = server.CreateClient())
            {
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
        }

        /// <summary>
        /// Tests that the files specified in data-source-file are read and used to create the database objects.
        /// In this case, the config is using data-source-file to specify the table and its relationships.
        /// The test ensures that after the configuration is loaded, the application can successfully query the table and also the related object (linking table).
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task TestDataSourceFileReadsAndCreatesDatabaseObjects()
        {
            string dataSourceFile = @"../data-source-files/sample-books-data-source.json";
            string configFile = @"{
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
                                    ""entities"":{ },
                                    ""data-source-file"": [
                                        ""%24{dataSourceFile}%""
                                    ]
                                }";

            // Act
            bool configParsed = RuntimeConfigLoader.TryParseConfig(configFile, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(configParsed, "Config with data-source-file should be valid.");

            string serializedConfig = deserializedConfig.ToJson();
            using JsonDocument parsedDocument = JsonDocument.Parse(serializedConfig);
            {
                // Validate the data-source-file property exists
                JsonElement dataSourceFileElement = parsedDocument.RootElement.GetProperty("data-source-file");
                Assert.IsTrue(dataSourceFileElement.ValueKind == JsonValueKind.Array && dataSourceFileElement.GetArrayLength() == 1);

                string dataSourceFilePath = dataSourceFileElement[0].GetString();
                Assert.IsFalse(string.IsNullOrWhiteSpace(dataSourceFilePath));

                // Manually invoke the DataSourceFile configuration processor.
                // This is usually triggered by the engine during startup.
                TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
                RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();

                configProvider.ProcessDataSourceFile(dataSourceFilePath);

                Assert.AreEqual(2, configProvider.GetConfig().Entities.Entities.Count, "Expected two entities to be created from data-source-file.");
            }

            // Clean up
            await Task.CompletedTask;
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
            // Even though this entity is not under test, it must be supplied to enable successfull
            // config file creation.
            Entity requiredEntity = new(
                Source: new("bookmarks", EntitySourceType.Table, null, null),
                Fields: null,
                Rest: new(Enabled: true),
                GraphQL: new(Singular: "", Plural: "", Enabled: false),
                Permissions: new[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { ENTITY_NAME, requiredEntity }
            };

            PaginationOptions paginationOptions = new()
            {
                DefaultPageSize = 1,
                MaxPageSize = 1,
                UserProvidedDefaultPageSize = true,
                UserProvidedMaxPageSize = true,
                NextLinkRelative = false // Absolute nextLink required for this test
            };

            CreateCustomConfigFile(entityMap, enableGlobalRest: true, paginationOptions: paginationOptions);

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

            // Build the Uri from nextLink string for query parsing.
            // If relative, combine with base; if absolute, use as is.
            Uri nextLink = null;
            if (Uri.IsWellFormedUriString(followUpResponseNextLink, UriKind.Absolute))
            {
                nextLink = new(followUpResponseNextLink, UriKind.Absolute);
            }
            else if (Uri.IsWellFormedUriString(followUpResponseNextLink, UriKind.Relative))
            {
                nextLink = new(new("http://localhost:5000"), followUpResponseNextLink);
            }
            else
            {
                Assert.Fail($"Invalid nextLink URI format: {followUpResponseNextLink}");
            }

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

            // Validate nextLink is relative if nextLinkRelative is true or false otherwise.
            // The assertion is now done directly on the original string, not on the parsed Uri object.
            if (isNextLinkRelative)
            {
                // The server returned a relative URL, so it should NOT start with http/https
                Assert.IsFalse(Uri.IsWellFormedUriString(followUpResponseNextLink, UriKind.Absolute),
                    $"nextLink was expected to be relative but was absolute: {followUpResponseNextLink}");
                Assert.IsTrue(followUpResponseNextLink.StartsWith("/"),
                    $"nextLink was expected to start with '/' (relative), got: {followUpResponseNextLink}");
            }
            else
            {
                Assert.IsTrue(Uri.IsWellFormedUriString(followUpResponseNextLink, UriKind.Absolute),
                    $"nextLink was expected to be absolute but was relative: {followUpResponseNextLink}");
                Assert.IsTrue(followUpResponseNextLink.StartsWith("http"),
                    $"nextLink was expected to start with http/https, got: {followUpResponseNextLink}");
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

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restOptions: new(), mcpOptions: new());
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
                Assert.IsNotNull(responseJson, "The response should be a valid JSON.");
                Assert.IsTrue(responseJson.TryGetProperty("data", out JsonElement data), "The response should contain data.");
                Assert.IsFalse(data.TryGetProperty("errors", out _), "The response should not contain any errors.");
                Assert.IsTrue(data.TryGetProperty("book_by_pk", out _), "The response data should contain book_by_pk data.");
            }
        }

        /// <summary>
        /// This test verifies that the depth-limit specified for GraphQL does not affect introspection queries.
        /// In this test, we have specified the depth limit as 2 and we are sending introspection query with depth 6.
        /// The expected result is that the query should be successful and should not return any errors.
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

            RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions, restOptions: new(), mcpOptions: new());
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
                Assert.IsTrue(data.TryGetProperty("__schema", out _), "The response data should contain __schema data.");
            }
        }
    }
}
