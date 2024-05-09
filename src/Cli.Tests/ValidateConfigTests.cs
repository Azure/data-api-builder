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
    /// This method validates that the IsConfigValid method returns false when the config is invalid.
    /// </summary>
    [TestMethod]
    public void TestConfigWithCustomPropertyAsInvalid()
    {
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, CONFIG_WITH_CUSTOM_PROPERTIES);

        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        bool isConfigValid = ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);
        Assert.IsFalse(isConfigValid);
    }

    /// <summary>
    /// This method verifies that the relationship validation does not cause unhandled
    /// exceptions, and that the errors generated include the expected messaging.
    /// This case is a regression test due to the metadata needed not always being
    /// populated in the SqlMetadataProvider if for example a bad connection string
    /// is given.
    /// </summary>
    [TestMethod]
    public void TestErrorHandlingForRelationshipValidationWithNonWorkingConnectionString()
    {
        // Arrange
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, COMPLETE_CONFIG_WITH_RELATIONSHIPS_NON_WORKING_CONN_STRING);
        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);
        StringWriter writer = new();
        // Capture console output to get error messaging.
        Console.SetOut(writer);

        // Act
        ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);
        string errorMessage = writer.ToString();

        // Assert
        Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE));
    }

    /// <summary>
    /// Validates that the IsConfigValid method returns false when a config is passed with
    /// both rest and graphQL disabled globally.
    /// </summary>
    [TestMethod]
    public void TestConfigWithInvalidConfigProperties()
    {
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, CONFIG_WITH_DISABLED_GLOBAL_REST_GRAPHQL);

        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        bool isConfigValid = ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);
        Assert.IsFalse(isConfigValid);
    }

    /// <summary>
    /// This method validates that the IsConfigValid method returns false when the config is empty.
    /// This is to validate that no exceptions are thrown with validate for failures during config deserialization.
    /// </summary>
    [TestMethod]
    public void TestValidateWithEmptyConfig()
    {
        // create an empty config file
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, string.Empty);

        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        try
        {
            Assert.IsFalse(ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected Exception thrown: {ex.Message}");
        }
    }
}
