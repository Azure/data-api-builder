// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Tests for the auto-config-simulate CLI command.
/// </summary>
[TestClass]
public class AutoSimulateTests
{
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
    /// Tests that the simulate command fails when no config file is present.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentities_NoConfigFile()
    {
        // Arrange
        AutoConfigSimulateOptions options = new();

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsFalse(success);
    }

    /// <summary>
    /// Tests that the simulate command fails when the database type is not MSSQL.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentities_NonMssqlDatabase()
    {
        // Arrange: create a PostgreSQL config
        InitOptions initOptions = new(
            databaseType: DatabaseType.PostgreSQL,
            connectionString: "testconnectionstring",
            cosmosNoSqlDatabase: null,
            cosmosNoSqlContainer: null,
            graphQLSchemaPath: null,
            setSessionContext: false,
            hostMode: HostMode.Development,
            corsOrigin: new List<string>(),
            authenticationProvider: EasyAuthType.AppService.ToString(),
            restRequestBodyStrict: CliBool.True,
            config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoConfigSimulateOptions options = new(config: TEST_RUNTIME_CONFIG_FILE);

        // Act
        bool success = TrySimulateAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsFalse(success);
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
    /// Tests that the simulate command options parse the output path correctly.
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentitiesOptions_OutputPathParsed()
    {
        // Arrange
        string outputPath = "simulation-output.csv";

        // Act
        AutoConfigSimulateOptions options = new(output: outputPath, config: TEST_RUNTIME_CONFIG_FILE);

        // Assert
        Assert.AreEqual(outputPath, options.Output);
        Assert.AreEqual(TEST_RUNTIME_CONFIG_FILE, options.Config);
    }

    /// <summary>
    /// Tests that the simulate command options default output to null (console output).
    /// </summary>
    [TestMethod]
    public void TestSimulateAutoentitiesOptions_DefaultOutputIsNull()
    {
        // Arrange & Act
        AutoConfigSimulateOptions options = new();

        // Assert
        Assert.IsNull(options.Output);
    }
}
