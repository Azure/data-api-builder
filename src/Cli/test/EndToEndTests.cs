using Microsoft.Extensions.Logging;
using Moq;

namespace Cli.Tests;

/// <summary>
/// End To End Tests for CLI.
/// </summary>
[TestClass]
public class EndToEndTests
{
    /// <summary>
    /// Initializing config for cosmos DB.
    /// </summary>
    [TestMethod]
    public void TestInitForCosmosDB()
    {
        string[] args = { "init", "-c", _testRuntimeConfig, "--database-type", "cosmos",
                          "--connection-string", "localhost:5000", "--authenticate-devmode-requests", "True", "--cosmos-database",
                          "graphqldb", "--cosmos-container", "planet", "--graphql-schema", "schema.gql", "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Main(args);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        runtimeConfig!.DetermineGlobalSettings();
        runtimeConfig!.DetermineGraphQLEntityNames();

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(DatabaseType.cosmos, runtimeConfig.DatabaseType);
        Assert.IsNotNull(runtimeConfig.CosmosDb);
        Assert.AreEqual("graphqldb", runtimeConfig.CosmosDb.Database);
        Assert.AreEqual("planet", runtimeConfig.CosmosDb.Container);
        Assert.AreEqual("schema.gql", runtimeConfig.CosmosDb.GraphQLSchemaPath);
        Assert.IsNotNull(runtimeConfig.RuntimeSettings);
        Assert.AreEqual(true, runtimeConfig.HostGlobalSettings.IsDevModeDefaultRequestAuthenticated);
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
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--host-mode", "development", "--database-type", "mssql", "--connection-string", "localhost:5000", "--authenticate-devmode-requests", "false" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        runtimeConfig!.DetermineGlobalSettings();
        runtimeConfig!.DetermineGraphQLEntityNames();

        // Perform assertions on various properties.
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostModeType.Development, runtimeConfig.HostGlobalSettings.Mode);
        Assert.AreEqual(false, runtimeConfig.HostGlobalSettings.IsDevModeDefaultRequestAuthenticated);

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
        Console.WriteLine(JsonSerializer.Serialize(entity));
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
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--connection-string", "localhost:5000" };
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
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--host-mode", "Development", "--connection-string", "testconnectionstring" };
        Program.Main(initArgs);
        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", _testRuntimeConfig, "--source", "s001.book", "--permissions", "anonymous:*", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true" };
        Program.Main(addArgs);
        string? actualConfig = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(actualConfig), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
    }

    /// <summary>
    /// Test the exact config json generated to verify adding a new Entity with default source type and given key-fields.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithSourceWithDefaultType()
    {
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--host-mode", "Development", "--connection-string", "testconnectionstring" };
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
        Assert.IsTrue(((JsonElement)entity.GraphQL).Deserialize<bool>());
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

    // <summary>
    // Test to verify the engine gets started using start command
    // </summary>
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
    public void TestStartEngine(string logging)
    {
        Process process = new()
        {
            StartInfo =
                {
                    FileName = @"./dab",
                    Arguments = $"start --config {RuntimeConfigPath.DefaultName} {logging}",
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
        string? output = process.StandardOutput.ReadLine();
        Assert.IsTrue(output!.Contains($"Using config file: {RuntimeConfigPath.DefaultName}"));
        output = process.StandardOutput.ReadLine();
        process.Kill();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains("Starting the runtime engine..."));
    }

    public static RuntimeConfig? TryGetRuntimeConfig(string testRuntimeConfig)
    {
        ILogger logger = new Mock<ILogger>().Object;
        string jsonString;

        if (!TryReadRuntimeConfig(testRuntimeConfig, out jsonString))
        {
            return null;
        }

        RuntimeConfig.TryGetDeserializedConfig(jsonString, out RuntimeConfig? runtimeConfig, logger);

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
