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

    /// <summary>
    /// A fully resolved connection string containing no @env()/@akv() references. It points at a port
    /// nothing listens on with a short timeout, so the tests that reach the query stage fail fast
    /// without requiring a database.
    /// </summary>
    private const string MSSQL_RESOLVED_CONNECTION_STRING =
        "Server=tcp:127.0.0.1,1;Persist Security Info=False;User ID=sa;" +
        "Password=placeholder;TrustServerCertificate=True;Connect Timeout=1;";

    /// <summary>
    /// Name of an environment variable that is deliberately never set, used to produce an
    /// unresolved @env() reference in a connection string.
    /// </summary>
    private const string UNSET_ENV_VAR_NAME = "DAB_TEST_UNSET_CONNECTION_SECRET";

    /// <summary>
    /// The OpenTelemetry environment variables that `dab init` always references from the generated
    /// config. They are normally unset, which is the scenario covered by issue #3791.
    /// </summary>
    private static readonly string[] _openTelemetryEnvVarNames = new[]
    {
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_HEADERS",
        "OTEL_SERVICE_NAME"
    };

    /// <summary>
    /// Every environment variable these tests unset. The OpenTelemetry names are the real ones an
    /// init-generated config references, so they may legitimately be set in the host environment.
    /// </summary>
    private static readonly string[] _mutatedEnvVarNames =
        _openTelemetryEnvVarNames.Append(UNSET_ENV_VAR_NAME).ToArray();

    private IFileSystem? _fileSystem;
    private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;

    /// <summary>
    /// Host values of <see cref="_mutatedEnvVarNames"/>, captured before each test clears them and
    /// restored in cleanup. Without this, a cleared variable leaks into every test that runs later in
    /// the same process, making unrelated tests fail depending on ordering and host environment.
    /// </summary>
    private readonly Dictionary<string, string?> _originalEnvVarValues = new();

    [TestInitialize]
    public void TestInitialize()
    {
        foreach (string name in _mutatedEnvVarNames)
        {
            _originalEnvVarValues[name] = Environment.GetEnvironmentVariable(name);
        }

        _fileSystem = FileSystemUtils.ProvisionMockFileSystem();
        // isCliLoader mirrors how the CLI builds its loader. Without it a successful load starts a
        // hot-reload file watcher against the mock file system, whose retries add seconds per test.
        _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_fileSystem, isCliLoader: true);

        ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();
        ConfigGenerator.SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (KeyValuePair<string, string?> original in _originalEnvVarValues)
        {
            Environment.SetEnvironmentVariable(original.Key, original.Value);
        }

        _originalEnvVarValues.Clear();
        _fileSystem = null;
        _runtimeConfigLoader = null;
    }

    /// <summary>
    /// Tests that the simulate command fails when no autoentities are defined in the config.
    /// The config is produced by `dab init`, which always writes unset OpenTelemetry @env()
    /// placeholders, so asserting on the specific error also proves the command reached the
    /// autoentities check rather than aborting during the config load.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentities_NoAutoentitiesDefined()
    {
        // Arrange: create an MSSQL config without autoentities
        ClearOpenTelemetryEnvironmentVariables();
        InitOptions initOptions = CreateInitOptionsForMsSql(MSSQL_RESOLVED_CONNECTION_STRING);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        Mock<ILogger<ConfigGenerator>> loggerMock = new();
        SetLoggerForCliConfigGenerator(loggerMock.Object);

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsFalse(success);
        AssertErrorLogged(loggerMock, "No autoentities definitions found in the config file.");
    }

    /// <summary>
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3791.
    /// A config generated by `dab init` references OpenTelemetry environment variables that are
    /// normally unset. Those unresolved @env() references must not abort the config load, so the
    /// command proceeds all the way to the database query stage.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentities_UnsetTelemetryEnvVars_DoesNotBlockConfigLoad()
    {
        // Arrange: an init-generated config (unset OpenTelemetry @env() placeholders) with an autoentity.
        ClearOpenTelemetryEnvironmentVariables();
        InitOptions initOptions = CreateInitOptionsForMsSql(MSSQL_RESOLVED_CONNECTION_STRING);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigOptions autoConfigOptions = new(
            definitionName: "books-filter",
            patternsInclude: new[] { "dbo.books" },
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(autoConfigOptions, _runtimeConfigLoader!, _fileSystem!));

        Mock<ILogger<ConfigGenerator>> loggerMock = new();
        SetLoggerForCliConfigGenerator(loggerMock.Object);

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert: the run fails only because no database is listening, which means the config load,
        // the database type check, the autoentities check and the connection string checks all passed.
        Assert.IsFalse(success, "No database is listening, so the simulation cannot succeed.");
        AssertErrorLogged(loggerMock, "Failed to query the database");
        AssertErrorNotLogged(loggerMock, "Failed to read the config file");
        AssertErrorNotLogged(loggerMock, "No autoentities definitions found");
    }

    /// <summary>
    /// Tests that an @env() reference which could not be resolved is rejected with an actionable
    /// message instead of being sent to the database as a literal. Unresolved references survive the
    /// config load because it runs in Ignore mode, so this check is what catches them.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentities_UnresolvedEnvVarInConnectionString_Fails()
    {
        // Arrange: a config whose connection string references an environment variable that is not set.
        ClearOpenTelemetryEnvironmentVariables();
        Environment.SetEnvironmentVariable(UNSET_ENV_VAR_NAME, null);

        InitOptions initOptions = CreateInitOptionsForMsSql(
            "Server=tcp:127.0.0.1,1;User ID=sa;Password=@env('" + UNSET_ENV_VAR_NAME + "');Connect Timeout=1;");
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigOptions autoConfigOptions = new(
            definitionName: "books-filter",
            patternsInclude: new[] { "dbo.books" },
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(autoConfigOptions, _runtimeConfigLoader!, _fileSystem!));

        Mock<ILogger<ConfigGenerator>> loggerMock = new();
        SetLoggerForCliConfigGenerator(loggerMock.Object);

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsFalse(success);
        AssertErrorLogged(loggerMock, "unresolved @env() or @akv() reference");
        AssertErrorNotLogged(loggerMock, "Failed to query the database");
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

    /// <summary>
    /// Creates the init options used to generate an MSSQL config with the given connection string.
    /// </summary>
    /// <param name="connectionString">The connection string written to the generated config.</param>
    private static InitOptions CreateInitOptionsForMsSql(string connectionString)
    {
        return new(
            databaseType: DatabaseType.MSSQL,
            connectionString: connectionString,
            cosmosNoSqlDatabase: null,
            cosmosNoSqlContainer: null,
            graphQLSchemaPath: null,
            setSessionContext: false,
            hostMode: HostMode.Development,
            corsOrigin: new List<string>(),
            authenticationProvider: EasyAuthType.AppService.ToString(),
            config: TEST_RUNTIME_CONFIG_FILE);
    }

    /// <summary>
    /// Unsets the OpenTelemetry environment variables referenced by an init-generated config so the
    /// tests deterministically exercise the unresolved @env() scenario.
    /// </summary>
    private static void ClearOpenTelemetryEnvironmentVariables()
    {
        foreach (string name in _openTelemetryEnvVarNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>
    /// Asserts that an error containing the given fragment was logged exactly once.
    /// </summary>
    /// <param name="loggerMock">The mocked logger the command wrote to.</param>
    /// <param name="expectedMessageFragment">Fragment expected in the logged error message.</param>
    private static void AssertErrorLogged(Mock<ILogger<ConfigGenerator>> loggerMock, string expectedMessageFragment)
    {
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(expectedMessageFragment)),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once,
            $"Expected an error containing '{expectedMessageFragment}' to be logged.");
    }

    /// <summary>
    /// Asserts that no error containing the given fragment was logged.
    /// </summary>
    /// <param name="loggerMock">The mocked logger the command wrote to.</param>
    /// <param name="unexpectedMessageFragment">Fragment that must not appear in any logged error.</param>
    private static void AssertErrorNotLogged(Mock<ILogger<ConfigGenerator>> loggerMock, string unexpectedMessageFragment)
    {
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(unexpectedMessageFragment)),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Never,
            $"Did not expect an error containing '{unexpectedMessageFragment}' to be logged.");
    }
}
