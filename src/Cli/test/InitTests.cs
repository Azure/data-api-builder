namespace Cli.Tests
{
    /// <summary>
    /// Test for config file initialization.
    /// </summary>
    [TestClass]
    public class InitTests
    {
        /// <summary>
        /// Test the simple init config for mssql database. PG and MySQL should be similar.
        /// There is no need for a separate test.
        /// </summary>
        [TestMethod]
        public void MssqlDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosDatabase: null,
                cosmosContainer: null,
                graphQLSchemaPath: null,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                config: "outputfile");

            string expectedRuntimeConfig =
            @"{
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
      ""path"": ""/graphql""
    },
    ""host"": {
      ""mode"": ""development"",
      ""cors"": {
        ""origins"": [""http://localhost:3000"", ""http://nolocalhost:80""],
        ""allow-credentials"": false
      },
      ""authentication"": {
        ""provider"": ""StaticWebApps""
      }
    }
  },
  ""entities"": {}
}";

            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Test cosmos db specifc settings like cosmos-database, cosmos-container, cosmos-schema file.
        /// </summary>
        [TestMethod]
        public void CosmosDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmos,
                connectionString: "testconnectionstring",
                cosmosDatabase: "testdb",
                cosmosContainer: "testcontainer",
                graphQLSchemaPath: "schemafile",
                hostMode: HostModeType.Production,
                corsOrigin: null,
                config: "outputfile");

            string expectedRuntimeConfig = @"{
  ""$schema"": ""hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""cosmos"",
    ""connection-string"": ""testconnectionstring""
  },
  ""cosmos"": {
    ""database"": ""testdb"",
    ""container"": ""testcontainer"",
    ""schema"": ""schemafile""
  },
  ""runtime"": {
    ""rest"": {
      ""enabled"": false,
      ""path"": ""/api""
    },
    ""graphql"": {
      ""allow-introspection"": true,
      ""enabled"": true,
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
  ""entities"": {}
}";

            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Verify that if either database or schema file is null or empty, we will get error.
        /// </summary>
        [TestMethod]
        public void VerifyRequiredOptionsForCosmosDatabase()
        {
            // Both database and schema are either null or empty.
            //
            InitOptions options = new(
                databaseType: DatabaseType.cosmos,
                connectionString: "testconnectionstring",
                cosmosDatabase: null,
                cosmosContainer: "testcontainer",
                graphQLSchemaPath: string.Empty,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                config: "outputfile");

            Assert.IsFalse(ConfigGenerator.TryCreateRuntimeConfig(options, out _));

            // Database field is empty.
            //
            options = new(
                databaseType: DatabaseType.cosmos,
                connectionString: "testconnectionstring",
                cosmosDatabase: null,
                cosmosContainer: "testcontainer",
                graphQLSchemaPath: "testschema",
                hostMode: HostModeType.Production,
                corsOrigin: null,
                config: "outputfile");

            Assert.IsFalse(ConfigGenerator.TryCreateRuntimeConfig(options, out _));

            // Schema field is empty.
            //
            options = new(
                databaseType: DatabaseType.cosmos,
                connectionString: "testconnectionstring",
                cosmosDatabase: "testdatabase",
                cosmosContainer: "testcontainer",
                graphQLSchemaPath: string.Empty,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                config: "outputfile");

            Assert.IsFalse(ConfigGenerator.TryCreateRuntimeConfig(options, out _));
        }

        /// <summary>
        /// Call ConfigGenerator.TryCreateRuntimeConfig and verify json result.
        /// </summary>
        /// <param name="options">InitOptions.</param>
        /// <param name="expectedRuntimeConfig">Expected json string output.</param>
        private static void RunTest(InitOptions options, string expectedRuntimeConfig)
        {
            string runtimeConfigJson;
            Assert.IsTrue(ConfigGenerator.TryCreateRuntimeConfig(options, out runtimeConfigJson));

            JObject expectedJson = JObject.Parse(expectedRuntimeConfig);
            JObject actualJson = JObject.Parse(runtimeConfigJson);

            Assert.IsTrue(JToken.DeepEquals(expectedJson, actualJson));
        }
    }
}
