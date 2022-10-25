namespace Cli.Tests
{
    public static class TestHelper
    {
        // Config file name for tests
        public static string _testRuntimeConfig = "dab-config-test.json";

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

        public const string SAMPLE_SCHEMA_DATA_SOURCE = @"
          ""$schema"": ""dab.draft-01.schema.json"",
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": ""testconnectionstring""
            }
        ";

        public const string INITIAL_CONFIG =
          "{" +
            SAMPLE_SCHEMA_DATA_SOURCE + "," +
            @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
                ""allow-introspection"": true,
                ""path"": ""/graphql""
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
        /// Config containing authenticate-devmode-request type as string
        /// instead of boolean for Host global settings.
        /// </summary>
        public const string CONFIG_WITH_INVALID_DEVMODE_REQUEST_AUTH_TYPE =
          "{" +
            SAMPLE_SCHEMA_DATA_SOURCE + "," +
            @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
                ""allow-introspection"": true,
                ""path"": ""/graphql""
              },
              ""host"": {
                ""mode"": ""development"",
                ""authenticate-devmode-requests"": ""false"",
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
                      ""*""
                    ]
                  }
                ]
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

        public const string CONFIG_WITH_SINGLE_ENTITY = @"
          {
        ""$schema"": ""dab.draft-01.schema.json"",
        ""data-source"": {
          ""database-type"": ""mssql"",
          ""connection-string"": ""localhost:5000""
        },
        ""runtime"": {
          ""rest"": {
            ""path"": ""/api""
          },
          ""graphql"": {
            ""allow-introspection"": true,
            ""path"": ""/graphql""
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
            DatabaseType databaseType,
            HostModeType hostModeType = HostModeType.Production,
            IEnumerable<string>? corsOrigins = null,
            bool? authenticateDevModeRequest = null)
        {
            Dictionary<string, object> runtimeSettingDict = new();
            Dictionary<GlobalSettingsType, object> defaultGlobalSetting = GetDefaultGlobalSettings(
                hostMode: hostModeType,
                corsOrigin: corsOrigins,
                devModeDefaultAuth: authenticateDevModeRequest);

            runtimeSettingDict.Add("runtime", defaultGlobalSetting);

            return JsonSerializer.Serialize(runtimeSettingDict, GetSerializationOptions());
        }
    }
}
