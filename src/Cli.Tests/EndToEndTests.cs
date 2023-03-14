// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// End To End Tests for CLI.
/// </summary>
[TestClass]
public class EndToEndTests
{
    /// <summary>
    /// Setup the logger for CLI
    /// </summary>
    [TestInitialize]
    public void SetupLoggerForCLI()
    {
        TestHelper.SetupTestLoggerForCLI();
    }

    /// <summary>
    /// Initializing config for cosmosdb_nosql.
    /// </summary>
    [TestMethod]
    public void TestInitForCosmosDBNoSql()
    {
        string[] args = { "init", "-c", _testRuntimeConfig, "--database-type", "cosmosdb_nosql",
                          "--connection-string", "localhost:5000", "--cosmosdb_nosql-database",
                          "graphqldb", "--cosmosdb_nosql-container", "planet", "--graphql-schema", "schema.gql", "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Main(args);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        Assert.IsNotNull(runtimeConfig);
        Assert.IsTrue(runtimeConfig.GraphQLGlobalSettings.AllowIntrospection);
        Assert.AreEqual(DatabaseType.cosmosdb_nosql, runtimeConfig.DatabaseType);
        Assert.IsNotNull(runtimeConfig.DataSource.CosmosDbNoSql);
        Assert.AreEqual("graphqldb", runtimeConfig.DataSource.CosmosDbNoSql.Database);
        Assert.AreEqual("planet", runtimeConfig.DataSource.CosmosDbNoSql.Container);
        Assert.AreEqual("schema.gql", runtimeConfig.DataSource.CosmosDbNoSql.GraphQLSchemaPath);
        Assert.IsNotNull(runtimeConfig.RuntimeSettings);
        Assert.IsNotNull(runtimeConfig.HostGlobalSettings);

        Assert.IsTrue(runtimeConfig.RuntimeSettings.ContainsKey(GlobalSettingsType.Host));
        HostGlobalSettings? hostGlobalSettings = JsonSerializer.Deserialize<HostGlobalSettings>((JsonElement)runtimeConfig.RuntimeSettings[GlobalSettingsType.Host], RuntimeConfig.SerializerOptions);
        Assert.IsNotNull(hostGlobalSettings);
        CollectionAssert.AreEqual(new string[] { "localhost:3000", "www.nolocalhost.com:80" }, hostGlobalSettings.Cors!.Origins);
    }

    /// <summary>
    /// Initializing config for cosmosdb_postgresql.
    /// </summary>
    [TestMethod]
    public void TestInitForCosmosDBPostgreSql()
    {
        string[] args = { "init", "-c", _testRuntimeConfig, "--database-type", "cosmosdb_postgresql", "--rest.path", "/rest-api",
                          "--connection-string", "localhost:5000", "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Main(args);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(DatabaseType.cosmosdb_postgresql, runtimeConfig.DatabaseType);
        Assert.IsNull(runtimeConfig.DataSource.CosmosDbPostgreSql);
        Assert.IsNotNull(runtimeConfig.RuntimeSettings);
        Assert.AreEqual("/rest-api", runtimeConfig.RestGlobalSettings!.Path);
        JsonElement jsonRestSettings = (JsonElement)runtimeConfig.RuntimeSettings[GlobalSettingsType.Rest];

        RestGlobalSettings? restGlobalSettings = JsonSerializer.Deserialize<RestGlobalSettings>(jsonRestSettings, RuntimeConfig.SerializerOptions);
        Assert.IsNotNull(restGlobalSettings);
        Assert.IsNotNull(runtimeConfig.HostGlobalSettings);

        Assert.IsTrue(runtimeConfig.RuntimeSettings.ContainsKey(GlobalSettingsType.Host));
        HostGlobalSettings? hostGlobalSettings = JsonSerializer.Deserialize<HostGlobalSettings>((JsonElement)runtimeConfig.RuntimeSettings[GlobalSettingsType.Host], RuntimeConfig.SerializerOptions);
        Assert.IsNotNull(hostGlobalSettings);
        CollectionAssert.AreEqual(new string[] { "localhost:3000", "www.nolocalhost.com:80" }, hostGlobalSettings.Cors!.Origins);
    }

    /// <summary>
    /// Test to verify adding a new Entity.
    /// </summary>
    [TestMethod]
    public void TestAddEntity()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--host-mode", "development", "--database-type", "mssql", "--connection-string", "localhost:5000", "--auth.provider", "StaticWebApps" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        // Perform assertions on various properties.
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostModeType.Development, runtimeConfig.HostGlobalSettings.Mode);

        string[] addArgs = {"add", "todo", "-c", _testRuntimeConfig, "--source", "s001.todo",
                            "--rest", "todo", "--graphql", "todo", "--permissions", "anonymous:*"};
        Program.Main(addArgs);

        runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(1, runtimeConfig.Entities.Count()); // 1 new entity added
        Assert.IsTrue(runtimeConfig.Entities.ContainsKey("todo"));
        Entity entity = runtimeConfig.Entities["todo"];
        Assert.AreEqual("{\"path\":\"/todo\"}", JsonSerializer.Serialize(entity.Rest));
        Assert.AreEqual("{\"type\":{\"singular\":\"todo\",\"plural\":\"todos\"}}", JsonSerializer.Serialize(entity.GraphQL));
        Assert.AreEqual(1, entity.Permissions.Length);
        Assert.AreEqual("anonymous", entity.Permissions[0].Role);
        Assert.AreEqual(1, entity.Permissions[0].Operations.Length);
        Assert.AreEqual(WILDCARD, ((JsonElement)entity.Permissions[0].Operations[0]).GetString());
    }

    /// <summary>
    /// Test to verify authentication options with init command containing
    /// neither EasyAuth or Simulator as Authentication provider.
    /// It checks correct generation of config with provider, audience and issuer.
    /// </summary>
    [TestMethod]
    public void TestVerifyAuthenticationOptions()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql",
            "--auth.provider", "AzureAD", "--auth.audience", "aud-xxx", "--auth.issuer", "issuer-xxx" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Console.WriteLine(JsonSerializer.Serialize(runtimeConfig.HostGlobalSettings.Authentication));
        string expectedAuthenticationJson = @"
            {
                ""Provider"": ""AzureAD"",
                ""Jwt"":
                {
                    ""Audience"": ""aud-xxx"",
                    ""Issuer"": ""issuer-xxx""
                }
            }";

        JObject expectedJson = JObject.Parse(expectedAuthenticationJson);
        JObject actualJson = JObject.Parse(JsonSerializer.Serialize(runtimeConfig.HostGlobalSettings.Authentication));

        Assert.IsTrue(JToken.DeepEquals(expectedJson, actualJson));
    }

    /// <summary>
    /// Test to verify that --host-mode is case insensitive.
    /// Short forms are not supported.
    /// </summary>
    [DataTestMethod]
    [DataRow("production", HostModeType.Production, true)]
    [DataRow("Production", HostModeType.Production, true)]
    [DataRow("development", HostModeType.Development, true)]
    [DataRow("Development", HostModeType.Development, true)]
    [DataRow("developer", HostModeType.Development, false)]
    [DataRow("prod", HostModeType.Production, false)]
    public void EnsureHostModeEnumIsCaseInsensitive(string hostMode, HostModeType hostModeEnumType, bool expectSuccess)
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--host-mode", hostMode, "--database-type", "mssql", "--connection-string", "localhost:5000" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        if (expectSuccess)
        {
            Assert.IsNotNull(runtimeConfig);
            runtimeConfig.DetermineGlobalSettings();
            Assert.AreEqual(hostModeEnumType, runtimeConfig.HostGlobalSettings.Mode);
        }
        else
        {
            Assert.IsNull(runtimeConfig);
        }
    }

    /// <summary>
    /// Test to verify adding a new Entity without IEnumerable options.
    /// </summary>
    [TestMethod]
    public void TestAddEntityWithoutIEnumerables()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--connection-string", "localhost:5000" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostModeType.Production, runtimeConfig.HostGlobalSettings.Mode);

        string[] addArgs = { "add", "book", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:*" };
        Program.Main(addArgs);

        runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(1, runtimeConfig.Entities.Count()); // 1 new entity added
        Assert.IsTrue(runtimeConfig.Entities.ContainsKey("book"));
        Entity entity = runtimeConfig.Entities["book"];
        Assert.IsNull(entity.Rest);
        Assert.IsNull(entity.GraphQL);
        Assert.AreEqual(1, entity.Permissions.Length);
        Assert.AreEqual("anonymous", entity.Permissions[0].Role);
        Assert.AreEqual(1, entity.Permissions[0].Operations.Length);
        Assert.AreEqual(WILDCARD, ((JsonElement)entity.Permissions[0].Operations[0]).GetString());
        Assert.IsNull(entity.Mappings);
        Assert.IsNull(entity.Relationships);
    }

    /// <summary>
    /// Test the exact config json generated to verify adding a new Entity without IEnumerable options.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithoutIEnumerables()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--connection-string", "localhost:5000",
            "--set-session-context", "true" };
        Program.Main(initArgs);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "book", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:*" };
        Program.Main(addArgs);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(CONFIG_WITH_SINGLE_ENTITY), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
    }

    /// <summary>
    /// Test the exact config json generated to verify adding source as stored-procedure.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithSourceAsStoredProcedure()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", "testconnectionstring", "--set-session-context", "true" };
        Program.Main(initArgs);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true" };
        Program.Main(addArgs);
        string? actualConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(actualConfig), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
    }

    /// <summary>
    /// Validate update command for stored procedures by verifying the config json generated
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterUpdatingEntityWithSourceAsStoredProcedure()
    {
        string? runtimeConfigJson = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);
        WriteJsonContentToFile(_testRuntimeConfig, runtimeConfigJson);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        string expectedSourceObject = @"{
            ""type"": ""stored-procedure"",
            ""object"": ""s001.book"",
            ""parameters"": {
                ""param1"": 123,
                ""param2"": ""hello"",
                ""param3"": true
            }
        }";

        string actualSourceObject = JsonSerializer.Serialize(runtimeConfig.Entities["MyEntity"].Source);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedSourceObject), JObject.Parse(actualSourceObject)));

        // args for update command to update the source name from "s001.book" to "dbo.books"
        string[] updateArgs = { "update", "MyEntity", "-c", _testRuntimeConfig, "--source", "dbo.books" };
        Program.Main(updateArgs);
        runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        expectedSourceObject = @"{
            ""type"": ""stored-procedure"",
            ""object"": ""dbo.books"",
            ""parameters"": {
                ""param1"": 123,
                ""param2"": ""hello"",
                ""param3"": true
            }
        }";

        actualSourceObject = JsonSerializer.Serialize(runtimeConfig.Entities["MyEntity"].Source);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedSourceObject), JObject.Parse(actualSourceObject)));
    }

    /// <summary>
    /// Validates the config json generated when a stored procedure is added with both 
    /// --rest.methods and --graphql.operation options.
    /// </summary>
    [TestMethod]
    public void TestAddingStoredProcedureWithRestMethodsAndGraphQLOperations()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", "testconnectionstring", "--set-session-context", "true" };
        Program.Main(initArgs);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true", "--rest.methods", "post,put,patch", "--graphql.operation", "query" };
        Program.Main(addArgs);
        string? expectedConfig = AddPropertiesToJson(INITIAL_CONFIG, STORED_PROCEDURE_WITH_BOTH_REST_METHODS_GRAPHQL_OPERATION);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
    }

    /// <summary>
    /// Validates that CLI execution of the add/update commands results in a stored procedure entity
    /// with explicit rest method GET and GraphQL endpoint disabled.
    /// </summary>
    [TestMethod]
    public void TestUpdatingStoredProcedureWithRestMethodsAndGraphQLOperations()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", "testconnectionstring", "--set-session-context", "true" };
        Program.Main(initArgs);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities

        string[] addArgs = { "add", "MyEntity", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true", "--rest.methods", "post,put,patch", "--graphql.operation", "query" };
        Program.Main(addArgs);
        string? expectedConfig = AddPropertiesToJson(INITIAL_CONFIG, STORED_PROCEDURE_WITH_BOTH_REST_METHODS_GRAPHQL_OPERATION);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));

        string[] updateArgs = { "update", "MyEntity", "-c", _testRuntimeConfig, "--rest.methods", "get", "--graphql", "false" };
        Program.Main(updateArgs);
        expectedConfig = AddPropertiesToJson(INITIAL_CONFIG, STORED_PROCEDURE_WITH_REST_GRAPHQL_CONFIG);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(expectedConfig), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
    }

    /// <summary>
    /// Test the exact config json generated to verify adding a new Entity with default source type and given key-fields.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithSourceWithDefaultType()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--host-mode", "Development",
            "--connection-string", "testconnectionstring", "--set-session-context", "true"  };
        Program.Main(initArgs);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:*", "--source.key-fields", "id,name" };
        Program.Main(addArgs);
        string? actualConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_SOURCE_AS_TABLE);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(actualConfig), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
    }

    /// <summary>
    /// Test to verify updating an existing Entity.
    /// It tests updating permissions as well as relationship
    /// </summary>
    [TestMethod]
    public void TestUpdateEntity()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type",
                              "mssql", "--connection-string", "localhost:5000" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities

        string[] addArgs = {"add", "todo", "-c", _testRuntimeConfig,
                            "--source", "s001.todo", "--rest", "todo",
                            "--graphql", "todo", "--permissions", "anonymous:*"};
        Program.Main(addArgs);

        runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(1, runtimeConfig.Entities.Count()); // 1 new entity added

        // Adding another entity
        //
        string[] addArgs_2 = {"add", "books", "-c", _testRuntimeConfig,
                            "--source", "s001.books", "--rest", "books",
                            "--graphql", "books", "--permissions", "anonymous:*"};
        Program.Main(addArgs_2);

        runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(2, runtimeConfig.Entities.Count()); // 1 more entity added

        string[] updateArgs = {"update", "todo", "-c", _testRuntimeConfig,
                                "--source", "s001.todos","--graphql", "true",
                                "--permissions", "anonymous:create,delete",
                                "--fields.include", "id,content", "--fields.exclude", "rating,level",
                                "--relationship", "r1", "--cardinality", "one",
                                "--target.entity", "books", "--relationship.fields", "id:book_id",
                                "--linking.object", "todo_books",
                                "--linking.source.fields", "todo_id",
                                "--linking.target.fields", "id",
                                "--map", "id:identity,name:Company Name"};
        Program.Main(updateArgs);

        runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(2, runtimeConfig.Entities.Count()); // No new entity added

        Assert.IsTrue(runtimeConfig.Entities.ContainsKey("todo"));
        Entity entity = runtimeConfig.Entities["todo"];
        Assert.AreEqual("{\"path\":\"/todo\"}", JsonSerializer.Serialize(entity.Rest));
        Assert.IsNotNull(entity.GraphQL);
        Assert.IsTrue((System.Boolean)entity.GraphQL);
        //The value isn entity.GraphQL is true/false, we expect the serialization to be a string.
        Assert.AreEqual("true", JsonSerializer.Serialize(entity.GraphQL), ignoreCase: true);
        Assert.AreEqual(1, entity.Permissions.Length);
        Assert.AreEqual("anonymous", entity.Permissions[0].Role);
        Assert.AreEqual(4, entity.Permissions[0].Operations.Length);
        //Only create and delete are updated.
        Assert.AreEqual("{\"action\":\"create\",\"fields\":{\"include\":[\"id\",\"content\"],\"exclude\":[\"rating\",\"level\"]}}", JsonSerializer.Serialize(entity.Permissions[0].Operations[0]), ignoreCase: true);
        Assert.AreEqual("{\"action\":\"delete\",\"fields\":{\"include\":[\"id\",\"content\"],\"exclude\":[\"rating\",\"level\"]}}", JsonSerializer.Serialize(entity.Permissions[0].Operations[1]), ignoreCase: true);
        Assert.AreEqual("\"read\"", JsonSerializer.Serialize(entity.Permissions[0].Operations[2]), ignoreCase: true);
        Assert.AreEqual("\"update\"", JsonSerializer.Serialize(entity.Permissions[0].Operations[3]), ignoreCase: true);

        Assert.IsTrue(entity.Relationships!.ContainsKey("r1"));
        Relationship relationship = entity.Relationships["r1"];
        Assert.AreEqual(1, entity.Relationships.Count());
        Assert.AreEqual(Cardinality.One, relationship.Cardinality);
        Assert.AreEqual("books", relationship.TargetEntity);
        Assert.AreEqual("todo_books", relationship.LinkingObject);
        CollectionAssert.AreEqual(new string[] { "id" }, relationship.SourceFields);
        CollectionAssert.AreEqual(new string[] { "book_id" }, relationship.TargetFields);
        CollectionAssert.AreEqual(new string[] { "todo_id" }, relationship.LinkingSourceFields);
        CollectionAssert.AreEqual(new string[] { "id" }, relationship.LinkingTargetFields);

        Assert.IsNotNull(entity.Mappings);
        Assert.AreEqual("{\"id\":\"identity\",\"name\":\"Company Name\"}", JsonSerializer.Serialize(entity.Mappings));
    }

    /// <summary>
    /// Test to validate that the engine starts successfully when --verbose and --LogLevel
    /// options are used with the start command
    /// This test does not validate whether the engine logs messages at the specified log level
    /// </summary>
    /// <param name="logLevelOption">Log level options</param>
    [DataTestMethod]
    [DataRow("", DisplayName = "No logging from command line.")]
    [DataRow("--verbose", DisplayName = "Verbose logging from command line.")]
    [DataRow("--LogLevel 0", DisplayName = "LogLevel 0 from command line.")]
    [DataRow("--LogLevel 1", DisplayName = "LogLevel 1 from command line.")]
    [DataRow("--LogLevel 2", DisplayName = "LogLevel 2 from command line.")]
    [DataRow("--LogLevel 3", DisplayName = "LogLevel 3 from command line.")]
    [DataRow("--LogLevel 4", DisplayName = "LogLevel 4 from command line.")]
    [DataRow("--LogLevel 5", DisplayName = "LogLevel 5 from command line.")]
    [DataRow("--LogLevel 6", DisplayName = "LogLevel 6 from command line.")]
    [DataRow("--LogLevel Trace", DisplayName = "LogLevel Trace from command line.")]
    [DataRow("--LogLevel Debug", DisplayName = "LogLevel Debug from command line.")]
    [DataRow("--LogLevel Information", DisplayName = "LogLevel Information from command line.")]
    [DataRow("--LogLevel Warning", DisplayName = "LogLevel Warning from command line.")]
    [DataRow("--LogLevel Error", DisplayName = "LogLevel Error from command line.")]
    [DataRow("--LogLevel Critical", DisplayName = "LogLevel Critical from command line.")]
    [DataRow("--LogLevel None", DisplayName = "LogLevel None from command line.")]
    [DataRow("--LogLevel tRace", DisplayName = "Case sensitivity: LogLevel Trace from command line.")]
    [DataRow("--LogLevel DebUG", DisplayName = "Case sensitivity: LogLevel Debug from command line.")]
    [DataRow("--LogLevel information", DisplayName = "Case sensitivity: LogLevel Information from command line.")]
    [DataRow("--LogLevel waRNing", DisplayName = "Case sensitivity: LogLevel Warning from command line.")]
    [DataRow("--LogLevel eRROR", DisplayName = "Case sensitivity: LogLevel Error from command line.")]
    [DataRow("--LogLevel CrItIcal", DisplayName = "Case sensitivity: LogLevel Critical from command line.")]
    [DataRow("--LogLevel NONE", DisplayName = "Case sensitivity: LogLevel None from command line.")]
    public void TestEngineStartUpWithVerboseAndLogLevelOptions(string logLevelOption)
    {
        WriteJsonContentToFile(_testRuntimeConfig, INITIAL_CONFIG);

        using Process process = ExecuteDabCommand(
            command: $"start --config {_testRuntimeConfig}",
            logLevelOption
        );

        string? output = process.StandardOutput.ReadLine();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains($"{Program.PRODUCT_NAME} {GetProductVersion()}"));
        output = process.StandardOutput.ReadLine();
        process.Kill();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains($"User provided config file: {_testRuntimeConfig}"));
    }

    /// <summary>
    /// Test to verify that `--help` and `--version` along with know command/option produce the exit code 0,
    /// while unknown commands/options have exit code -1.
    /// </summary>
    [DataTestMethod]
    [DataRow(new string[] { "--version" }, 0, DisplayName = "Checking version should have exit code 0.")]
    [DataRow(new string[] { "--help" }, 0, DisplayName = "Checking commands with help should have exit code 0.")]
    [DataRow(new string[] { "add", "--help" }, 0, DisplayName = "Checking options with help should have exit code 0.")]
    [DataRow(new string[] { "initialize" }, -1, DisplayName = "Invalid Command should have exit code -1.")]
    [DataRow(new string[] { "init", "--database-name", "mssql" }, -1, DisplayName = "Invalid Options should have exit code -1.")]
    [DataRow(new string[] { "init", "--database-type", "mssql", "-c", "dab-config-test.json" }, 0,
        DisplayName = "Correct command with correct options should have exit code 0.")]
    public void VerifyExitCodeForCli(string[] cliArguments, int expectedErrorCode)
    {
        Assert.AreEqual(Cli.Program.Main(cliArguments), expectedErrorCode);
    }

    /// <summary>
    /// Test to verify that help writer window generates output on the console.
    /// </summary>
    [DataTestMethod]
    [DataRow("", "", new string[] { "ERROR" }, DisplayName = "No flags provided.")]
    [DataRow("initialize", "", new string[] { "ERROR", "Verb 'initialize' is not recognized." }, DisplayName = "Wrong Command provided.")]
    [DataRow("", "--version", new string[] { "Microsoft.DataApiBuilder 1.0.0" }, DisplayName = "Checking version.")]
    [DataRow("", "--help", new string[] { "init", "add", "update", "start" }, DisplayName = "Checking output for --help.")]
    public void TestHelpWriterOutput(string command, string flags, string[] expectedOutputArray)
    {
        using Process process = ExecuteDabCommand(
            command,
            flags
        );

        string? output = process.StandardOutput.ReadToEnd();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains($"{Program.PRODUCT_NAME} {GetProductVersion()}"));

        foreach (string expectedOutput in expectedOutputArray)
        {
            Assert.IsTrue(output.Contains(expectedOutput));
        }

        process.Kill();
    }

    /// <summary>
    /// Test to verify that the version info is logged for both correct/incorrect command,
    /// and that the config name is displayed in the logs.
    /// </summary>
    [DataRow("", "--version", false, DisplayName = "Checking dab version with --version.")]
    [DataRow("", "--help", false, DisplayName = "Checking version through --help option.")]
    [DataRow("edit", "--new-option", false, DisplayName = "Version printed with invalid command edit.")]
    [DataRow("init", "--database-type mssql", true, DisplayName = "Version printed with valid command init.")]
    [DataRow("add", "MyEntity -s my_entity --permissions \"anonymous:*\"", true, DisplayName = "Version printed with valid command add.")]
    [DataRow("update", "MyEntity -s my_entity", true, DisplayName = "Version printed with valid command update.")]
    [DataRow("start", "", true, DisplayName = "Version printed with valid command start.")]
    [DataTestMethod]
    public void TestVersionInfoAndConfigIsCorrectlyDisplayedWithDifferentCommand(
        string command,
        string options,
        bool isParsableDabCommandName)
    {
        WriteJsonContentToFile(_testRuntimeConfig, INITIAL_CONFIG);

        using Process process = ExecuteDabCommand(
            command: $"{command} ",
            flags: $"--config {_testRuntimeConfig} {options}"
        );

        string? output = process.StandardOutput.ReadToEnd();
        Assert.IsNotNull(output);

        // Version Info logged by dab irrespective of commands being parsed correctly.
        Assert.IsTrue(output.Contains($"{Program.PRODUCT_NAME} {GetProductVersion()}"));

        if (isParsableDabCommandName)
        {
            Assert.IsTrue(output.Contains($"{_testRuntimeConfig}"));
        }

        process.Kill();
    }

    /// <summary>
    /// Test to verify that any parsing errors in the config
    /// are caught before starting the engine.
    /// </summary>
    [DataRow(INITIAL_CONFIG, BASIC_ENTITY_WITH_ANONYMOUS_ROLE, true, DisplayName = "Correct Config")]
    [DataRow(INITIAL_CONFIG, SINGLE_ENTITY_WITH_INVALID_GRAPHQL_TYPE, false, DisplayName = "Invalid GraphQL type for entity")]
    [DataTestMethod]
    public void TestExitOfRuntimeEngineWithInvalidConfig(
        string initialConfig,
        string entityDetails,
        bool expectSuccess)
    {
        string runtimeConfigJson = AddPropertiesToJson(initialConfig, entityDetails);
        File.WriteAllText(_testRuntimeConfig, runtimeConfigJson);
        using Process process = ExecuteDabCommand(
            command: "start",
            flags: $"--config {_testRuntimeConfig}"
        );

        string? output = process.StandardOutput.ReadLine();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains($"{Program.PRODUCT_NAME} {GetProductVersion()}"));
        output = process.StandardOutput.ReadLine();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains($"User provided config file: {_testRuntimeConfig}"));
        output = process.StandardOutput.ReadLine();
        Assert.IsNotNull(output);
        if (expectSuccess)
        {
            Assert.IsTrue(output.Contains($"Setting default minimum LogLevel:"));
            output = process.StandardOutput.ReadLine();
            Assert.IsNotNull(output);
            Assert.IsTrue(output.Contains("Starting the runtime engine..."));
        }
        else
        {
            Assert.IsTrue(output.Contains($"Failed to parse the config file: {_testRuntimeConfig}."));
            output = process.StandardOutput.ReadLine();
            Assert.IsNotNull(output);
            Assert.IsTrue(output.Contains($"Failed to start the engine."));
        }

        process.Kill();

    }

    /// <summary>
    /// Test to verify that if entity is not specified in the add/update
    /// command, a custom (more user friendly) message is displayed.
    /// NOTE: Below order of execution is important, changing the order for DataRow might result in test failures.
    /// The below order makes sure entity is added before update.
    /// </summary>
    [DataRow("add", "", "-s my_entity --permissions anonymous:create", false)]
    [DataRow("add", "MyEntity", "-s my_entity --permissions anonymous:create", true)]
    [DataRow("update", "", "-s my_entity --permissions authenticate:*", false)]
    [DataRow("update", "MyEntity", "-s my_entity --permissions authenticate:*", true)]
    [DataTestMethod]
    public void TestMissingEntityFromCommand(
        string command,
        string entityName,
        string flags,
        bool expectSuccess)
    {
        if (!File.Exists(_testRuntimeConfig))
        {
            string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql" };
            Program.Main(initArgs);
        }

        using Process process = ExecuteDabCommand(
            command: $"{command} {entityName}",
            flags: $"-c {_testRuntimeConfig} {flags}"
        );

        string? output = process.StandardOutput.ReadToEnd();
        Assert.IsNotNull(output);
        if (!expectSuccess)
        {
            Assert.IsTrue(output.Contains($"Error: Entity name is missing. Usage: dab {command} [entity-name] [{command}-options]"));
        }

        process.Kill();

    }

    public static RuntimeConfig? TryGetRuntimeConfig(string testRuntimeConfig)
    {
        ILogger logger = new Mock<ILogger>().Object;
        string jsonString;

        if (!TryReadRuntimeConfig(testRuntimeConfig, out jsonString))
        {
            return null;
        }

        RuntimeConfig.TryGetDeserializedRuntimeConfig(jsonString, out RuntimeConfig? runtimeConfig, logger);

        if (runtimeConfig is null)
        {
            Assert.Fail("Config was not generated correctly.");
        }

        return runtimeConfig;
    }

    /// <summary>
    /// Removes the generated configuration file after each test
    /// to avoid file name conflicts on subsequent test runs because the
    /// file is statically named.
    /// </summary>
    [TestCleanup]
    public void CleanUp()
    {
        if (File.Exists(_testRuntimeConfig))
        {
            File.Delete(_testRuntimeConfig);
        }
    }

}
