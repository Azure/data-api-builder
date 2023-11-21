// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;
/// <summary>
/// Test for config file initialization.
/// </summary>
[TestClass]
public class ValidateConfigTests
    : VerifyBase
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
    /// this method  validates that the IsConfigValid method returns false when the config is invalid.
    /// </summary>
    [TestMethod]
    public void TestConfigWithCustomPropertyAsInValid()
    {
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, CONFIG_WITH_CUSTOM_PROPERTIES);

        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        bool isConfigValid = ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);
        Assert.IsFalse(isConfigValid);
    }

    /// <summary>
    /// Validates that the IsConfigValid method returns true when the config is valid, i.e No custom proerties.
    /// </summary>
    [TestMethod]
    public void TestConfigWithNoCustomPropertyAsValid()
    {
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, INITIAL_CONFIG);

        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        bool isConfigValid = ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);
        Assert.IsTrue(isConfigValid);
    }

    /// <summary>
    /// Validates that the IsConfigValid method returns true when the config is valid, i.e No custom proerties.
    /// </summary>
    [TestMethod]
    public void TestConfigWithInValidConnectionString()
    {
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, AddPropertiesToJson(INITIAL_CONFIG, SINGLE_ENTITY));

        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        bool isConfigValid = ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);
        Assert.IsFalse(isConfigValid);
    }
}
