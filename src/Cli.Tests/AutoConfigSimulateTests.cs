// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Tests for the auto-config-simulate CLI command.
/// </summary>
[TestClass]
public class AutoConfigSimulateTests
{
    /// <summary>
    /// MSSQL test category constant, matching the value used by Service.Tests to filter integration tests.
    /// Run with: dotnet test --filter "TestCategory=MsSql"
    /// </summary>
    private const string MSSQL_CATEGORY = "MsSql";

    /// <summary>
    /// Connection string template for integration tests.
    /// The @env('MSSQL_SA_PASSWORD') reference is resolved at config load time when
    /// TrySimulateAutoentities calls TryLoadConfig with doReplaceEnvVar: true.
    /// </summary>
    private const string MSSQL_CONNECTION_STRING_TEMPLATE =
        "Server=tcp:127.0.0.1,1433;Persist Security Info=False;User ID=sa;" +
        "Password=@env('MSSQL_SA_PASSWORD');MultipleActiveResultSets=False;Connection Timeout=30;";

    private IFileSystem? _fileSystem;
    private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;

    [TestInitialize]
    public void TestInitialize()
    {
        _fileSystem = FileSystemUtils.ProvisionMockFileSystem();
        _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_fileSystem);

        ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();
        ConfigGenerator.SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _fileSystem = null;
        _runtimeConfigLoader = null;
    }

    /// <summary>
    /// Tests that the simulate command fails when no autoentities are defined in the config.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentities_NoAutoentitiesDefined()
    {
        // Arrange: create an MSSQL config without autoentities
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsFalse(success);
    }

    /// <summary>
    /// Integration test: verifies that an autoentities filter matching a known table (dbo.books)
    /// produces correct console output containing the filter name, entity name, and database object.
    /// Requires a running MSSQL instance with MSSQL_SA_PASSWORD environment variable set.
    /// </summary>
    [TestMethod]
    [TestCategory(MSSQL_CATEGORY)]
    public void TestSimulateAutoentities_WithMatchingFilter_OutputsToConsole()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")))
        {
            Assert.Inconclusive("MSSQL_SA_PASSWORD environment variable not set. Skipping integration test.");
            return;
        }

        // Arrange: create MSSQL config with autoentities filter for dbo.books
        InitOptions initOptions = new(
            databaseType: DatabaseType.MSSQL,
            connectionString: MSSQL_CONNECTION_STRING_TEMPLATE,
            cosmosNoSqlDatabase: null,
            cosmosNoSqlContainer: null,
            graphQLSchemaPath: null,
            setSessionContext: false,
            hostMode: HostMode.Development,
            corsOrigin: new List<string>(),
            authenticationProvider: EasyAuthType.AppService.ToString(),
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigOptions autoConfigOptions = new(
            definitionName: "books-filter",
            patternsInclude: new[] { "dbo.books" },
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(autoConfigOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Capture console output
        TextWriter originalOut = Console.Out;
        using StringWriter consoleOutput = new();
        Console.SetOut(consoleOutput);
        bool success;
        try
        {
            success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = consoleOutput.ToString();

        // Assert
        Assert.IsTrue(success, "Simulation should succeed when the filter matches tables.");
        StringAssert.Contains(output, "books-filter", "Output should contain the filter name.");
        StringAssert.Contains(output, "books", "Output should contain the entity name.");
        StringAssert.Contains(output, "dbo.books", "Output should contain the database object.");
    }

    /// <summary>
    /// Integration test: verifies that an autoentities filter matching a known table (dbo.books)
    /// produces a well-formed CSV file containing the filter name, entity name, and database object.
    /// Requires a running MSSQL instance with MSSQL_SA_PASSWORD environment variable set.
    /// </summary>
    [TestMethod]
    [TestCategory(MSSQL_CATEGORY)]
    public void TestSimulateAutoentities_WithMatchingFilter_WritesToCsvFile()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")))
        {
            Assert.Inconclusive("MSSQL_SA_PASSWORD environment variable not set. Skipping integration test.");
            return;
        }

        // Arrange: create MSSQL config with autoentities filter for dbo.books
        InitOptions initOptions = new(
            databaseType: DatabaseType.MSSQL,
            connectionString: MSSQL_CONNECTION_STRING_TEMPLATE,
            cosmosNoSqlDatabase: null,
            cosmosNoSqlContainer: null,
            graphQLSchemaPath: null,
            setSessionContext: false,
            hostMode: HostMode.Development,
            corsOrigin: new List<string>(),
            authenticationProvider: EasyAuthType.AppService.ToString(),
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigOptions autoConfigOptions = new(
            definitionName: "books-filter",
            patternsInclude: new[] { "dbo.books" },
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(autoConfigOptions, _runtimeConfigLoader!, _fileSystem!));

        string outputCsvPath = "simulation-output.csv";
        AutoConfigSimulateOptions options = new(output: outputCsvPath, config: TEST_RUNTIME_CONFIG_FILE);

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsTrue(success, "Simulation should succeed when the filter matches tables.");
        Assert.IsTrue(_fileSystem!.File.Exists(outputCsvPath), "CSV output file should be created.");
        string csvContent = _fileSystem.File.ReadAllText(outputCsvPath);
        StringAssert.Contains(csvContent, "filter_name,entity_name,database_object", "CSV should have a header row.");
        StringAssert.Contains(csvContent, "books-filter", "CSV should contain the filter name.");
        StringAssert.Contains(csvContent, "books", "CSV should contain the entity name.");
        StringAssert.Contains(csvContent, "dbo.books", "CSV should contain the database object.");
    }

    /// <summary>
    /// Integration test: verifies that an autoentities filter matching no tables returns success
    /// and prints a "(no matches)" message to the console.
    /// Requires a running MSSQL instance with MSSQL_SA_PASSWORD environment variable set.
    /// </summary>
    [TestMethod]
    [TestCategory(MSSQL_CATEGORY)]
    public void TestSimulateAutoentities_WithNonMatchingFilter_OutputsNoMatches()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")))
        {
            Assert.Inconclusive("MSSQL_SA_PASSWORD environment variable not set. Skipping integration test.");
            return;
        }

        // Arrange: create MSSQL config with autoentities filter that matches no tables
        InitOptions initOptions = new(
            databaseType: DatabaseType.MSSQL,
            connectionString: MSSQL_CONNECTION_STRING_TEMPLATE,
            cosmosNoSqlDatabase: null,
            cosmosNoSqlContainer: null,
            graphQLSchemaPath: null,
            setSessionContext: false,
            hostMode: HostMode.Development,
            corsOrigin: new List<string>(),
            authenticationProvider: EasyAuthType.AppService.ToString(),
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigOptions autoConfigOptions = new(
            definitionName: "empty-filter",
            patternsInclude: new[] { "dbo.NonExistentTable99999" },
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(autoConfigOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Capture console output
        TextWriter originalOut = Console.Out;
        using StringWriter consoleOutput = new();
        Console.SetOut(consoleOutput);
        bool success;
        try
        {
            success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        string output = consoleOutput.ToString();

        // Assert
        // Output format is produced by WriteSimulationResultsToConsole:
        // "Filter: <name>", "Matches: <count>", and "(no matches)" when count is 0.
        Assert.IsTrue(success, "Simulation should succeed even when no tables match.");
        StringAssert.Contains(output, "empty-filter", "Output should contain the filter name.");
        StringAssert.Contains(output, "Matches: 0", "Output should show zero matches.");
        StringAssert.Contains(output, "(no matches)", "Output should show the 'no matches' message.");
    }
}
