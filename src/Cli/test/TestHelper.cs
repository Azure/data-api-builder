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

    // <summary>
    // Creates a new dab Process with the given command and flags,
    // and returns the console output.
    // </summary>
    public static string GetConsoleOutputOnRunningDabProcessWithCommandAndFlags(string command, string flags)
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
        string? output = process.StandardOutput.ReadToEnd();
        Assert.IsNotNull(output);
        process.Kill();
        return output;
    }

        public static string GetInitialConfiguration
        {
            get { return @"{
            ""$schema"": ""dab.draft-01.schema.json"",
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
                ""allow-introspection"": true,
                ""enabled"": true,
                ""path"": ""/graphql""
              },
              ""host"": {
                ""mode"": ""development"",
                ""cors"": {
                  ""origins"": [],
                  ""allow-credentials"": true
                },
                ""authentication"": {
                  ""provider"": ""StaticWebApps""
                }
              }
            },
            ""entities"": {}
          }"; }

        }

        public static string GetSingleEntity
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

        public static string GetEntityConfigurationWithPolicy
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

        public static string GetEntityConfigurationWithFields
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

        public static string GetEntityConfigurationWithPolicyAndFields
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

        public static string GetEntityConfigurationWithPolicyAndFieldsGeneratedWithUpdateCommand
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

        public static string GetEntityConfigurationWithPolicyWithUpdateCommand
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

        public static string GetEntityConfigurationWithFieldsGeneratedWithUpdateCommand
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

        public static string GetCompleteConfigAfterAddingEntity
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
