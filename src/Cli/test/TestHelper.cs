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

        public static string InitialConfiguration
        {
            get { return @"{
            ""$schema"": ""dab.draft-01.schema.json"",
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": ""testconnectionstring""
            },
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
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
            ""entities"": {}
          }"; }

        }

        public static string SingleEntity
        {
            get { return @"
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
            }"; }
        }

        public static string BasicEntityWithAnonymousRole
        {
            get { return @"
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
            }"; }
        }

        public static string SingleEntityWithSourceAsStoredProcedure
        {
            get { return @"
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
            }"; }
        }

        public static string SingleEntityWithSourceWithDefaultType
        {
            get { return @"
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
            }"; }
        }

        public static string SingleEntityWithSourceForView
        {
            get { return @"
            {
                ""entities"": {
                ""MyEntity"": {
                  ""source"": {
                    ""type"": ""view"",
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
            }"; }
        }

        public static string EntityConfigurationWithPolicy
        {
            get { return @"
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
            }"; }
        }

        public static string EntityConfigurationWithFields
        {
            get { return @"
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
            }"; }
        }

        public static string EntityConfigurationWithPolicyAndFields
        {
            get { return @"
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
            }"; }
        }

        public static string EntityConfigurationWithPolicyAndFieldsGeneratedWithUpdateCommand
        {
            get { return @"
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
                                        ""include"": [""*""],
                                        ""exclude"": [""level"", ""rating""]
                                    }
                                }
                            ]
                          }
                        ]
                    }
                }
            }"; }

        }

        public static string EntityConfigurationWithPolicyWithUpdateCommand
        {
            get { return @"
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
            }"; }
        }

        public static string EntityConfigurationWithFieldsGeneratedWithUpdateCommand
        {
            get { return @"
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
            }"; }

        }

        public static string CompleteConfigAfterAddingEntity
        {
            get
            {
                return @"
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
            }
        }

        /// <summary>
        /// Helper method to create json string for runtime settings
        /// for json comparison in tests.
        /// </summary>
        public static string GetDefaultTestRuntimeSettingString(
            DatabaseType databaseType,
            HostModeType hostModeType = HostModeType.Production,
            IEnumerable<string>? corsOrigins = null,
            bool? authenticateDevModeRequest = null
        )
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
