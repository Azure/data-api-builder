// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Serilog;

namespace Cli.Tests;
/// <summary>
/// Test for config file initialization.
/// </summary>
[TestClass]
public class ValidateConfigTests
    : VerifyBase
{
    private MockFileSystem? _fileSystem;
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

        // Clear environment variables set in tests.
        Environment.SetEnvironmentVariable($"connection-string", null);
        Environment.SetEnvironmentVariable($"database-type", null);
        Environment.SetEnvironmentVariable($"sp_param1_int", null);
        Environment.SetEnvironmentVariable($"sp_param2_bool", null);
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

    /// <summary>
    /// This Test is used to verify that the validate command is able to catch invalid values for the depth-limit property.
    /// </summary>
    [DataTestMethod]
    [DataRow("null", true, DisplayName = "Invalid Value: 'null'. Only integer values are allowed.")]
    [DataRow("20", true, DisplayName = "Invalid Value: '20'. Integer values provided as strings are not allowed.")]
    [DataRow(0, false, DisplayName = "Invalid Value: 0. Only values between 1 and 2147483647 are allowed along with -1.")]
    [DataRow(-2, false, DisplayName = "Invalid Value: -2. Negative values are not allowed except -1.")]
    [DataRow(2147483648, false, DisplayName = "Invalid Value: 2147483648. Only values between 1 and 2147483647 are allowed along with -1.")]
    [DataRow("seven", true, DisplayName = "Invalid Value: 'seven'. Only integer values are allowed.")]
    public void TestValidateConfigFailsWithInvalidGraphQLDepthLimit(object? depthLimit, bool isStringValue)
    {
        string depthLimitSection = isStringValue ? $@"""depth-limit"": ""{depthLimit}""" : $@"""depth-limit"": {depthLimit}";

        string jsonData = TestHelper.GenerateConfigWithGivenDepthLimit(depthLimitSection);

        // create an empty config file
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, jsonData);

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

    /// <summary>
    /// This Test is used to verify that DAB fails when the JWT properties are missing for OAuth based providers
    /// </summary>
    [DataTestMethod]
    [DataRow("AzureAD")]
    [DataRow("EntraID")]
    [DataRow("Custom")]
    public void TestMissingJwtProperties(string authScheme)
    {
        string ConfigWithJwtAuthentication = $"{{{SAMPLE_SCHEMA_DATA_SOURCE}, {RUNTIME_SECTION_JWT_AUTHENTICATION_PLACEHOLDER}, \"entities\": {{ }}}}";
        ConfigWithJwtAuthentication = ConfigWithJwtAuthentication.Replace("<>", authScheme, StringComparison.OrdinalIgnoreCase);

        // create an empty config file
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, ConfigWithJwtAuthentication);

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

    /// <summary>
    /// This Test is used to verify that the validate command is able to catch when data source field or entities field is missing.
    /// </summary>
    [TestMethod]
    public void TestValidateConfigFailsWithNoEntities()
    {
        string ConfigWithoutEntities = $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{RUNTIME_SECTION}}}";

        // create an empty config file
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, ConfigWithoutEntities);

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

    /// <summary>
    /// Validates that when the config has no entities or autoentities, the config
    /// still parses successfully (constructor no longer throws), and IsConfigValid
    /// returns false without throwing.
    /// Adapted for https://github.com/Azure/data-api-builder/issues/3268
    /// </summary>
    [TestMethod]
    public void TestValidateConfigWithNoEntitiesProducesCleanError()
    {
        string configWithoutEntities = $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{RUNTIME_SECTION}}}";

        // Config with no entities should now parse successfully (validation catches it downstream).
        bool parsed = RuntimeConfigLoader.TryParseConfig(configWithoutEntities, out _);
        Assert.IsTrue(parsed, "Config with datasource and no entities should parse successfully.");

        // IsConfigValid should return false cleanly (no exception thrown).
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, configWithoutEntities);
        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);
        Assert.IsFalse(ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!));
    }

    /// <summary>
    /// This Test is used to verify that the validate command is able to catch when data source field is missing.
    /// </summary>
    [TestMethod]
    public void TestValidateConfigFailsWithNoDataSource()
    {
        string ConfigWithoutDataSource = $"{{{SCHEMA_PROPERTY},{RUNTIME_SECTION_WITH_EMPTY_ENTITIES}}}";

        // create an empty config file
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, ConfigWithoutDataSource);

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

    /// <summary>
    /// This method implicitly validates that RuntimeConfigValidator::ValidateConfigSchema(...) successfully
    /// executes against a config file referencing environment variables.
    /// [CLI] ConfigGenerator::IsConfigValid(...)
    ///     |_ [Engine] RuntimeConfigValidator::TryValidateConfig(...)
    ///        |_ [Engine] RuntimeConfigValidator::ValidateConfigSchema(...)
    /// ValidateConfigSchema(...) doesn't execute successfully when a RuntimeConfig object has unresolved environment variables.
    /// Example:
    /// Input file snipppet:
    ///   "data-source": {
    ///     "database-type": "@env('DATABASE_TYPE')", // ENUM
    ///     "connection-string": "@env('CONN_STRING')" // STRING
    ///   }
    ///   ...
    ///   "source": {
    ///     "type": ""stored-procedure",
    ///     "object": "s001.book",
    ///     "parameters": {
    ///         "param1": "@env('sp_param1_int')", // INT
    ///         "param2": "@env('sp_param3_bool')" // BOOL
    ///     }
    ///   }
    /// </summary>
    [TestMethod]
    public void ValidateConfigSchemaWhereConfigReferencesEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable($"connection-string", SAMPLE_TEST_CONN_STRING);
        Environment.SetEnvironmentVariable($"database-type", "mssql");
        Environment.SetEnvironmentVariable($"sp_param1_int", "123");
        Environment.SetEnvironmentVariable($"sp_param3_bool", "true");

        // Capture console output to get error messaging.
        StringWriter writer = new();
        Console.SetOut(writer);

        ((MockFileSystem)_fileSystem!).AddFile(
            path: TEST_RUNTIME_CONFIG_FILE,
            mockFile: CONFIG_ENV_VARS);
        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        // Act
        ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        string loggerOutput = writer.ToString();
        Assert.IsFalse(
            condition: loggerOutput.Contains("Failed to validate config against schema due to"),
            message: "Unexpected errors encountered when validating config schema in RuntimeConfigValidator::ValidateConfigSchema(...).");
        Assert.IsTrue(
            condition: loggerOutput.Contains("The config satisfies the schema requirements."),
            message: "RuntimeConfigValidator::ValidateConfigSchema(...) didn't communicate successful config schema validation.");
    }

    /// <summary>
    /// Tests that validation fails when AKV options are configured without an endpoint.
    /// </summary>
    [TestMethod]
    public async Task TestValidateAKVOptionsWithoutEndpointFails()
    {
        // Arrange
        ConfigureOptions options = new(
            azureKeyVaultRetryPolicyMaxCount: 1,
            azureKeyVaultRetryPolicyDelaySeconds: 1,
            azureKeyVaultRetryPolicyMaxDelaySeconds: 1,
            azureKeyVaultRetryPolicyMode: AKVRetryPolicyMode.Exponential,
            azureKeyVaultRetryPolicyNetworkTimeoutSeconds: 1,
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        await ValidatePropertyOptionsFails(options);
    }

    /// <summary>
    /// Tests that validation fails when Azure Log Analytics options are configured without the Auth options.
    /// </summary>
    [TestMethod]
    public async Task TestValidateAzureLogAnalyticsOptionsWithoutAuthFails()
    {
        // Arrange
        ConfigureOptions options = new(
            azureLogAnalyticsEnabled: CliBool.True,
            azureLogAnalyticsDabIdentifier: "dab-identifier-test",
            azureLogAnalyticsFlushIntervalSeconds: 1,
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        await ValidatePropertyOptionsFails(options);
    }

    /// <summary>
    /// Tests that validation fails when File Sink options are configured without the 'path' property.
    /// </summary>
    [TestMethod]
    public async Task TestValidateFileSinkOptionsWithoutPathFails()
    {
        // Arrange
        ConfigureOptions options = new(
            fileSinkEnabled: CliBool.True,
            fileSinkRollingInterval: RollingInterval.Day,
            fileSinkRetainedFileCountLimit: 1,
            fileSinkFileSizeLimitBytes: 1024,
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        await ValidatePropertyOptionsFails(options);
    }

    /// <summary>
    /// Helper function that ensures properties with missing options fail validation.
    /// </summary>
    private async Task ValidatePropertyOptionsFails(ConfigureOptions options)
    {
        _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, new MockFileData(INITIAL_CONFIG));
        Assert.IsTrue(_fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));
        Mock<RuntimeConfigProvider> mockRuntimeConfigProvider = new(_runtimeConfigLoader);
        RuntimeConfigValidator validator = new(mockRuntimeConfigProvider.Object, _fileSystem, new Mock<ILogger<RuntimeConfigValidator>>().Object);

        Mock<ILoggerFactory> mockLoggerFactory = new();
        Mock<ILogger<JsonConfigSchemaValidator>> mockLogger = new();
        mockLoggerFactory
            .Setup(factory => factory.CreateLogger(typeof(JsonConfigSchemaValidator).FullName!))
            .Returns(mockLogger.Object);

        // Act: Attempts to add File Sink options without empty path
        bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert: Settings are configured, config parses, validation fails.
        Assert.IsTrue(isSuccess);
        string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? config));
        JsonSchemaValidationResult result = await validator.ValidateConfigSchema(config, TEST_RUNTIME_CONFIG_FILE, mockLoggerFactory.Object);
        Assert.IsFalse(result.IsValid);
    }

    /// <summary>
    /// Validates that a non-root config (has data-source but no data-source-files) with zero entities
    /// and an invalid connection string gets a connection string validation error.
    /// Entity validation is gated on successful DB connectivity, so no entity error fires.
    /// The validation still returns false due to the connection string error.
    /// Regression test for https://github.com/Azure/data-api-builder/issues/3267
    /// </summary>
    [TestMethod]
    public void TestValidateNonRootZeroEntitiesWithInvalidConnectionString()
    {
        ((MockFileSystem)_fileSystem!).AddFile(TEST_RUNTIME_CONFIG_FILE, INVALID_INTIAL_CONFIG);
        ValidateOptions validateOptions = new(TEST_RUNTIME_CONFIG_FILE);

        Mock<ILogger<ConfigGenerator>> mockLogger = new();
        SetLoggerForCliConfigGenerator(mockLogger.Object);

        bool isValid = ConfigGenerator.IsConfigValid(validateOptions, _runtimeConfigLoader!, _fileSystem!);

        // Validation should fail due to the empty connection string.
        Assert.IsFalse(isValid);
    }

    /// <summary>
    /// Validates that a root config (with data-source-files pointing to children)
    /// that has no data-source and no entities is considered structurally valid
    /// for parsing. The root config delegates entity requirements to children.
    /// </summary>
    [TestMethod]
    public void TestRootConfigWithNoDataSourceAndNoEntitiesParses()
    {
        string rootConfig = @"
        {
            ""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @""",
            ""runtime"": {
                ""rest"": { ""enabled"": true },
                ""graphql"": { ""enabled"": true },
                ""host"": { ""mode"": ""development"" }
            },
            ""data-source-files"": [""child1.json""],
            ""entities"": {}
        }";

        // The root config should parse without error (no data-source required for root).
        Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(rootConfig, out RuntimeConfig? config));
        Assert.IsNotNull(config);
        Assert.IsTrue(config.IsRootConfig);
    }

    /// <summary>
    /// Validates that a non-root config with a data-source and no entities parses
    /// successfully. Validation of entity presence happens during dab validate,
    /// not during parsing.
    /// </summary>
    [TestMethod]
    public void TestNonRootConfigWithDataSourceAndNoEntitiesParses()
    {
        Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? config));
        Assert.IsNotNull(config);
        Assert.IsFalse(config.IsRootConfig);
    }

    /// <summary>
    /// Non-root with datasource and zero entities → error.
    /// </summary>
    [TestMethod]
    public void TestNonRootWithDataSourceAndNoEntitiesProducesError()
    {
        RuntimeConfig config = BuildTestConfig(hasDataSource: true, entities: new());
        RuntimeConfigValidator validator = BuildValidator(config);
        validator.ValidateDataSourceAndEntityPresence(config);

        Assert.IsTrue(validator.ConfigValidationExceptions.Count > 0,
            "Expected validation error for non-root config with datasource but no entities.");
    }

    /// <summary>
    /// Non-root with no datasource → error.
    /// </summary>
    [TestMethod]
    public void TestNonRootWithNoDataSourceProducesError()
    {
        RuntimeConfig config = BuildTestConfig(hasDataSource: false, entities: new());
        RuntimeConfigValidator validator = BuildValidator(config);
        validator.ValidateDataSourceAndEntityPresence(config);

        Assert.AreEqual(1, validator.ConfigValidationExceptions.Count);
        Assert.IsTrue(validator.ConfigValidationExceptions[0].Message.Contains("data source is required"));
    }

    /// <summary>
    /// Non-root with datasource and entities → valid.
    /// </summary>
    [TestMethod]
    public void TestNonRootWithDataSourceAndEntitiesIsValid()
    {
        RuntimeConfig config = BuildTestConfig(
            hasDataSource: true,
            entities: new() { { "Book", BuildSimpleEntity("dbo.books") } });
        RuntimeConfigValidator validator = BuildValidator(config);
        validator.ValidateDataSourceAndEntityPresence(config);

        Assert.AreEqual(0, validator.ConfigValidationExceptions.Count);
    }

    /// <summary>
    /// Root with no datasource and no entities → valid (children carry the load).
    /// </summary>
    [TestMethod]
    public void TestRootWithNoDataSourceAndNoEntitiesIsValid()
    {
        RuntimeConfig childConfig = BuildTestConfig(
            hasDataSource: true,
            entities: new() { { "Book", BuildSimpleEntity("dbo.books") } });
        childConfig.IsChildConfig = true;

        RuntimeConfig rootConfig = BuildTestConfig(
            hasDataSource: false, entities: new(),
            dataSourceFiles: new DataSourceFiles(new[] { "child.json" }));
        rootConfig.ChildConfigs.Add(("child.json", childConfig));

        RuntimeConfigValidator validator = BuildValidator(rootConfig);
        validator.ValidateDataSourceAndEntityPresence(rootConfig);

        Assert.AreEqual(0, validator.ConfigValidationExceptions.Count);
    }

    /// <summary>
    /// Root with no datasource but with entities → error (entities need a datasource).
    /// </summary>
    [TestMethod]
    public void TestRootWithNoDataSourceButEntitiesProducesError()
    {
        RuntimeConfig childConfig = BuildTestConfig(
            hasDataSource: true,
            entities: new() { { "Author", BuildSimpleEntity("dbo.authors") } });
        childConfig.IsChildConfig = true;

        RuntimeConfig rootConfig = BuildTestConfig(
            hasDataSource: false,
            entities: new() { { "Book", BuildSimpleEntity("dbo.books") } },
            dataSourceFiles: new DataSourceFiles(new[] { "child.json" }));
        rootConfig.ChildConfigs.Add(("child.json", childConfig));

        RuntimeConfigValidator validator = BuildValidator(rootConfig);
        validator.ValidateDataSourceAndEntityPresence(rootConfig);

        Assert.IsTrue(validator.ConfigValidationExceptions.Count > 0);
        Assert.IsTrue(validator.ConfigValidationExceptions[0].Message.Contains("must not define entities"));
    }

    /// <summary>
    /// Root with datasource and entities → valid (follows normal entity rules).
    /// </summary>
    [TestMethod]
    public void TestRootWithDataSourceAndEntitiesIsValid()
    {
        RuntimeConfig childConfig = BuildTestConfig(
            hasDataSource: true,
            entities: new() { { "Author", BuildSimpleEntity("dbo.authors") } });
        childConfig.IsChildConfig = true;

        RuntimeConfig rootConfig = BuildTestConfig(
            hasDataSource: true,
            entities: new() { { "Book", BuildSimpleEntity("dbo.books") } },
            dataSourceFiles: new DataSourceFiles(new[] { "child.json" }));
        rootConfig.ChildConfigs.Add(("child.json", childConfig));

        RuntimeConfigValidator validator = BuildValidator(rootConfig);
        validator.ValidateDataSourceAndEntityPresence(rootConfig);

        Assert.AreEqual(0, validator.ConfigValidationExceptions.Count);
    }

    /// <summary>
    /// Child config with datasource but no entities → error naming the child file.
    /// </summary>
    [TestMethod]
    public void TestChildWithDataSourceAndNoEntitiesProducesNamedError()
    {
        RuntimeConfig childConfig = BuildTestConfig(hasDataSource: true, entities: new());
        childConfig.IsChildConfig = true;

        RuntimeConfig rootConfig = BuildTestConfig(
            hasDataSource: false, entities: new(),
            dataSourceFiles: new DataSourceFiles(new[] { "child-db.json" }));
        rootConfig.ChildConfigs.Add(("child-db.json", childConfig));

        RuntimeConfigValidator validator = BuildValidator(rootConfig);
        validator.ValidateDataSourceAndEntityPresence(rootConfig);

        Assert.AreEqual(1, validator.ConfigValidationExceptions.Count);
        Assert.IsTrue(validator.ConfigValidationExceptions[0].Message.Contains("child-db.json"),
            "Error should name the child config file.");
        Assert.IsTrue(validator.ConfigValidationExceptions[0].Message.Contains("No entities found"),
            "Error should mention no entities found.");
    }

    /// <summary>
    /// Child config with no datasource → error naming the child file.
    /// </summary>
    [TestMethod]
    public void TestChildWithNoDataSourceProducesNamedError()
    {
        RuntimeConfig childConfig = BuildTestConfig(hasDataSource: false, entities: new());
        childConfig.IsChildConfig = true;

        RuntimeConfig rootConfig = BuildTestConfig(
            hasDataSource: false, entities: new(),
            dataSourceFiles: new DataSourceFiles(new[] { "child-db.json" }));
        rootConfig.ChildConfigs.Add(("child-db.json", childConfig));

        RuntimeConfigValidator validator = BuildValidator(rootConfig);
        validator.ValidateDataSourceAndEntityPresence(rootConfig);

        Assert.AreEqual(1, validator.ConfigValidationExceptions.Count);
        Assert.IsTrue(validator.ConfigValidationExceptions[0].Message.Contains("child-db.json"));
        Assert.IsTrue(validator.ConfigValidationExceptions[0].Message.Contains("data source is required"));
    }

    /// <summary>
    /// Helper: builds a RuntimeConfigValidator in validate-only mode over the given config.
    /// </summary>
    private static RuntimeConfigValidator BuildValidator(RuntimeConfig config)
    {
        MockFileSystem fs = new();
        FileSystemRuntimeConfigLoader loader = new(fs) { RuntimeConfig = config };
        RuntimeConfigProvider provider = new(loader);
        return new RuntimeConfigValidator(provider, fs, new Mock<ILogger<RuntimeConfigValidator>>().Object, isValidateOnly: true);
    }

    /// <summary>
    /// Helper: builds a minimal RuntimeConfig for testing.
    /// </summary>
    private static RuntimeConfig BuildTestConfig(
        bool hasDataSource,
        Dictionary<string, Entity> entities,
        DataSourceFiles? dataSourceFiles = null)
    {
        DataSource? ds = hasDataSource
            ? new DataSource(DatabaseType.MSSQL, "Server=localhost;Database=test;", Options: null)
            : null;

        return new RuntimeConfig(
            Schema: null,
            DataSource: ds,
            Runtime: new(
                Rest: new(),
                GraphQL: new(),
                Mcp: new(),
                Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
            Entities: new RuntimeEntities(entities),
            DataSourceFiles: dataSourceFiles);
    }

    /// <summary>
    /// Helper: builds a simple entity for testing.
    /// </summary>
    private static Entity BuildSimpleEntity(string source)
    {
        return new Entity(
            Source: new EntitySource(Object: source, Type: EntitySourceType.Table, Parameters: null, KeyFields: null),
            GraphQL: new(Singular: null, Plural: null),
            Fields: null,
            Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
            Permissions: new[] { new EntityPermission("anonymous", new[] { new EntityAction(EntityActionOperation.Read, null, null) }) },
            Relationships: null,
            Mappings: null);
    }
}
