// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Cli.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static Cli.Utils;
using static Cli.Tests.TestHelper;

namespace Cli.Tests;

/// <summary>
/// Tests for the autoentities-configure CLI command.
/// </summary>
[TestClass]
public class AutoentitiesConfigureTests
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
    /// Tests that a new autoentities definition is successfully created with patterns.
    /// </summary>
    [TestMethod]
    public void TestCreateAutoentitiesDefinition_WithPatterns()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoentitiesConfigureOptions options = new(
            definitionName: "test-def",
            patternsInclude: new[] { "dbo.%", "sys.%" },
            patternsExclude: new[] { "dbo.internal%" },
            patternsName: "{schema}_{table}",
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsTrue(success);
        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? config));
        Assert.IsNotNull(config.Autoentities);
        Assert.IsTrue(config.Autoentities.AutoEntities.ContainsKey("test-def"));

        Autoentity autoentity = config.Autoentities.AutoEntities["test-def"];
        Assert.AreEqual(2, autoentity.Patterns.Include.Length);
        Assert.AreEqual("dbo.%", autoentity.Patterns.Include[0]);
        Assert.AreEqual("sys.%", autoentity.Patterns.Include[1]);
        Assert.AreEqual(1, autoentity.Patterns.Exclude.Length);
        Assert.AreEqual("dbo.internal%", autoentity.Patterns.Exclude[0]);
        Assert.AreEqual("{schema}_{table}", autoentity.Patterns.Name);
    }

    /// <summary>
    /// Tests that template options are correctly configured for an autoentities definition.
    /// </summary>
    [TestMethod]
    public void TestConfigureAutoentitiesDefinition_WithTemplateOptions()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoentitiesConfigureOptions options = new(
            definitionName: "test-def",
            templateRestEnabled: true,
            templateGraphqlEnabled: false,
            templateMcpDmlTool: "true",
            templateCacheEnabled: true,
            templateCacheTtlSeconds: 30,
            templateCacheLevel: "L1",
            templateHealthEnabled: true,
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsTrue(success);
        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? config));
        
        Autoentity autoentity = config.Autoentities!.AutoEntities["test-def"];
        Assert.IsTrue(autoentity.Template.Rest.Enabled);
        Assert.IsFalse(autoentity.Template.GraphQL.Enabled);
        Assert.IsTrue(autoentity.Template.Mcp!.DmlToolEnabled);
        Assert.AreEqual(true, autoentity.Template.Cache.Enabled);
        Assert.AreEqual(30, autoentity.Template.Cache.TtlSeconds);
        Assert.AreEqual(EntityCacheLevel.L1, autoentity.Template.Cache.Level);
        Assert.IsTrue(autoentity.Template.Health.Enabled);
    }

    /// <summary>
    /// Tests that an existing autoentities definition is successfully updated.
    /// </summary>
    [TestMethod]
    public void TestUpdateExistingAutoentitiesDefinition()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        // Create initial definition
        AutoentitiesConfigureOptions initialOptions = new(
            definitionName: "test-def",
            patternsInclude: new[] { "dbo.%" },
            templateCacheTtlSeconds: 10,
            permissions: new[] { "anonymous", "read" },
            config: TEST_RUNTIME_CONFIG_FILE
        );
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(initialOptions, _runtimeConfigLoader!, _fileSystem!));

        // Update definition
        AutoentitiesConfigureOptions updateOptions = new(
            definitionName: "test-def",
            patternsExclude: new[] { "dbo.internal%" },
            templateCacheTtlSeconds: 60,
            permissions: new[] { "authenticated", "create,read,update,delete" },
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(updateOptions, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsTrue(success);
        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? config));
        
        Autoentity autoentity = config.Autoentities!.AutoEntities["test-def"];
        // Include should remain from initial setup
        Assert.AreEqual(1, autoentity.Patterns.Include.Length);
        Assert.AreEqual("dbo.%", autoentity.Patterns.Include[0]);
        // Exclude should be added
        Assert.AreEqual(1, autoentity.Patterns.Exclude.Length);
        Assert.AreEqual("dbo.internal%", autoentity.Patterns.Exclude[0]);
        // Cache TTL should be updated
        Assert.AreEqual(60, autoentity.Template.Cache.TtlSeconds);
        // Permissions should be replaced
        Assert.AreEqual(1, autoentity.Permissions.Length);
        Assert.AreEqual("authenticated", autoentity.Permissions[0].Role);
    }

    /// <summary>
    /// Tests that permissions are correctly parsed and applied.
    /// </summary>
    [TestMethod]
    public void TestConfigureAutoentitiesDefinition_WithMultipleActions()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoentitiesConfigureOptions options = new(
            definitionName: "test-def",
            permissions: new[] { "authenticated", "create,read,update,delete" },
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsTrue(success);
        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? config));
        
        Autoentity autoentity = config.Autoentities!.AutoEntities["test-def"];
        Assert.AreEqual(1, autoentity.Permissions.Length);
        Assert.AreEqual("authenticated", autoentity.Permissions[0].Role);
        Assert.AreEqual(4, autoentity.Permissions[0].Actions.Length);
    }

    /// <summary>
    /// Tests that invalid MCP dml-tool value is handled correctly.
    /// </summary>
    [TestMethod]
    public void TestConfigureAutoentitiesDefinition_InvalidMcpDmlTool()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoentitiesConfigureOptions options = new(
            definitionName: "test-def",
            templateMcpDmlTool: "invalid-value",
            permissions: new[] { "anonymous", "read" },
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert - Should fail due to invalid MCP value
        Assert.IsFalse(success);
    }

    /// <summary>
    /// Tests that invalid cache level value is handled correctly.
    /// </summary>
    [TestMethod]
    public void TestConfigureAutoentitiesDefinition_InvalidCacheLevel()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        AutoentitiesConfigureOptions options = new(
            definitionName: "test-def",
            templateCacheLevel: "InvalidLevel",
            permissions: new[] { "anonymous", "read" },
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert - Should fail due to invalid cache level
        Assert.IsFalse(success);
    }

    /// <summary>
    /// Tests that multiple autoentities definitions can coexist.
    /// </summary>
    [TestMethod]
    public void TestMultipleAutoentitiesDefinitions()
    {
        // Arrange
        InitOptions initOptions = CreateBasicInitOptionsForMsSqlWithConfig(config: TEST_RUNTIME_CONFIG_FILE);
        Assert.IsTrue(ConfigGenerator.TryGenerateConfig(initOptions, _runtimeConfigLoader!, _fileSystem!));

        // Create first definition
        AutoentitiesConfigureOptions options1 = new(
            definitionName: "def-1",
            patternsInclude: new[] { "dbo.%" },
            permissions: new[] { "anonymous", "read" },
            config: TEST_RUNTIME_CONFIG_FILE
        );
        Assert.IsTrue(ConfigGenerator.TryConfigureAutoentities(options1, _runtimeConfigLoader!, _fileSystem!));

        // Create second definition
        AutoentitiesConfigureOptions options2 = new(
            definitionName: "def-2",
            patternsInclude: new[] { "sys.%" },
            permissions: new[] { "authenticated", "*" },
            config: TEST_RUNTIME_CONFIG_FILE
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options2, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsTrue(success);
        Assert.IsTrue(_runtimeConfigLoader!.TryLoadConfig(TEST_RUNTIME_CONFIG_FILE, out RuntimeConfig? config));
        Assert.AreEqual(2, config.Autoentities!.AutoEntities.Count);
        Assert.IsTrue(config.Autoentities.AutoEntities.ContainsKey("def-1"));
        Assert.IsTrue(config.Autoentities.AutoEntities.ContainsKey("def-2"));
    }

    /// <summary>
    /// Tests that attempting to configure autoentities without a config file fails.
    /// </summary>
    [TestMethod]
    public void TestConfigureAutoentitiesDefinition_NoConfigFile()
    {
        // Arrange
        AutoentitiesConfigureOptions options = new(
            definitionName: "test-def",
            permissions: new[] { "anonymous", "read" }
        );

        // Act
        bool success = ConfigGenerator.TryConfigureAutoentities(options, _runtimeConfigLoader!, _fileSystem!);

        // Assert
        Assert.IsFalse(success);
    }
}
