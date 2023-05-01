// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions.TestingHelpers;
using System.IO.Abstractions;
using System.Reflection;
using Snapshooter.MSTest;

namespace Cli.Tests;

/// <summary>
/// End To End Tests for CLI.
/// </summary>
[TestClass]
public class EndToEndTests
{
    private IFileSystem? _fileSystem;
    private RuntimeConfigLoader? _runtimeConfigLoader;
    private ILogger<Program>? _cliLogger;

    [TestInitialize]
    public void TestInitialize()
    {
        MockFileSystem fileSystem = new();

        fileSystem.AddFile(
            fileSystem.Path.Combine(
                fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "dab.draft.schema.json"),
            new MockFileData("{ \"additionalProperties\": {\"version\": \"https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json\"} }"));

        fileSystem.AddFile(
            TEST_SCHEMA_FILE,
            new MockFileData(""));

        _fileSystem = fileSystem;

        _runtimeConfigLoader = new RuntimeConfigLoader(_fileSystem);

        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });

        _cliLogger = loggerFactory.CreateLogger<Program>();
        SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _fileSystem = null;
        _runtimeConfigLoader = null;
        _cliLogger = null;
    }
    /// <summary>
    /// Initializing config for CosmosDB_NoSQL.
    /// </summary>
    [TestMethod]
    public void TestInitForCosmosDBNoSql()
    {
        string[] args = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "CosmosDB_NoSQL",
                          "--connection-string", "localhost:5000", "--CosmosDB_NoSQL-database",
                          "graphqldb", "--CosmosDB_NoSQL-container", "planet", "--graphql-schema", TEST_SCHEMA_FILE, "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Execute(args, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        Assert.IsNotNull(runtimeConfig);
        Assert.IsTrue(runtimeConfig.Runtime.GraphQL.AllowIntrospection);
        Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, runtimeConfig.DataSource.DatabaseType);
        CosmosDbNoSQLDataSourceOptions? cosmosDataSourceOptions = runtimeConfig.DataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>();
        Assert.IsNotNull(cosmosDataSourceOptions);
        Assert.AreEqual("graphqldb", cosmosDataSourceOptions.Database);
        Assert.AreEqual("planet", cosmosDataSourceOptions.Container);
        Assert.AreEqual(TEST_SCHEMA_FILE, cosmosDataSourceOptions.GraphQLSchemaPath);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.IsNotNull(runtimeConfig.Runtime.Host);

        HostOptions hostGlobalSettings = runtimeConfig.Runtime.Host;
        CollectionAssert.AreEqual(new string[] { "localhost:3000", "www.nolocalhost.com:80" }, hostGlobalSettings.Cors!.Origins);

        Snapshot.Match(runtimeConfig);
    }

    /// <summary>
    /// Initializing config for cosmosdb_postgresql.
    /// </summary>
    [TestMethod]
    public void TestInitForCosmosDBPostgreSql()
    {
        string[] args = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "cosmosdb_postgresql", "--rest.path", "/rest-api",
                          "--graphql.path", "/graphql-api", "--connection-string", "localhost:5000", "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Execute(args, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(DatabaseType.CosmosDB_PostgreSQL, runtimeConfig.DataSource.DatabaseType);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.AreEqual("/rest-api", runtimeConfig.Runtime.Rest.Path);
        Assert.IsTrue(runtimeConfig.Runtime.Rest.Enabled);
        Assert.AreEqual("/graphql-api", runtimeConfig.Runtime.GraphQL.Path);
        Assert.IsTrue(runtimeConfig.Runtime.GraphQL.Enabled);

        HostOptions hostGlobalSettings = runtimeConfig.Runtime.Host;
        CollectionAssert.AreEqual(new string[] { "localhost:3000", "www.nolocalhost.com:80" }, hostGlobalSettings.Cors!.Origins);
    }

    /// <summary>
    /// Initializing config for REST and GraphQL global settings,
    /// such as custom path and enabling/disabling endpoints.
    /// </summary>
    [TestMethod]
    public void TestInitializingRestAndGraphQLGlobalSettings()
    {
        string[] args = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--rest.path", "/rest-api",
                          "--rest.disabled", "--graphql.path", "/graphql-api" };
        Program.Execute(args, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(DatabaseType.MSSQL, runtimeConfig.DataSource.DatabaseType);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.AreEqual("/rest-api", runtimeConfig.Runtime.Rest.Path);
        Assert.IsFalse(runtimeConfig.Runtime.Rest.Enabled);
        Assert.AreEqual("/graphql-api", runtimeConfig.Runtime.GraphQL.Path);
        Assert.IsTrue(runtimeConfig.Runtime.GraphQL.Enabled);
    }

    /// <summary>
    /// Test to verify adding a new Entity.
    /// </summary>
    [TestMethod]
    public void TestAddEntity()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--host-mode", "development", "--database-type", "mssql", "--connection-string", "localhost:5000", "--auth.provider", "StaticWebApps" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        // Perform assertions on various properties.
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostMode.Development, runtimeConfig.Runtime.Host.Mode);

        string[] addArgs = {"add", "todo", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.todo",
                            "--rest", "todo", "--graphql", "todo", "--permissions", "anonymous:*"};
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? addRuntimeConfig));
        Assert.IsNotNull(addRuntimeConfig);
        Assert.AreEqual(1, addRuntimeConfig.Entities.Count()); // 1 new entity added
        Assert.IsTrue(addRuntimeConfig.Entities.ContainsKey("todo"));
        Entity entity = addRuntimeConfig.Entities["todo"];
        Assert.AreEqual("/todo", entity.Rest.Path);
        Assert.AreEqual("todo", entity.GraphQL.Singular);
        Assert.AreEqual("todos", entity.GraphQL.Plural);
        Assert.AreEqual(1, entity.Permissions.Length);
        Assert.AreEqual("anonymous", entity.Permissions[0].Role);
        Assert.AreEqual(1, entity.Permissions[0].Actions.Length);
        Assert.AreEqual(EntityActionOperation.All, entity.Permissions[0].Actions[0].Action);
    }

    /// <summary>
    /// Test to verify authentication options with init command containing
    /// neither EasyAuth or Simulator as Authentication provider.
    /// It checks correct generation of config with provider, audience and issuer.
    /// </summary>
    [TestMethod]
    public void TestVerifyAuthenticationOptions()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--auth.provider", "AzureAD", "--auth.audience", "aud-xxx", "--auth.issuer", "issuer-xxx" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);

        Assert.AreEqual("AzureAD", runtimeConfig.Runtime.Host.Authentication?.Provider);
        Assert.AreEqual("aud-xxx", runtimeConfig.Runtime.Host.Authentication?.Jwt?.Audience);
        Assert.AreEqual("issuer-xxx", runtimeConfig.Runtime.Host.Authentication?.Jwt?.Issuer);
    }

    /// <summary>
    /// Test to verify that --host-mode is case insensitive.
    /// Short forms are not supported.
    /// </summary>
    [DataTestMethod]
    [DataRow("production", HostMode.Production, true)]
    [DataRow("Production", HostMode.Production, true)]
    [DataRow("development", HostMode.Development, true)]
    [DataRow("Development", HostMode.Development, true)]
    [DataRow("developer", HostMode.Development, false)]
    [DataRow("prod", HostMode.Production, false)]
    public void EnsureHostModeEnumIsCaseInsensitive(string hostMode, HostMode hostModeEnumType, bool expectSuccess)
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--host-mode", hostMode, "--database-type", "mssql", "--connection-string", "localhost:5000" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        _runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig);
        if (expectSuccess)
        {
            Assert.IsNotNull(runtimeConfig);
            Assert.AreEqual(hostModeEnumType, runtimeConfig.Runtime.Host.Mode);
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
    public void TestAddEntityWithoutIEnumerable()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--connection-string", "localhost:5000" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostMode.Production, runtimeConfig.Runtime.Host.Mode);

        string[] addArgs = { "add", "book", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:*" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? addRuntimeConfig));
        Assert.IsNotNull(addRuntimeConfig);
        Assert.AreEqual(1, addRuntimeConfig.Entities.Count()); // 1 new entity added
        Assert.IsTrue(addRuntimeConfig.Entities.ContainsKey("book"));
        Entity entity = addRuntimeConfig.Entities["book"];
        Assert.IsTrue(entity.Rest.Enabled);
        Assert.IsTrue(entity.GraphQL.Enabled);
        Assert.AreEqual(1, entity.Permissions.Length);
        Assert.AreEqual("anonymous", entity.Permissions[0].Role);
        Assert.AreEqual(1, entity.Permissions[0].Actions.Length);
        Assert.AreEqual(EntityActionOperation.All, entity.Permissions[0].Actions[0].Action);
        Assert.IsNull(entity.Mappings);
        Assert.IsNull(entity.Relationships);
    }

    /// <summary>
    /// Test the exact config json generated to verify adding a new Entity without IEnumerable options.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithoutIEnumerables()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--connection-string", "localhost:5000",
            "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "book", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:*" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        Snapshot.Match(updatedRuntimeConfig);
    }

    /// <summary>
    /// Test the exact config json generated to verify adding source as stored-procedure.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithSourceAsStoredProcedure()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", "testconnectionstring", "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        Snapshot.Match(updatedRuntimeConfig);
    }

    /// <summary>
    /// Validate update command for stored procedures by verifying the config json generated
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterUpdatingEntityWithSourceAsStoredProcedure()
    {
        string runtimeConfigJson = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

        _fileSystem!.File.WriteAllText(TEST_RUNTIME_CONFIG_FILE, runtimeConfigJson);

        // args for update command to update the source name from "s001.book" to "dbo.books"
        string[] updateArgs = { "update", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "dbo.books" };
        Program.Execute(updateArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Entity entity = runtimeConfig.Entities["MyEntity"];
        Assert.AreEqual(EntityType.StoredProcedure, entity.Source.Type);
        Assert.AreEqual("dbo.books", entity.Source.Object);
        Assert.IsNotNull(entity.Source.Parameters);
        Assert.AreEqual(3, entity.Source.Parameters.Count);
        Assert.AreEqual(123, entity.Source.Parameters["param1"]);
        Assert.AreEqual("hello", entity.Source.Parameters["param2"]);
        Assert.AreEqual(true, entity.Source.Parameters["param3"]);
    }

    /// <summary>
    /// Validates the config json generated when a stored procedure is added with both 
    /// --rest.methods and --graphql.operation options.
    /// </summary>
    [TestMethod]
    public void TestAddingStoredProcedureWithRestMethodsAndGraphQLOperations()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", "testconnectionstring", "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true", "--rest.methods", "post,put,patch", "--graphql.operation", "query" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        Snapshot.Match(updatedRuntimeConfig);
    }

    /// <summary>
    /// Validates that CLI execution of the add/update commands results in a stored procedure entity
    /// with explicit rest method GET and GraphQL endpoint disabled.
    /// </summary>
    [TestMethod]
    public void TestUpdatingStoredProcedureWithRestMethodsAndGraphQLOperations()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", "testconnectionstring", "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities

        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true", "--rest.methods", "post,put,patch", "--graphql.operation", "query" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

        string[] updateArgs = { "update", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--rest.methods", "get", "--graphql", "false" };
        Program.Execute(updateArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig2));
        Assert.AreNotSame(updatedRuntimeConfig, updatedRuntimeConfig2);
        Snapshot.Match(updatedRuntimeConfig2);
    }

    /// <summary>
    /// Test the exact config json generated to verify adding a new Entity with default source type and given key-fields.
    /// </summary>
    [TestMethod]
    public void TestConfigGeneratedAfterAddingEntityWithSourceWithDefaultType()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--host-mode", "Development",
            "--connection-string", "testconnectionstring", "--set-session-context", "true"  };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:*", "--source.key-fields", "id,name" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        Snapshot.Match(updatedRuntimeConfig);
    }

    /// <summary>
    /// Test to verify updating an existing Entity.
    /// It tests updating permissions as well as relationship
    /// </summary>
    [TestMethod]
    public void TestUpdateEntity()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type",
                              "mssql", "--connection-string", "localhost:5000" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities

        string[] addArgs = {"add", "todo", "-c", TEST_RUNTIME_CONFIG_FILE,
                            "--source", "s001.todo", "--rest", "todo",
                            "--graphql", "todo", "--permissions", "anonymous:*"};
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? addRuntimeConfig));
        Assert.IsNotNull(addRuntimeConfig);
        Assert.AreEqual(1, addRuntimeConfig.Entities.Count()); // 1 new entity added

        // Adding another entity
        //
        string[] addArgs_2 = {"add", "books", "-c", TEST_RUNTIME_CONFIG_FILE,
                            "--source", "s001.books", "--rest", "books",
                            "--graphql", "books", "--permissions", "anonymous:*"};
        Program.Execute(addArgs_2, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? addRuntimeConfig2));
        Assert.IsNotNull(addRuntimeConfig2);
        Assert.AreEqual(2, addRuntimeConfig2.Entities.Count()); // 1 more entity added

        string[] updateArgs = {"update", "todo", "-c", TEST_RUNTIME_CONFIG_FILE,
                                "--source", "s001.todos","--graphql", "true",
                                "--permissions", "anonymous:create,delete",
                                "--fields.include", "id,content", "--fields.exclude", "rating,level",
                                "--relationship", "r1", "--cardinality", "one",
                                "--target.entity", "books", "--relationship.fields", "id:book_id",
                                "--linking.object", "todo_books",
                                "--linking.source.fields", "todo_id",
                                "--linking.target.fields", "id",
                                "--map", "id:identity,name:Company Name"};
        Program.Execute(updateArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updateRuntimeConfig));
        Assert.IsNotNull(updateRuntimeConfig);
        Assert.AreEqual(2, updateRuntimeConfig.Entities.Count()); // No new entity added

        Assert.IsTrue(updateRuntimeConfig.Entities.ContainsKey("todo"));
        Entity entity = updateRuntimeConfig.Entities["todo"];
        Assert.AreEqual("/todo", entity.Rest.Path);
        Assert.IsNotNull(entity.GraphQL);
        Assert.IsTrue(entity.GraphQL.Enabled);
        //The value isn entity.GraphQL is true/false, we expect the serialization to be a string.
        Assert.AreEqual(true, entity.GraphQL.Enabled);
        Assert.AreEqual(1, entity.Permissions.Length);
        Assert.AreEqual("anonymous", entity.Permissions[0].Role);
        Assert.AreEqual(4, entity.Permissions[0].Actions.Length);
        //Only create and delete are updated.
        EntityAction action = entity.Permissions[0].Actions.First(a => a.Action == EntityActionOperation.Create);
        Assert.AreEqual(2, action.Fields?.Include?.Count);
        Assert.AreEqual(2, action.Fields?.Exclude?.Count);
        Assert.IsTrue(action.Fields?.Include?.Contains("id"));
        Assert.IsTrue(action.Fields?.Include?.Contains("content"));
        Assert.IsTrue(action.Fields?.Exclude?.Contains("rating"));
        Assert.IsTrue(action.Fields?.Exclude?.Contains("level"));

        action = entity.Permissions[0].Actions.First(a => a.Action == EntityActionOperation.Delete);
        Assert.AreEqual(2, action.Fields?.Include?.Count);
        Assert.AreEqual(2, action.Fields?.Exclude?.Count);
        Assert.IsTrue(action.Fields?.Include?.Contains("id"));
        Assert.IsTrue(action.Fields?.Include?.Contains("content"));
        Assert.IsTrue(action.Fields?.Exclude?.Contains("rating"));
        Assert.IsTrue(action.Fields?.Exclude?.Contains("level"));

        action = entity.Permissions[0].Actions.First(a => a.Action == EntityActionOperation.Read);
        Assert.IsNull(action.Fields?.Include);
        Assert.IsNull(action.Fields?.Exclude);

        action = entity.Permissions[0].Actions.First(a => a.Action == EntityActionOperation.Update);
        Assert.IsNull(action.Fields?.Include);
        Assert.IsNull(action.Fields?.Exclude);

        Assert.IsTrue(entity.Relationships!.ContainsKey("r1"));
        EntityRelationship relationship = entity.Relationships["r1"];
        Assert.AreEqual(1, entity.Relationships.Count);
        Assert.AreEqual(Cardinality.One, relationship.Cardinality);
        Assert.AreEqual("books", relationship.TargetEntity);
        Assert.AreEqual("todo_books", relationship.LinkingObject);
        CollectionAssert.AreEqual(new string[] { "id" }, relationship.SourceFields);
        CollectionAssert.AreEqual(new string[] { "book_id" }, relationship.TargetFields);
        CollectionAssert.AreEqual(new string[] { "todo_id" }, relationship.LinkingSourceFields);
        CollectionAssert.AreEqual(new string[] { "id" }, relationship.LinkingTargetFields);

        Assert.IsNotNull(entity.Mappings);
        Assert.AreEqual("identity", entity.Mappings["id"]);
        Assert.AreEqual("Company Name", entity.Mappings["name"]);
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
    [DataRow(new string[] { "init", "--database-type", "mssql", "-c", TEST_RUNTIME_CONFIG_FILE }, 0,
        DisplayName = "Correct command with correct options should have exit code 0.")]
    public void VerifyExitCodeForCli(string[] cliArguments, int expectedErrorCode)
    {
        Assert.AreEqual(expectedErrorCode, Program.Execute(cliArguments, _cliLogger!, _fileSystem!, _runtimeConfigLoader!));
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
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql" };
        StringLogger logger = new();
        Program.Execute(initArgs, logger, _fileSystem!, _runtimeConfigLoader!);

        logger = new();
        string[] args = $"{command} {entityName} -c {TEST_RUNTIME_CONFIG_FILE} {flags}".Split(' ');
        Program.Execute(args, logger, _fileSystem!, _runtimeConfigLoader!);

        if (!expectSuccess)
        {
            string output = logger.GetLog();
            StringAssert.Contains(output, $"Entity name is missing. Usage: dab {command} [entity-name] [{command}-options]");
        }
    }
}
