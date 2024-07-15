// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.IdentityModel.Tokens;

namespace Cli.Tests
{
    public static class TestHelper
    {
        // Config file name for tests
        public const string TEST_RUNTIME_CONFIG_FILE = "dab-config-test.json";

        public const string TEST_CONNECTION_STRING = "testconnectionstring";
        public const string TEST_ENV_CONN_STRING = "@env('connection-string')";

        public const string SAMPLE_TEST_CONN_STRING = "Data Source=<>;Initial Catalog=<>;User ID=<>;Password=<>;";

        public const string SAMPLE_TEST_PGSQL_CONN_STRING = "Host=<>;Database=<>;username=<>;password=<>";

        // test schema for cosmosDB
        public const string TEST_SCHEMA_FILE = "test-schema.gql";
        public const string DAB_DRAFT_SCHEMA_TEST_PATH = "https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json";

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
        public static Process ExecuteDabCommand(string command, string flags)
        {
            Process process = new()
            {
                StartInfo =
                {
                    FileName = @"./Microsoft.DataApiBuilder",
                    CreateNoWindow = true,
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
              ""connection-string"": """ + SAMPLE_TEST_CONN_STRING + @"""
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
        /// Data source property of the config json with custom property `description`.
        /// This is to test that custom properties are not allowed and fails during schema validation.
        /// </summary>
        public const string SAMPLE_DATA_SOURCE_WITH_CUSTOM_PROPERTIES = @"
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": """ + SAMPLE_TEST_CONN_STRING + @""",
              ""description"": ""This is a sample data source description""
            }
        ";

        public const string RUNTIME_SECTION = @"
          ""runtime"": {
              ""rest"": {
                  ""path"": ""/api"",
                  ""enabled"": true,
                  ""request-body-strict"": true
              },
              ""graphql"": {
                  ""path"": ""/graphql"",
                  ""enabled"": true,
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
          ""entities"": {}";

        /// <summary>
        /// Runtime section containing both rest and graphql disabled.
        /// This is used for validating config to test that exceptions are thrown when both rest and graphql are disabled.
        /// </summary>
        public const string RUNTIME_SECTION_WITH_DISABLED_REST_GRAPHQL = @"
          ""runtime"": {
              ""rest"": {
                  ""path"": ""/api"",
                  ""enabled"": false
              },
              ""graphql"": {
                  ""path"": ""/graphql"",
                  ""enabled"": false,
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
          ""entities"": {}";

        /// <summary>
        /// Configuration with unresolved environment variable references on
        /// properties of various data types (string, enum, bool, int).
        /// </summary>
        public const string CONFIG_ENV_VARS = @"
            {
               ""data-source"": {
              ""database-type"": ""@env('database-type')"",
              ""connection-string"": ""@env('connection-string')""
            },
          ""runtime"": {
              ""rest"": {
                  ""path"": ""/api"",
                  ""enabled"": false
              },
              ""graphql"": {
                  ""path"": ""/graphql"",
                  ""enabled"": true,
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
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book"",
                  ""parameters"": {
                      ""param1"": ""@env('sp_param1_int')"",
                      ""param2"": ""hello"",
                      ""param3"": ""@env('sp_param3_bool')""
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

        /// <summary>
        /// A minimal valid config json without any entities. This config string is used in unit tests.
        /// </summary>
        public const string INITIAL_CONFIG = $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{RUNTIME_SECTION}}}";

        /// <summary>
        /// A minimal config json without any entities. This config is invalid as it contains an empty connection
        /// string. This config is used in tests to verify validation failures.
        /// </summary>
        public const string INVALID_INTIAL_CONFIG = $"{{{SAMPLE_SCHEMA_DATA_SOURCE_WITH_INVALID_CONNSTRING},{RUNTIME_SECTION}}}";

        public const string CONFIG_WITH_CUSTOM_PROPERTIES = $"{{{SAMPLE_DATA_SOURCE_WITH_CUSTOM_PROPERTIES},{RUNTIME_SECTION}}}";

        public const string CONFIG_WITH_DISABLED_GLOBAL_REST_GRAPHQL = $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{RUNTIME_SECTION_WITH_DISABLED_REST_GRAPHQL}}}";

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

        public const string SP_DEFAULT_REST_METHODS_GRAPHQL_OPERATION = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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

        public const string SP_GRAPHQL_ENABLED = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                    ""type"": true,
                    ""operation"": ""mutation""
                      }
                    }
                  }
          }";

        public const string SP_GRAPHQL_CUSTOM_TYPE = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                    ""type"": {
                        ""singular"": ""book"",
                        ""plural"": ""books""
                    },
                        ""operation"": ""mutation""
                      }
                    }
                  }
          }";

        public const string SP_GRAPHQL_ENABLED_WITH_CUSTOM_OPERATION = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                    ""type"": true,
                     ""operation"": ""query""
                      }
                    }
                  }
          }";

        public const string SP_GRAPHQL_ENABLED_WITH_CUSTOM_TYPE_OPERATION = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                    ""type"": {
                      ""singular"": ""book"",
                        ""plural"": ""books""
                    },
                     ""operation"": ""query""
                      }
                    }
                  }
          }";

        public const string SP_REST_GRAPHQL_ENABLED = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"":true,
                  ""methods"": [
                      ""post""
                    ]
                },
                ""graphql"": {
                    ""type"": true,
                     ""operation"": ""mutation""
                      }
                    }
                  }
          }";

        public const string SP_REST_GRAPHQL_DISABLED = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
                },
                ""permissions"": [
                  {
                    ""role"": ""anonymous"",
                    ""actions"": [
                      ""execute""
                    ]
                  }
                ],
                ""rest"": false,
                ""graphql"": false
                }
              }
          }";

        public const string SP_CUSTOM_REST_METHOD_GRAPHQL_OPERATION = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"":true,
                  ""methods"": [
                      ""get""
                    ]
                },
                ""graphql"": {
                    ""type"": true,
                     ""operation"": ""query""
                      }
                    }
                  }
          }";

        public const string SP_CUSTOM_REST_GRAPHQL_ALL = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"":""/book"",
                  ""methods"": [
                      ""post"",
                      ""patch"",
                      ""put""
                    ]
                },
                ""graphql"": {
                    ""type"": {
                      ""singular"":""book"",
                      ""plural"":""books""
                    },
                     ""operation"": ""query""
                      }
                    }
                  }
          }";

        public const string SP_DEFAULT_REST_ENABLED = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"": true,
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

        public const string SP_CUSTOM_REST_PATH = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"": ""/book"",
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

        public const string SP_CUSTOM_REST_METHODS = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                      ""get"",
                      ""post"",
                      ""patch""
                    ]
                },
                ""graphql"": {
                    ""operation"": ""mutation""
                      }
                    }
                  }
          }";

        public const string SP_REST_ENABLED_WITH_CUSTOM_REST_METHODS = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"": true,
                  ""methods"": [
                      ""get"",
                      ""post"",
                      ""patch""
                    ]
                },
                ""graphql"": {
                    ""operation"": ""mutation""
                      }
                    }
                  }
          }";

        public const string SP_CUSTOM_REST_PATH_WITH_CUSTOM_REST_METHODS = @"{
              ""entities"": {
              ""MyEntity"": {
                ""source"": {
                  ""type"": ""stored-procedure"",
                  ""object"": ""s001.book""
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
                  ""path"": ""/book"",
                  ""methods"": [
                      ""get"",
                      ""post"",
                      ""patch""
                    ]
                },
                ""graphql"": {
                    ""operation"": ""mutation""
                      }
                    }
                  }
          }";

        public const string STORED_PROCEDURE_WITH_BOTH_REST_METHODS_GRAPHQL_OPERATION = @"
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
                      ""post"",
                      ""put"",
                      ""patch""
                    ]
                  },
                  ""graphql"": {
                    ""operation"": ""query""
                      }
                    }
                  }
          }";

        public const string STORED_PROCEDURE_WITH_REST_GRAPHQL_CONFIG = @"
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
                      ""get""
                    ]
                  },
                  ""graphql"": false
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

        public const string BASE_CONFIG =
          @"{" +
            @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
            @"""data-source"": {
          ""database-type"": ""mssql"",
          ""connection-string"": """",
          ""options"":{
            ""set-session-context"": true
          }
        },
        ""runtime"": {
          ""rest"": {
            ""path"": ""/api"",
            ""enabled"": true
          },
          ""graphql"": {
            ""path"": ""/graphql"",
            ""enabled"": true,
            ""allow-introspection"": false
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
          },
          ""author"": {
            ""source"": ""s001.authors"",
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

        public const string ENV_BASED_CONFIG =
          @"{" +
            @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
            @"""data-source"": {
          ""database-type"": ""mssql"",
          ""connection-string"": ""localhost:5000;User ID={USER_NAME};Password={USER_PASSWORD};MultipleActiveResultSets=False;""
        },
        ""runtime"": {
          ""host"": {
            ""mode"": ""production"",
            ""cors"": {
              ""origins"": [ ""http://localhost:5000"" ],
              ""allow-credentials"": false
            },
            ""authentication"": {
              ""provider"": ""StaticWebApps""
            }
          }
        },
        ""entities"": {
          ""source"":{
            ""source"": ""src"",
            ""rest"": ""true"",
            ""permissions"": [
              {
                ""role"": ""authenticated"",
                ""actions"": [
                  ""*""
                ]
              }
            ]
          },
          ""book"": {
            ""source"": ""books"",
            ""rest"": ""true"",
            ""permissions"": [
              {
                ""role"": ""authenticated"",
                ""actions"": [
                  ""*""
                ]
              }
            ]
          },
          ""publisher"": {
            ""source"": ""publishers"",
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

        public const string MERGED_CONFIG =
          @"{" +
            @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
            @"""data-source"": {
          ""database-type"": ""mssql"",
          ""connection-string"": ""localhost:5000;User ID={USER_NAME};Password={USER_PASSWORD};MultipleActiveResultSets=False;"",
          ""options"":{
            ""set-session-context"": true
          }
        },
        ""runtime"": {
          ""rest"": {
            ""path"": ""/api"",
            ""enabled"": true
          },
          ""graphql"": {
            ""path"": ""/graphql"",
            ""enabled"": true,
            ""allow-introspection"": false
          },
          ""host"": {
            ""mode"": ""production"",
            ""cors"": {
              ""origins"": [ ""http://localhost:5000"" ],
              ""allow-credentials"": false
            },
            ""authentication"": {
              ""provider"": ""StaticWebApps""
            }
          }
        },
        ""entities"": {
          ""source"":{
            ""source"": ""src"",
            ""rest"": ""true"",
            ""permissions"": [
              {
                ""role"": ""authenticated"",
                ""actions"": [
                  ""*""
                ]
              }
            ]
          },
          ""book"": {
            ""source"": ""books"",
            ""rest"": ""true"",
            ""permissions"": [
              {
                ""role"": ""authenticated"",
                ""actions"": [
                  ""*""
                ]
              }
            ]
          },
          ""author"": {
            ""source"": ""s001.authors"",
            ""permissions"": [
              {
                ""role"": ""anonymous"",
                ""actions"": [
                  ""*""
                ]
              }
            ]
          },
          ""publisher"": {
            ""source"": ""publishers"",
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

        public const string COMPLETE_CONFIG_WITH_RELATIONSHIPS_NON_WORKING_CONN_STRING = @"
        {
  ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""options"": {
      ""set-session-context"": false
    },
    ""connection-string"": ""Server=XXXXX;Persist Security Info=False;User ID=<USERHERE>;Password=<PWD HERE> ;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=5;""
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
        ""allow-credentials"": false
      },
      ""authentication"": {
        ""provider"": ""StaticWebApps""
      }
    }
  },
  ""entities"": {
    ""Publisher"": {
      ""source"": {
        ""object"": ""publishers"",
        ""type"": ""table"",
        ""key-fields"": [ ""id"" ]
      },
      ""graphql"": {
        ""enabled"": true,
        ""type"": {
          ""singular"": ""Publisher"",
          ""plural"": ""Publishers""
        }
      },
      ""rest"": {
        ""enabled"": true
      },
      ""permissions"": [
      ],
      ""relationships"": {
        ""books"": {
          ""cardinality"": ""many"",
          ""target.entity"": ""Book"",
          ""source.fields"": [ ""id"" ],
          ""target.fields"": [ ""publisher_id"" ],
          ""linking.source.fields"": [],
          ""linking.target.fields"": []
        }
      }
    },
    ""Book"": {
      ""source"": {
        ""object"": ""books"",
        ""type"": ""table"",
        ""key-fields"": [ ""id"" ]
      },
      ""graphql"": {
        ""enabled"": true,
        ""type"": {
          ""singular"": ""book"",
          ""plural"": ""books""
        }
      },
      ""rest"": {
        ""enabled"": true
      },
      ""permissions"": [
      ],
      ""mappings"": {
        ""id"": ""id"",
        ""title"": ""title""
      },
      ""relationships"": {
        ""publishers"": {
          ""cardinality"": ""one"",
          ""target.entity"": ""Publisher"",
          ""source.fields"": [ ""publisher_id"" ],
          ""target.fields"": [ ""id"" ],
          ""linking.source.fields"": [],
          ""linking.target.fields"": []
        }
      }
    }
  }
}
";
        /// <summary>
        /// Generates the config json string with the given depth limit in the form of json string.
        /// example: { ""depth-limit"": 10 }
        /// </summary>
        /// <returns></returns>
        public static string GenerateConfigWithGivenDepthLimit(string? depthLimitJson = null)
        {
            string depthLimitSection = depthLimitJson.IsNullOrEmpty() ? string.Empty : ("," + depthLimitJson);

            string runtimeSection = $@"
            ""runtime"": {{
                ""rest"": {{
                    ""path"": ""/api"",
                    ""enabled"": true,
                    ""request-body-strict"": true
                }},
                ""graphql"": {{
                    ""path"": ""/graphql"",
                    ""enabled"": true,
                    ""allow-introspection"": true
                    {depthLimitSection}
                }},
                ""host"": {{
                    ""mode"": ""development"",
                    ""cors"": {{
                        ""origins"": [],
                        ""allow-credentials"": false
                    }},
                    ""authentication"": {{
                        ""provider"": ""StaticWebApps""
                    }}
                }}
            }},
            ""entities"": {{}}";

            return $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{runtimeSection}}}";
        }

        /// <summary>
        /// Creates basic initialization options for MS SQL config.
        /// </summary>
        /// <param name="config">Optional config file name.</param>
        /// <returns>InitOptions</returns>
        public static InitOptions CreateBasicInitOptionsForMsSqlWithConfig(string? config = null)
        {
            return new(
                databaseType: DatabaseType.MSSQL,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: true,
                hostMode: HostMode.Development,
                corsOrigin: new List<string>(),
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restRequestBodyStrict: CliBool.True,
                config: config);
        }
    }
}
