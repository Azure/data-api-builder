// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Product;
using Microsoft.Data.SqlClient;
using static Azure.DataApiBuilder.Product.ProductInfo;

namespace Cli.Tests;

/// <summary>
/// End To End Tests for CLI.
/// </summary>
[TestClass]
public class EndToEndTests
    : VerifyBase
{
    private IFileSystem? _fileSystem;
    private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;
    private ILogger<Program>? _cliLogger;

    [TestInitialize]
    public void TestInitialize()
    {
        MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
        fileSystem.AddFile(
            TEST_SCHEMA_FILE,
            new MockFileData(""));

        _fileSystem = fileSystem;

        _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_fileSystem);

        ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

        _cliLogger = loggerFactory.CreateLogger<Program>();
        SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());

        Environment.SetEnvironmentVariable($"connection-string", TEST_CONNECTION_STRING);
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
    public Task TestInitForCosmosDBNoSql()
    {
        string[] args = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "cosmosdb_nosql",
                          "--connection-string", TEST_ENV_CONN_STRING, "--cosmosdb_nosql-database",
                          "graphqldb", "--cosmosdb_nosql-container", "planet", "--graphql-schema", TEST_SCHEMA_FILE, "--cors-origin", "localhost:3000,www.nolocalhost.com:80" };
        Program.Execute(args, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        Assert.IsNotNull(runtimeConfig);
        Assert.IsTrue(runtimeConfig.AllowIntrospection);
        Assert.AreEqual(DatabaseType.CosmosDB_NoSQL, runtimeConfig.DataSource.DatabaseType);
        CosmosDbNoSQLDataSourceOptions? cosmosDataSourceOptions = runtimeConfig.DataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>();
        Assert.IsNotNull(cosmosDataSourceOptions);
        Assert.AreEqual("graphqldb", cosmosDataSourceOptions.Database);
        Assert.AreEqual("planet", cosmosDataSourceOptions.Container);
        Assert.AreEqual(TEST_SCHEMA_FILE, cosmosDataSourceOptions.Schema);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.IsNotNull(runtimeConfig.Runtime.Host);

        HostOptions hostGlobalSettings = runtimeConfig.Runtime.Host;
        CollectionAssert.AreEqual(new string[] { "localhost:3000", "www.nolocalhost.com:80" }, hostGlobalSettings.Cors!.Origins);

        return Verify(runtimeConfig);
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
        Assert.IsNotNull(runtimeConfig.Runtime.Rest);
        Assert.AreEqual("/rest-api", runtimeConfig.Runtime.Rest.Path);
        Assert.IsTrue(runtimeConfig.Runtime.Rest.Enabled);
        Assert.IsNotNull(runtimeConfig.Runtime.GraphQL);
        Assert.AreEqual("/graphql-api", runtimeConfig.Runtime.GraphQL.Path);
        Assert.IsTrue(runtimeConfig.Runtime.GraphQL.Enabled);

        HostOptions? hostGlobalSettings = runtimeConfig.Runtime?.Host;
        Assert.IsNotNull(hostGlobalSettings);
        Assert.IsNotNull(hostGlobalSettings.Cors);
        CollectionAssert.AreEqual(new string[] { "localhost:3000", "www.nolocalhost.com:80" }, hostGlobalSettings.Cors.Origins);
    }

    /// <summary>
    /// Initializing config for REST and GraphQL global settings,
    /// such as custom path and enabling/disabling endpoints.
    /// </summary>
    [TestMethod]
    public void TestInitializingRestAndGraphQLGlobalSettings()
    {
        string[] args = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--connection-string", SAMPLE_TEST_CONN_STRING, "--database-type", "mssql", "--rest.path", "/rest-api", "--rest.enabled", "false", "--graphql.path", "/graphql-api" };
        Program.Execute(args, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(
            TEST_RUNTIME_CONFIG_FILE,
            out RuntimeConfig? runtimeConfig,
            replaceEnvVar: true));

        SqlConnectionStringBuilder builder = new(runtimeConfig.DataSource.ConnectionString);
        Assert.AreEqual(DEFAULT_APP_NAME, builder.ApplicationName);

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(DatabaseType.MSSQL, runtimeConfig.DataSource.DatabaseType);
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.AreEqual("/rest-api", runtimeConfig.Runtime.Rest?.Path);
        Assert.IsFalse(runtimeConfig.Runtime.Rest?.Enabled);
        Assert.AreEqual("/graphql-api", runtimeConfig.Runtime.GraphQL?.Path);
        Assert.IsTrue(runtimeConfig.Runtime.GraphQL?.Enabled);
    }

    /// <summary>
    /// Test to verify adding a new Entity.
    /// </summary>
    [TestMethod]
    public void TestAddEntity()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--host-mode", "development", "--database-type",
            "mssql", "--connection-string", TEST_ENV_CONN_STRING, "--auth.provider", "StaticWebApps" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));

        // Perform assertions on various properties.
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostMode.Development, runtimeConfig.Runtime?.Host?.Mode);

        string[] addArgs = {"add", "todo", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.todo",
                            "--rest", "todo", "--graphql", "todo", "--permissions", "anonymous:*"};
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? addRuntimeConfig));
        Assert.IsNotNull(addRuntimeConfig);
        Assert.AreEqual(TEST_ENV_CONN_STRING, addRuntimeConfig.DataSource.ConnectionString);
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

        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.IsNotNull(runtimeConfig.Runtime.Host);
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
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--host-mode", hostMode, "--database-type", "mssql", "--connection-string", SAMPLE_TEST_CONN_STRING };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        _runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig);
        if (expectSuccess)
        {
            Assert.IsNotNull(runtimeConfig);
            Assert.AreEqual(hostModeEnumType, runtimeConfig.Runtime?.Host?.Mode);
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
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--connection-string", SAMPLE_TEST_CONN_STRING };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig), "Expected to parse the config file.");

        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        Assert.AreEqual(HostMode.Production, runtimeConfig.Runtime?.Host?.Mode);

        string[] addArgs = { "add", "book", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:*" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? addRuntimeConfig));
        Assert.IsNotNull(addRuntimeConfig);
        Assert.AreEqual(1, addRuntimeConfig.Entities.Count()); // 1 new entity added
        Assert.IsTrue(addRuntimeConfig.Entities.ContainsKey("book"));
        Entity entity = addRuntimeConfig.Entities["book"];
        Assert.IsTrue(entity.Rest.Enabled, "REST expected be to enabled");
        Assert.IsTrue(entity.GraphQL.Enabled, "GraphQL expected to be enabled");
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
    public Task TestConfigGeneratedAfterAddingEntityWithoutIEnumerables()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--connection-string", SAMPLE_TEST_CONN_STRING,
            "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "book", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:*" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        return Verify(updatedRuntimeConfig);
    }

    /// <summary>
    /// Test the exact config json generated to verify adding source as stored-procedure.
    /// </summary>
    [TestMethod]
    public Task TestConfigGeneratedAfterAddingEntityWithSourceAsStoredProcedure()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", SAMPLE_TEST_CONN_STRING, "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        return Verify(updatedRuntimeConfig);
    }

    /// <summary>
    /// Validate update command for stored procedures by verifying the config json generated
    /// </summary>
    [TestMethod]
    public Task TestConfigGeneratedAfterUpdatingEntityWithSourceAsStoredProcedure()
    {
        string runtimeConfigJson = AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY_WITH_STORED_PROCEDURE);

        _fileSystem!.File.WriteAllText(TEST_RUNTIME_CONFIG_FILE, runtimeConfigJson);

        // args for update command to update the source name from "s001.book" to "dbo.books"
        string[] updateArgs = { "update", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "dbo.books" };
        _ = Program.Execute(updateArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig), "Failed to load config.");
        Entity entity = runtimeConfig.Entities["MyEntity"];
        return Verify(entity);
    }

    /// <summary>
    /// Validates the config json generated when a stored procedure is added with both
    /// --rest.methods and --graphql.operation options.
    /// </summary>
    [TestMethod]
    public Task TestAddingStoredProcedureWithRestMethodsAndGraphQLOperations()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", SAMPLE_TEST_CONN_STRING, "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true", "--rest.methods", "post,put,patch", "--graphql.operation", "query" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        return Verify(updatedRuntimeConfig);
    }

    /// <summary>
    /// Validates that CLI execution of the add/update commands results in a stored procedure entity
    /// with explicit rest method GET and GraphQL endpoint disabled.
    /// </summary>
    [TestMethod]
    public Task TestUpdatingStoredProcedureWithRestMethodsAndGraphQLOperations()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", SAMPLE_TEST_CONN_STRING, "--set-session-context", "true" };
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
        return Verify(updatedRuntimeConfig2);
    }

    /// <summary>
    /// Test the exact config json generated to verify adding a new Entity with default source type and given key-fields.
    /// </summary>
    [TestMethod]
    public Task TestConfigGeneratedAfterAddingEntityWithSourceWithDefaultType()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--host-mode", "Development",
            "--connection-string", SAMPLE_TEST_CONN_STRING, "--set-session-context", "true"  };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities
        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:*", "--source.key-fields", "id,name" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);
        return Verify(updatedRuntimeConfig);
    }

    /// <summary>
    /// Test to verify updating an existing Entity.
    /// It tests updating permissions as well as relationship
    /// </summary>
    [TestMethod]
    public void TestUpdateEntity()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type",
                              "mssql", "--connection-string", TEST_ENV_CONN_STRING };
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
        Assert.AreEqual(TEST_ENV_CONN_STRING, updateRuntimeConfig.DataSource.ConnectionString);
        Assert.AreEqual(2, updateRuntimeConfig.Entities.Count()); // No new entity added

        Assert.IsTrue(updateRuntimeConfig.Entities.ContainsKey("todo"));
        Entity entity = updateRuntimeConfig.Entities["todo"];
        Assert.AreEqual("/todo", entity.Rest.Path);
        Assert.IsNotNull(entity.GraphQL);
        Assert.IsTrue(entity.GraphQL.Enabled);
        //The value in entity.GraphQL is true/false, we expect the serialization to be a string.
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
    /// Validates the updation of REST Methods for a stored procedure entity
    /// </summary>
    [TestMethod]
    public Task TestUpdatingStoredProcedureWithRestMethods()
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--host-mode", "Development", "--connection-string", SAMPLE_TEST_CONN_STRING, "--set-session-context", "true" };
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig);
        Assert.AreEqual(0, runtimeConfig.Entities.Count()); // No entities

        string[] addArgs = { "add", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--source", "s001.book", "--permissions", "anonymous:execute", "--source.type", "stored-procedure", "--source.params", "param1:123,param2:hello,param3:true", "--rest.methods", "post,put,patch", "--graphql.operation", "query" };
        Program.Execute(addArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig));
        Assert.AreNotSame(runtimeConfig, updatedRuntimeConfig);

        string[] updateArgs = { "update", "MyEntity", "-c", TEST_RUNTIME_CONFIG_FILE, "--rest.methods", "get" };
        Program.Execute(updateArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? updatedRuntimeConfig2));
        Assert.AreNotSame(updatedRuntimeConfig, updatedRuntimeConfig2);
        return Verify(updatedRuntimeConfig2);
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
        _fileSystem!.File.WriteAllText(TEST_RUNTIME_CONFIG_FILE, INITIAL_CONFIG);

        using Process process = ExecuteDabCommand(
            command: $"start --config {TEST_RUNTIME_CONFIG_FILE}",
            logLevelOption
        );

        string? output = process.StandardOutput.ReadLine();
        Assert.IsNotNull(output);
        StringAssert.Contains(output, $"{Program.PRODUCT_NAME} {ProductInfo.GetProductVersion()}", StringComparison.Ordinal);
        output = process.StandardOutput.ReadLine();
        process.Kill();
        Assert.IsNotNull(output);
        StringAssert.Contains(output, $"User provided config file: {TEST_RUNTIME_CONFIG_FILE}", StringComparison.Ordinal);
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
            StringAssert.Contains(output, $"Entity name is missing. Usage: dab {command} [entity-name] [{command}-options]", StringComparison.Ordinal);
        }
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
        StringAssert.Contains(output, $"{Program.PRODUCT_NAME} {ProductInfo.GetProductVersion()}", StringComparison.Ordinal);

        foreach (string expectedOutput in expectedOutputArray)
        {
            StringAssert.Contains(output, expectedOutput, StringComparison.Ordinal);
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
        _fileSystem!.File.WriteAllText(TEST_RUNTIME_CONFIG_FILE, INITIAL_CONFIG);

        using Process process = ExecuteDabCommand(
            command: $"{command} ",
            flags: $"--config {TEST_RUNTIME_CONFIG_FILE} {options}"
        );

        string? output = process.StandardOutput.ReadLine();
        Assert.IsNotNull(output);

        // Version Info logged by dab irrespective of commands being parsed correctly.
        StringAssert.Contains(output, $"{Program.PRODUCT_NAME} {ProductInfo.GetProductVersion()}", StringComparison.Ordinal);

        if (isParsableDabCommandName)
        {
            output = process.StandardOutput.ReadLine();
            StringAssert.Contains(output, TEST_RUNTIME_CONFIG_FILE, StringComparison.Ordinal);
        }

        process.Kill();
    }

    /// <summary>
    /// Test to verify that any parsing errors in the config
    /// are caught before starting the engine.
    /// Ignoring due to deadlocks when attempting to read Standard.Output
    /// and Standard.Error. A fix will come in a follow-up PR.
    /// </summary>
    [DataRow(INITIAL_CONFIG, BASIC_ENTITY_WITH_ANONYMOUS_ROLE, true, DisplayName = "Correct Config")]
    [DataRow(INITIAL_CONFIG, SINGLE_ENTITY_WITH_INVALID_GRAPHQL_TYPE, false, DisplayName = "Invalid GraphQL type for entity")]
    [DataTestMethod, Ignore]
    public async Task TestExitOfRuntimeEngineWithInvalidConfig(
        string initialConfig,
        string entityDetails,
        bool expectSuccess)
    {
        string runtimeConfigJson = AddPropertiesToJson(initialConfig, entityDetails);
        File.WriteAllText(TEST_RUNTIME_CONFIG_FILE, runtimeConfigJson);
        using Process process = ExecuteDabCommand(
            command: "start",
            flags: $"--config {TEST_RUNTIME_CONFIG_FILE}"
        );
        string? output = await process.StandardOutput.ReadLineAsync();
        Assert.IsNotNull(output);
        StringAssert.Contains(output, $"{Program.PRODUCT_NAME} {ProductInfo.GetProductVersion()}", StringComparison.Ordinal);

        output = await process.StandardOutput.ReadLineAsync();
        Assert.IsNotNull(output);
        StringAssert.Contains(output, $"User provided config file: {TEST_RUNTIME_CONFIG_FILE}", StringComparison.Ordinal);

        if (expectSuccess)
        {
            output = await process.StandardOutput.ReadLineAsync();
            Assert.IsNotNull(output);
            StringAssert.Contains(output, $"Found config file: {TEST_RUNTIME_CONFIG_FILE}", StringComparison.Ordinal);

            output = await process.StandardOutput.ReadLineAsync();
            Assert.IsNotNull(output);
            StringAssert.Contains(output, $"Setting default minimum LogLevel:", StringComparison.Ordinal);

            output = await process.StandardOutput.ReadLineAsync();
            Assert.IsNotNull(output);
            StringAssert.Contains(output, "Starting the runtime engine...", StringComparison.Ordinal);
        }
        else
        {
            output = await process.StandardError.ReadLineAsync();
            Assert.IsNotNull(output);
            StringAssert.Contains(output, $"Deserialization of the configuration file failed.", StringComparison.Ordinal);

            output = await process.StandardOutput.ReadLineAsync();
            Assert.IsNotNull(output);
            StringAssert.Contains(output, $"Error: Failed to parse the config file: {TEST_RUNTIME_CONFIG_FILE}.", StringComparison.Ordinal);

            output = await process.StandardOutput.ReadLineAsync();
            Assert.IsNotNull(output);
            StringAssert.Contains(output, $"Failed to start the engine.", StringComparison.Ordinal);
        }

        process.Kill();
    }

    /// <summary>
    /// Test to verify that when base-route is configured, the runtime config is only successfully generated when the
    /// authentication provider is Static Web Apps.
    /// </summary>
    /// <param name="authProvider">Authentication provider specified for the runtime.</param>
    /// <param name="isExceptionExpected">Whether an exception is expected as a result of test run.</param>
    [DataTestMethod]
    [DataRow("StaticWebApps", false)]
    [DataRow("AppService", true)]
    [DataRow("AzureAD", true)]
    public void TestBaseRouteIsConfigurableForSWA(string authProvider, bool isExceptionExpected)
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--host-mode", "development", "--database-type", "mssql",
            "--connection-string", SAMPLE_TEST_CONN_STRING, "--auth.provider", authProvider, "--runtime.base-route", "base-route" };

        if (!Enum.TryParse(authProvider, ignoreCase: true, out EasyAuthType _))
        {
            string[] audIssuers = { "--auth.audience", "aud-xxx", "--auth.issuer", "issuer-xxx" };
            initArgs = initArgs.Concat(audIssuers).ToArray();
        }

        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        if (isExceptionExpected)
        {
            Assert.IsFalse(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
            Assert.IsNull(runtimeConfig);
        }
        else
        {
            Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig);
            Assert.IsNotNull(runtimeConfig.Runtime);
            Assert.AreEqual("/base-route", runtimeConfig.Runtime.BaseRoute);
        }
    }

    [DataTestMethod]
    [DataRow(ApiType.REST, false, false, true, true, DisplayName = "Validate that REST endpoint is enabled when both enabled and disabled options are omitted from the init command.")]
    [DataRow(ApiType.REST, false, true, true, true, DisplayName = "Validate that REST endpoint is enabled when enabled option is set to true and disabled option is omitted from the init command.")]
    [DataRow(ApiType.REST, true, false, true, false, DisplayName = "Validate that REST endpoint is disabled when enabled option is omitted and disabled option is included in the init command.")]
    [DataRow(ApiType.REST, true, true, false, false, DisplayName = "Validate that REST endpoint is disabled when enabled option is set to false and disabled option is included in the init command.")]
    [DataRow(ApiType.REST, true, true, true, true, true, DisplayName = "Validate that config generation fails when enabled and disabled options provide conflicting values for REST endpoint.")]
    [DataRow(ApiType.GraphQL, false, false, true, true, DisplayName = "Validate that GraphQL endpoint is enabled when both enabled and disabled options are omitted from the init command.")]
    [DataRow(ApiType.GraphQL, false, true, true, true, DisplayName = "Validate that GraphQL endpoint is enabled when enabled option is set to true and disabled option is omitted from the init command.")]
    [DataRow(ApiType.GraphQL, true, false, true, false, DisplayName = "Validate that GraphQL endpoint is disabled when enabled option is omitted and disabled option is included in the init command.")]
    [DataRow(ApiType.GraphQL, true, true, false, false, DisplayName = "Validate that GraphQL endpoint is disabled when enabled option is set to false and disabled option is included in the init command.")]
    [DataRow(ApiType.GraphQL, true, true, true, true, true, DisplayName = "Validate that config generation fails when enabled and disabled options provide conflicting values for GraphQL endpoint.")]
    public void TestEnabledDisabledFlagsForApis(
        ApiType apiType,
        bool includeDisabledFlag,
        bool includeEnabledFlag,
        bool enabledFlagValue,
        bool expectedEnabledFlagValueInConfig,
        bool isExceptionExpected = false)
    {
        string apiName = apiType.ToString().ToLower();
        string disabledFlag = $"--{apiName}.disabled";
        string enabledFlag = $"--{apiName}.enabled";

        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--connection-string", SAMPLE_TEST_CONN_STRING };

        string[] enabledDisabledArgs = { };

        if (includeDisabledFlag)
        {
            enabledDisabledArgs = enabledDisabledArgs.Append(disabledFlag).ToArray();
        }

        if (includeEnabledFlag)
        {
            enabledDisabledArgs = enabledDisabledArgs.Append(enabledFlag).ToArray();
            enabledDisabledArgs = enabledDisabledArgs.Append(enabledFlagValue.ToString()).ToArray();
        }

        initArgs = initArgs.Concat(enabledDisabledArgs).ToArray();
        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);
        if (isExceptionExpected)
        {
            Assert.IsFalse(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
            Assert.IsNull(runtimeConfig);
        }
        else
        {
            Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig);

            if (apiType is ApiType.REST)
            {
                Assert.IsNotNull(runtimeConfig.Runtime);
                Assert.IsNotNull(runtimeConfig.Runtime.Rest);
                Assert.AreEqual(expectedEnabledFlagValueInConfig, runtimeConfig.Runtime.Rest.Enabled);
            }
            else
            {
                Assert.IsNotNull(runtimeConfig.Runtime);
                Assert.IsNotNull(runtimeConfig.Runtime.GraphQL);
                Assert.AreEqual(expectedEnabledFlagValueInConfig, runtimeConfig.Runtime.GraphQL.Enabled);
            }
        }
    }

    /// <summary>
    /// Test to validate that whenever the option rest.request-body-strict is included in the init command,
    /// the runtimeconfig is initialized with the appropriate value of the above option in the rest runtime section, as it is assigned in the init command.
    /// When the above mentioned option is not included in the init command, the default behavior - that of not allowing any extraneous fields in request body, is observed.
    /// </summary>
    /// <param name="includeRestRequestBodyStrictFlag">Whether or not to include --rest.request-body-strict option in the init command.</param>
    /// <param name="isRequestBodyStrict">Value of the rest.request-body-strict option in the init command.</param>
    [DataTestMethod]
    [DataRow(true, false, DisplayName = "dab init command specifies --rest.request-body-strict as false - REST request body allows extraneous fields.")]
    [DataRow(true, true, DisplayName = "dab init command specifies --rest.request-body-strict as true - REST request body doesn't allow extraneous fields.")]
    [DataRow(false, true, DisplayName = "dab init command does not include --rest.request-body-strict flag. The default behavior is followed - REST request body doesn't allow extraneous fields.")]
    public void TestRestRequestBodyStrictMode(bool includeRestRequestBodyStrictFlag, bool isRequestBodyStrict)
    {
        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--host-mode", "development", "--database-type", "mssql",
            "--connection-string", SAMPLE_TEST_CONN_STRING};

        if (includeRestRequestBodyStrictFlag)
        {
            string[] restRequestBodyArgs = { "--rest.request-body-strict", isRequestBodyStrict.ToString() };
            initArgs = initArgs.Concat(restRequestBodyArgs).ToArray();
        }

        Program.Execute(initArgs, _cliLogger!, _fileSystem!, _runtimeConfigLoader!);

        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? runtimeConfig));
        Assert.IsNotNull(runtimeConfig.Runtime);
        Assert.IsNotNull(runtimeConfig.Runtime.Rest);
        Assert.AreEqual(isRequestBodyStrict, runtimeConfig.Runtime.Rest.RequestBodyStrict);
    }
}
