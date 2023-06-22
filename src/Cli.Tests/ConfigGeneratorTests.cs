// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Config Generation Tests for CLI.
/// </summary>
[TestClass]
public class ConfigGeneratorTests
{
    /// <summary>
    /// Setup the logger for CLI
    /// </summary>
    [ClassInitialize]
    public static void Setup(TestContext context)
    {
        TestHelper.SetupTestLoggerForCLI();
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
        Assert.AreEqual(isConfigFilePresentAlready, File.Exists(TEST_RUNTIME_CONFIG_FILE));

        InitOptions options = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);

        Assert.AreEqual(isConfigGenerationSuccessful, ConfigGenerator.TryGenerateConfig(options));

        if (!isConfigFilePresentAlready)
        {
            Assert.AreEqual(isConfigGenerationSuccessful, File.Exists(TEST_RUNTIME_CONFIG_FILE));
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
        Assert.AreEqual(isConfigFilePresentAlready, File.Exists(configFileName));

        InitOptions options = CreateBasicInitOptionsForMsSqlWithConfig();

        Assert.AreEqual(isConfigGenerationSuccessful, ConfigGenerator.TryGenerateConfig(options));
        if (!isConfigFilePresentAlready)
        {
            Assert.AreEqual(isConfigGenerationSuccessful, File.Exists(configFileName));
        }
    }

    /// <summary>
    /// This method handles the creation and deletion of a configuration file.
    /// </summary>
    private static void HandleConfigFileCreationAndDeletion(string configFilePath, bool configFilePresent)
    {
        if (File.Exists(configFilePath) && !configFilePresent)
        {
            File.Delete(configFilePath);
        }
        else if (!File.Exists(configFilePath) && configFilePresent)
        {
            File.Create(configFilePath).Dispose();
        }
    }

    /// <summary>
    /// Removes the generated configuration test files
    /// to avoid affecting the results of future tests.
    /// </summary>
    [ClassCleanup]
    public static void CleanUp()
    {
        if (File.Exists("dab-config.Test.json"))
        {
            File.Delete("dab-config.Test.json");
        }

        if (File.Exists("dab-config.json"))
        {
            File.Delete("dab-config.json");
        }
    }
}
