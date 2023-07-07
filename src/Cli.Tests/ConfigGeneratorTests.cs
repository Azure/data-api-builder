// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Config Generation Tests for CLI.
/// </summary>
[TestClass]
public class ConfigGeneratorTests
{
    private IFileSystem? _fileSystem;
    private RuntimeConfigLoader? _runtimeConfigLoader;

    [TestInitialize]
    public void TestInitialize()
    {
        _fileSystem = FileSystemUtils.ProvisionMockFileSystem();

        _runtimeConfigLoader = new RuntimeConfigLoader(_fileSystem);

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
        bool isConfigFilePresentAlready,
        bool isConfigGenerationSuccessful)
    {
        HandleConfigFileCreationAndDeletion(TEST_RUNTIME_CONFIG_FILE, isConfigFilePresentAlready);
        Assert.AreEqual(isConfigFilePresentAlready, _fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));

        InitOptions options = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);

        Assert.AreEqual(isConfigGenerationSuccessful, ConfigGenerator.TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));

        if (!isConfigFilePresentAlready)
        {
            Assert.AreEqual(isConfigGenerationSuccessful, _fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));
        }
    }

    /// <summary>
    /// Tests that environment config file is successfully generated when that file is not already present.
    /// When environment variable is not set, it should generate the default config file.
    /// </summary>
    [DataTestMethod]
    [DataRow(true, false, "Test", "dab-config.Test.json", DisplayName = "Failed to generate the config file when environment config file is present.")]
    [DataRow(false, true, "Test", "dab-config.Test.json", DisplayName = "Successfully generated the config file when environment config file is not present.")]
    [DataRow(false, true, "", "dab-config.json", DisplayName = "Successfully generated the config file when environment config file is not present and environment variable is not set.")]
    public void TryGenerateConfig_UsingEnvironmentVariable(
        bool isConfigFilePresentAlready,
        bool isConfigGenerationSuccessful,
        string environmentValue,
        string configFileName)
    {
        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
        HandleConfigFileCreationAndDeletion(configFileName, isConfigFilePresentAlready);
        Assert.AreEqual(isConfigFilePresentAlready, _fileSystem!.File.Exists(configFileName));

        InitOptions options = CreateBasicInitOptionsForMsSqlWithConfig();

        Assert.AreEqual(isConfigGenerationSuccessful, ConfigGenerator.TryGenerateConfig(options, _runtimeConfigLoader!, _fileSystem!));
        if (!isConfigFilePresentAlready)
        {
            Assert.AreEqual(isConfigGenerationSuccessful, _fileSystem!.File.Exists(configFileName));
        }
    }

    /// <summary>
    /// This method handles the creation and deletion of a configuration file.
    /// </summary>
    private void HandleConfigFileCreationAndDeletion(string configFilePath, bool configFilePresent)
    {
        if (!configFilePresent)
        {
            _fileSystem!.File.Delete(configFilePath);
            // File.Delete(configFilePath);
        }
        else if (configFilePresent)
        {
            _fileSystem!.File.Create(configFilePath).Dispose();
        }
    }
}
