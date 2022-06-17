namespace Hawaii.Cli.Tests
{
    [TestClass]
    public class AddEntityTest
    {
        /// <summary>
        /// Simple test to add a new entity to json config when there is no existing entity.
        /// By Default an empty collection is generated during initialization
        /// entities: {}
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesEmpty()
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: "anonymous:read,update",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                name: "outputfile");

            string initialConfig =
            @"
{
  ""$schema"": ""hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""testconnectionstring""
  },
  ""mssql"": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""allow-introspection"": true,
      ""enabled"": true,
      ""path"": ""/api/graphql""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [],
        ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": ""EasyAuth"",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuerkey"": """"
        }
      }
    }
  },
  ""entities"": {}
}
";
            string expectedConfig =
            @"
{
  ""$schema"": ""hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""testconnectionstring""
  },
  ""mssql"": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""allow-introspection"": true,
      ""enabled"": true,
      ""path"": ""/api/graphql""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [],
        ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": ""EasyAuth"",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuerkey"": """"
        }
      }
    }
  },
  ""entities"": {
    ""MyEntity"": {
      ""source"": ""MyTable"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [
            ""read"",
            ""update""
          ]
        }
      ]
    }
  }
}
";

            RunTest(options, initialConfig, expectedConfig);
        }

        /// <summary>
        /// Add second entity to a config.
        /// </summary>
        [TestMethod]
        public void AddNewEntityWhenEntitiesNotEmpty()
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: "anonymous:*",
                entity: "SecondEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                name: "outputfile");

            string initialConfig =
            @"
{
  ""$schema"": ""hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""testconnectionstring""
  },
  ""mssql"": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""allow-introspection"": true,
      ""enabled"": true,
      ""path"": ""/api/graphql""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [],
        ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": ""EasyAuth"",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuerkey"": """"
        }
      }
    }
  },
  ""entities"": {
    ""MyEntity"": {
      ""source"": ""MyTable"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [
            ""read"",
            ""update""
          ]
        }
      ]
    }
  }
}
";
            string expectedConfig =
            @"
{
  ""$schema"": ""hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""testconnectionstring""
  },
  ""mssql"": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""allow-introspection"": true,
      ""enabled"": true,
      ""path"": ""/api/graphql""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [],
        ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": ""EasyAuth"",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuerkey"": """"
        }
      }
    }
  },
  ""entities"": {
    ""MyEntity"": {
      ""source"": ""MyTable"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [
            ""read"",
            ""update""
          ]
        }
      ]
    },
    ""SecondEntity"": {
      ""source"": ""MyTable"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [ ""*"" ]
        }
      ]
    }
  }
}
";

            RunTest(options, initialConfig, expectedConfig);
        }

        /// <summary>
        /// Add duplicate entity should fail.
        /// </summary>
        [TestMethod]
        public void AddDuplicateEntity()
        {
            AddOptions options = new(
                source: "MyTable",
                permissions: "anonymous:*",
                entity: "MyEntity",
                restRoute: null,
                graphQLType: null,
                fieldsToInclude: null,
                fieldsToExclude: null,
                relationship: null,
                cardinality: null,
                targetEntity: null,
                linkingObject: null,
                linkingSourceFields: null,
                linkingTargetFields: null,
                mappingFields: null,
                name: "outputfile");

            string initialConfig =
            @"
{
  ""$schema"": ""hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""mssql"",
    ""connection-string"": ""testconnectionstring""
  },
  ""mssql"": {
    ""set-session-context"": true
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": true,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""allow-introspection"": true,
      ""enabled"": true,
      ""path"": ""/api/graphql""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [],
        ""allow-credentials"": true
      },
      ""authentication"": {
        ""provider"": ""EasyAuth"",
        ""jwt"": {
          ""audience"": """",
          ""issuer"": """",
          ""issuerkey"": """"
        }
      }
    }
  },
  ""entities"": {
    ""MyEntity"": {
      ""source"": ""MyTable"",
      ""permissions"": [
        {
          ""role"": ""anonymous"",
          ""actions"": [
            ""read"",
            ""update""
          ]
        }
      ]
    }
  }
}
";

            Assert.IsFalse(ConfigGenerator.TryAddNewEntity(options, ref initialConfig));
        }

        /// <summary>
        /// Call ConfigGenerator.TryAddNewEntity and verify json result.
        /// </summary>
        /// <param name="options">Add options.</param>
        /// <param name="initialConfig">Initial Json configuration.</param>
        /// <param name="expectedConfig">Expected Json output.</param>
        private static void RunTest(AddOptions options, string initialConfig, string expectedConfig)
        {
            Assert.IsTrue(ConfigGenerator.TryAddNewEntity(options, ref initialConfig));

            JObject expectedJson = JObject.Parse(expectedConfig);
            JObject actualJson = JObject.Parse(initialConfig);

            Assert.IsTrue(JToken.DeepEquals(expectedJson, actualJson));
        }
    }
}
