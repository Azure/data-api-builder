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
                          "--connection-string", "localhost:5000", "--cosmos-database",
                          "graphqldb", "--cosmos-container", "planet", "--graphql-schema", "schema.gql", "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Main(args);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(DatabaseType.cosmos, runtimeConfig.DatabaseType);
        Assert.IsNotNull(runtimeConfig.CosmosDb);
        Assert.AreEqual("graphqldb", runtimeConfig.CosmosDb.Database);
        Assert.AreEqual("planet", runtimeConfig.CosmosDb.Container);
        Assert.AreEqual("schema.gql", runtimeConfig.CosmosDb.GraphQLSchemaPath);
        Assert.IsNotNull(runtimeConfig.RuntimeSettings);
        JsonElement jsonRestSettings = (JsonElement)runtimeConfig.RuntimeSettings[GlobalSettingsType.Rest];

        RestGlobalSettings? restGlobalSettings = JsonSerializer.Deserialize<RestGlobalSettings>(jsonRestSettings, RuntimeConfig.SerializerOptions);
        Assert.IsNotNull(restGlobalSettings);
        Assert.IsFalse(restGlobalSettings.Enabled);
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
        string[] initArgs = { "init", "-c", _testRuntimeConfig, "--database-type", "mssql", "--connection-string", "localhost:5000" };
        Program.Main(initArgs);

        RuntimeConfig? runtimeConfig = TryGetRuntimeConfig(_testRuntimeConfig);

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities

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
    [TestMethod]
    public void TestStartEngine()
    {
        Process process = new()
        {
            StartInfo =
                {
                    FileName = @"./dab",
                    Arguments = $"start --config {RuntimeConfigPath.DefaultName}",
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
        Assert.IsTrue(output.Contains($"Using config file: {RuntimeConfigPath.DefaultName}"));
        output = process.StandardOutput.ReadLine();
        process.Kill();
        Assert.IsNotNull(output);
        Assert.IsTrue(output.Contains("Starting the runtime engine..."));
    }

    public static RuntimeConfig? TryGetRuntimeConfig(string testRuntimeConfig)
    {
        string jsonString;

        if (!TryReadRuntimeConfig(testRuntimeConfig, out jsonString))
        {
            return null;
        }

        RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, RuntimeConfig.SerializerOptions);

        if (runtimeConfig is null)
        {
            Assert.Fail("Config was not generated correctly.");
        }

        return runtimeConfig;
    }

    /// <summary>
    /// Method to clean up the generated test config file
    /// to avoid error while initializing the existing config file
    /// as all the tests are generating config with the same name.
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
