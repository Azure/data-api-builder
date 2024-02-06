// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;

namespace Cli.Tests;

/// <summary>
/// Config Generation Tests for CLI.
/// </summary>
[TestClass]
public class ConfigGeneratorTests
{
    private IFileSystem? _fileSystem;
    private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;

    [TestInitialize]
    public void TestInitialize()
    {
        _fileSystem = FileSystemUtils.ProvisionMockFileSystem();

        _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_fileSystem);

        ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

        SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _fileSystem = null;
        _runtimeConfigLoader = null;
    }

    /// <summary>
    /// Tests that user provided config file is successfully generated when that file is not already present.
    /// </summary>
    [DataTestMethod]
    [DataRow(true, false, DisplayName = "Failed to generate config file when user provided config file is present.")]
    [DataRow(false, true, DisplayName = "Successfully generated config file when user provided config file is not present.")]
    public void TryGenerateConfig_WithUserProvidedConfig(
        bool isConfigFilePresent,
        bool isConfigGenerationSuccessful)
    {
        HandleConfigFileCreationAndDeletion(TEST_RUNTIME_CONFIG_FILE, isConfigFilePresent);
        Assert.AreEqual(isConfigFilePresent, _fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));

        InitOptions options = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);

        // Mocking logger to assert on logs
        Mock<ILogger<ConfigGenerator>> loggerMock = new();
        SetLoggerForCliConfigGenerator(loggerMock.Object);

        Assert.AreEqual(isConfigGenerationSuccessful, TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));

        if (!isConfigFilePresent)
        {
            Assert.AreEqual(isConfigGenerationSuccessful, _fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));
        }
        else
        {
            // Assert on the log message to verify the failure
            loggerMock.Verify(
                x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"{TEST_RUNTIME_CONFIG_FILE} already exists.")),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()
                ),
                Times.Once
            );
        }
    }

    /// <summary>
    /// Tests that environment config file is successfully generated when that file is not already present.
    /// When environment variable is not set, it should generate the default config file.
    /// </summary>
    [DataTestMethod]
    [DataRow(true, false, "Test", "dab-config.Test.json", DisplayName = "Failed to generate the config file when environment config file is present.")]
    [DataRow(false, true, "Test", "dab-config.Test.json", DisplayName = "Successfully generated the config file when environment config file is not present.")]
    [DataRow(false, true, "", "dab-config.json", DisplayName = "Successfully generated the config file when environment config file is not present and environment variable is set as empty.")]
    [DataRow(false, true, null, "dab-config.json", DisplayName = "Successfully generated the config file when environment config file is not present and environment variable is not set.")]
    public void TryGenerateConfig_UsingEnvironmentVariable(
        bool isConfigFilePresent,
        bool isConfigGenerationSuccessful,
        string? environmentValue,
        string configFileName)
    {
        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
        HandleConfigFileCreationAndDeletion(configFileName, isConfigFilePresent);
        Assert.AreEqual(isConfigFilePresent, _fileSystem!.File.Exists(configFileName));

        InitOptions options = CreateBasicInitOptionsForMsSqlWithConfig();

        // Mocking logger to assert on logs
        Mock<ILogger<ConfigGenerator>> loggerMock = new();
        SetLoggerForCliConfigGenerator(loggerMock.Object);

        Assert.AreEqual(isConfigGenerationSuccessful, TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));
        if (!isConfigFilePresent)
        {
            Assert.AreEqual(isConfigGenerationSuccessful, _fileSystem!.File.Exists(configFileName));
        }
        else
        {
            // Assert on the log message to verify the failure
            loggerMock.Verify(
                x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"{configFileName} already exists.")),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()
                ),
                Times.Once
            );
        }
    }

    /// <summary>
    /// Test to verify creation of initial config with special characters
    /// such as [!,@,#,$,%,^,&,*, ,(,)] in connection-string or graphql api
    /// </summary>
    [TestMethod]
    public void TestSpecialCharactersInConnectionString()
    {
        HandleConfigFileCreationAndDeletion(TEST_RUNTIME_CONFIG_FILE, configFilePresent: false);
        InitOptions options = new(
            databaseType: DatabaseType.MSSQL,
            connectionString: "A!string@with#some$special%characters^to&check*proper(serialization).'",
            cosmosNoSqlDatabase: null,
            cosmosNoSqlContainer: null,
            graphQLSchemaPath: null,
            graphQLPath: "/An_",
            setSessionContext: false,
            hostMode: HostMode.Production,
            corsOrigin: null,
            authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
            config: TEST_RUNTIME_CONFIG_FILE);

        StringBuilder expectedRuntimeConfigJson = new(
        @"{" +
            @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
            @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""A!string@with#some$special%characters^to&check*proper(serialization).'"",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
           ""runtime"": {
                ""rest"": {
                  ""enabled"": true,
                  ""path"": ""/api"",
                  ""request-body-strict"": true
                  },
                ""graphql"": {
                  ""enabled"": true,
                  ""path"": ""/An_"",
                  ""allow-introspection"": true
                 },
                ""host"": {
                  ""cors"": {
                    ""origins"": [],
                    ""allow-credentials"": false
                        },
                  ""authentication"": {
                    ""provider"": ""StaticWebApps""
                        },
                  ""mode"": ""production""
                  }
              },
              ""entities"": {}
            }");

        expectedRuntimeConfigJson = expectedRuntimeConfigJson.Replace(" ", string.Empty);
        expectedRuntimeConfigJson = expectedRuntimeConfigJson.Replace("\r\n", string.Empty);
        expectedRuntimeConfigJson = expectedRuntimeConfigJson.Replace("\n", string.Empty);

        Assert.IsTrue(TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));

        StringBuilder actualRuntimeConfigJson = new(_fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE, Encoding.Default));
        // The order of these replacements should be identical to the ones being done with expectedRuntimeConfigJson to ensure that
        // the replacements done are identical.
        actualRuntimeConfigJson = actualRuntimeConfigJson.Replace(" ", string.Empty);
        actualRuntimeConfigJson = actualRuntimeConfigJson.Replace("\r\n", string.Empty);
        actualRuntimeConfigJson = actualRuntimeConfigJson.Replace("\n", string.Empty);

        // Comparing explicit strings here since parsing these into JSON would lose
        // the test scenario of verifying escaped chars are not written to the file system.
        Assert.AreEqual(expectedRuntimeConfigJson.ToString(), actualRuntimeConfigJson.ToString());
    }

    /// <summary>
    /// This method handles the creation and deletion of a configuration file.
    /// </summary>
    private void HandleConfigFileCreationAndDeletion(string configFilePath, bool configFilePresent)
    {
        if (!configFilePresent)
        {
            _fileSystem!.File.Delete(configFilePath);
        }
        else if (configFilePresent)
        {
            _fileSystem!.File.Create(configFilePath).Dispose();
        }
    }
}
