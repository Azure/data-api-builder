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
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(GetCompleteConfigAfterAddingEntity), JObject.Parse(File.ReadAllText(_testRuntimeConfig))));
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
    [DataRow("", true, false, DisplayName = "No logging from command line.")]
    [DataRow("--verbose", true, false, DisplayName = "Verbose logging from command line.")]
    [DataRow("--LogLevel 0", true, false, DisplayName = "LogLevel 0 from command line.")]
    [DataRow("--LogLevel 1", true, false, DisplayName = "LogLevel 1 from command line.")]
    [DataRow("--LogLevel 2", true, false, DisplayName = "LogLevel 2 from command line.")]
    [DataRow("--LogLevel 3", true, false, DisplayName = "LogLevel 3 from command line.")]
    [DataRow("--LogLevel 4", true, false, DisplayName = "LogLevel 4 from command line.")]
    [DataRow("--LogLevel 5", false, true, DisplayName = "LogLevel 5 from command line.")]
    [DataRow("--LogLevel 6", false, true, DisplayName = "LogLevel 6 from command line.")]
    [DataRow("--LogLevel Trace", true, false, DisplayName = "LogLevel Trace from command line.")]
    [DataRow("--LogLevel Debug", true, false, DisplayName = "LogLevel Debug from command line.")]
    [DataRow("--LogLevel Information", true, false, DisplayName = "LogLevel Information from command line.")]
    [DataRow("--LogLevel Warning", true, false, DisplayName = "LogLevel Warning from command line.")]
    [DataRow("--LogLevel Error", true, false, DisplayName = "LogLevel Error from command line.")]
    [DataRow("--LogLevel Critical", true, false, DisplayName = "LogLevel Critical from command line.")]
    [DataRow("--LogLevel None", false, true, DisplayName = "LogLevel None from command line.")]
    [DataRow("--LogLevel tRace", false, true, DisplayName = "Case sensitivity: LogLevel Trace from command line.")]
    [DataRow("--LogLevel DebUG", false, true, DisplayName = "Case sensitivity: LogLevel Debug from command line.")]
    [DataRow("--LogLevel information", false, true, DisplayName = "Case sensitivity: LogLevel Information from command line.")]
    [DataRow("--LogLevel waRNing", false, true, DisplayName = "Case sensitivity: LogLevel Warning from command line.")]
    [DataRow("--LogLevel eRROR", false, true, DisplayName = "Case sensitivity: LogLevel Error from command line.")]
    [DataRow("--LogLevel CrItIcal", false, true, DisplayName = "Case sensitivity: LogLevel Critical from command line.")]
    [DataRow("--LogLevel NONE", false, true, DisplayName = "Case sensitivity: LogLevel None from command line.")]
    public void TestStartEngine(string logging, bool useDefaultConfig, bool expectSuccess)
    {
        string configFileName;
        if (useDefaultConfig)
        {
            // default config contains empty connection-string
            configFileName = RuntimeConfigPath.DefaultName;
        }
        else
        {   
            // config with non-empty connection-string
            configFileName = _testRuntimeConfig;
            WriteJsonContentToFile(configFileName, GetInitialConfiguration);
        }

        Process process = new()
        {
            StartInfo =
                {
                    FileName = @"./dab",
                    Arguments = $"start --config {configFileName} {logging}",
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
        Assert.IsTrue(output!.Contains($"Using config file: {configFileName}"));
        output = process.StandardOutput.ReadLine();
        process.Kill();
        Assert.IsNotNull(output);
        if (expectSuccess)
        {
            Assert.IsTrue(output.Contains("Starting the runtime engine..."));
        }
        else
        {
            Assert.IsTrue(output.Contains("Failed to start the engine."));
        }
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
