namespace Cli.Tests
{
    public static class TestHelper
    {
        // Config file name for tests
        public static string _testRuntimeConfig = "dab-config-test.json";
        public const string DAB_DRAFT_SCHEMA_TEST_PATH = "https://dataapibuilder.azureedge.net/schemas/vmajor.minor.patch-alpha/dab.draft.schema.json";

        /// <summary>
        /// Adds the entity properties to the configuration and returns the updated configuration json as a string.
        /// </summary>
        /// <param name="configuration">Configuration Json.</param>
        /// <param name="entityProperties">Entity properties to be added to the configuration.</param>
        public static string AddPropertiesToJson(string configuration, string entityProperties)
        {
            JObject configurationJson = JObject.Parse(configuration);
            JObject entityPropertiesJson = JObject.Parse(entityProperties);

            configurationJson.Merge(entityPropertiesJson, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Union
            });
            return configurationJson.ToString();
        }

        /// <summary>
        /// Returns a new dab Process with the given command and flags
        /// </summary>
        public static Process StartDabProcess(string command, string flags)
        {
            Process process = new()
            {
                StartInfo =
                {
                    FileName = @"./dab",
                    Arguments = $"{command} {flags}",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            // Asserting that a new process has been started and no existing process is reused.
            Assert.IsTrue(process.Start());

            // The new process should not be exited after triggering the start command.
            Assert.IsFalse(process.HasExited);
            return process;
        }

        /// <summary>
        /// Schema property of the config json. This is used for constructing the required config json strings
        /// for unit tests
        /// </summary>
        public const string SCHEMA_PROPERTY = @"
          ""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""";

        /// <summary>
        /// Data source property of the config json. This is used for constructing the required config json strings
        /// for unit tests 
        /// </summary>
        public const string SAMPLE_SCHEMA_DATA_SOURCE = SCHEMA_PROPERTY + "," + @"
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": ""testconnectionstring"",
              ""options"":{
                ""set-session-context"": true
                }
            }
        ";

        /// <summary>
        /// Data source property of the config json with an invalid connection string. This is used for
        /// constructing the required config json strings for unit tests. Config json constructed using
        /// this data source element will fail validations as empty connection string
        /// is not allowed
        /// </summary>
        public const string SAMPLE_SCHEMA_DATA_SOURCE_WITH_INVALID_CONNSTRING = SCHEMA_PROPERTY + "," + @"
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": """"
            }
        ";

        /// <summary>
        /// A minimal valid config json without any entities. This config string is used in unit tests.
        /// </summary>
        public const string INITIAL_CONFIG =
          "{" +
            SAMPLE_SCHEMA_DATA_SOURCE + "," +
            @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
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
            ""entities"": {}" +
          "}";

        /// <summary>
        /// A minimal config json without any entities. This config is invalid as it contains an empty connection
        /// string. This config is used in tests to verify validation failures.
        /// </summary>
        public const string INVALID_INTIAL_CONFIG = "{" +
            SAMPLE_SCHEMA_DATA_SOURCE_WITH_INVALID_CONNSTRING + "," +
            @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
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
            ""entities"": {}" +
          "}";

        public const string SINGLE_ENTITY = @"
          {
              ""entities"": {
                  ""MyEntity"": {
                  ""source"": ""MyTable"",
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                          ""delete""
                      ]
                      }
                  ]
                  }
              }
          }";

        public const string BASIC_ENTITY_WITH_ANONYMOUS_ROLE = @"
          {
              ""entities"": {
                  ""MyEntity"": {
                  ""source"": ""s001.book"",
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                          ""*""
                      ]
                      }
                  ]
                  }
              }
          }";

        public const string SINGLE_ENTITY_WITH_ONLY_READ_PERMISSION = @"
          {
              ""entities"": {
                  ""MyEntity"": {
                  ""source"": ""s001.book"",
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                          ""read""
                      ]
                      }
                  ]
                  }
              }
          }";

        /// <summary>
        /// Entity containing invalid graphQL type
        /// </summary>
        public const string SINGLE_ENTITY_WITH_INVALID_GRAPHQL_TYPE = @"
          {
              ""entities"": {
                  ""MyEntity"": {
                  ""source"": ""s001.book"",
                  ""graphql"": {
                    ""type"" : 123
                  },
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                          ""*""
                      ]
                      }
                  ]
                  }
              }
          }";

        public const string SINGLE_ENTITY_WITH_STORED_PROCEDURE = @"
          {
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book"",
                  ""parameters"": {
                      ""param1"": 123,
                      ""param2"": ""hello"",
                      ""param3"": true
                  }
                },
                ""permissions"": [
                  {
                    ""role"": ""anonymous"",
                    ""actions"": [
                      ""execute""
                    ]
                  }
                ],
                ""rest"": {	
                    ""methods"": [	
                      ""post""	
                    ]	
                  },	
                  ""graphql"": {	
                    ""operation"": ""mutation""	
                      }
                    }
                  }
          }";

        public const string SINGLE_ENTITY_WITH_SOURCE_AS_TABLE = @"
          {
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""table"",
                  ""object"": ""s001.book"",
                  ""key-fields"": [
                      ""id"",
                      ""name""
                  ]
                },
                ""permissions"": [
                  {
                    ""role"": ""anonymous"",
                    ""actions"": [
                      ""*""
                    ]
                  }
                ]
              }
            }
          }";

        public const string SINGLE_ENTITY_WITH_SOURCE_AS_VIEW = @"
          {
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""view"",
                  ""object"": ""s001.book"",
                  ""key-fields"": [
                      ""col1"",
                      ""col2""
                  ]
                },
                ""permissions"": [
                  {
                    ""role"": ""anonymous"",
                    ""actions"": [
                      ""*""
                    ]
                  }
                ]
              }
            }
          }";

        public const string ENTITY_CONFIG_WITH_POLICY = @"
          {
            ""entities"": {
                ""MyEntity"": {
                  ""source"": ""MyTable"",
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                            {
                                ""action"": ""Delete"",
                                ""policy"": {
                                    ""request"": ""@claims.name eq 'dab'"",
                                    ""database"": ""@claims.id eq @item.id""
                                }
                            }
                        ]
                      }
                    ]
                }
            }
        }";

        public const string ENTITY_CONFIG_WITH_ACTION_FIELDS = @"
          {
            ""entities"": {
                ""MyEntity"": {
                  ""source"": ""MyTable"",
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                            {
                                ""action"": ""Delete"",
                                ""fields"": {
                                    ""include"": [ ""*"" ],
                                    ""exclude"": [ ""level"", ""rating"" ]
                                }
                            }
                        ]
                      }
                    ]
                }
            }
        }";

        public const string ENTITY_CONFIG_WITH_POLCIY_AND_ACTION_FIELDS = @"
          {
            ""entities"": {
                ""MyEntity"": {
                  ""source"": ""MyTable"",
                  ""permissions"": [
                      {
                      ""role"": ""anonymous"",
                      ""actions"": [
                            {
                                ""action"": ""Delete"",
                                ""policy"": {
                                    ""request"": ""@claims.name eq 'dab'"",
                                    ""database"": ""@claims.id eq @item.id""
                                },
                                ""fields"": {
                                    ""include"": [ ""*"" ],
                                    ""exclude"": [ ""level"", ""rating"" ]
                                }
                            }
                        ]
                      }
                    ]
                }
            }
        }";

        public const string CONFIG_WITH_SINGLE_ENTITY =
        @"{" +
          @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
          @"""data-source"": {
          ""database-type"": ""mssql"",
          ""connection-string"": ""localhost:5000"",
          ""options"":{
            ""set-session-context"": true
          }
        },
        ""runtime"": {
          ""rest"": {
            ""path"": ""/api""
          },
          ""graphql"": {
            ""path"": ""/graphql"",
            ""allow-introspection"": true
          },
          ""host"": {
            ""mode"": ""production"",
            ""cors"": {
              ""origins"": [],
              ""allow-credentials"": false
            },
            ""authentication"": {
              ""provider"": ""StaticWebApps""
            }
          }
        },
        ""entities"": {
          ""book"": {
            ""source"": ""s001.book"",
            ""permissions"": [
              {
                ""role"": ""anonymous"",
                ""actions"": [
                  ""*""
                ]
              }
            ]
          }
        }
      }";

        /// <summary>
        /// Helper method to create json string for runtime settings
        /// for json comparison in tests.
        /// </summary>
        public static string GetDefaultTestRuntimeSettingString(
            HostModeType hostModeType = HostModeType.Production,
            IEnumerable<string>? corsOrigins = null)
        {
            Dictionary<string, object> runtimeSettingDict = new();
            Dictionary<GlobalSettingsType, object> defaultGlobalSetting = GetDefaultGlobalSettings(
                hostMode: hostModeType,
                corsOrigin: corsOrigins);

            runtimeSettingDict.Add("runtime", defaultGlobalSetting);

            return JsonSerializer.Serialize(runtimeSettingDict, GetSerializationOptions());
        }

        /// <summary>
        /// Helper method to setup Logger factory
        /// for CLI related classes.
        /// </summary>
        public static void SetupTestLoggerForCLI()
        {
            Mock<ILogger<ConfigGenerator>> configGeneratorLogger = new();
            Mock<ILogger<Utils>> utilsLogger = new();
            ConfigGenerator.SetLoggerForCliConfigGenerator(configGeneratorLogger.Object);
            Utils.SetCliUtilsLogger(utilsLogger.Object);
        }
    }
}
