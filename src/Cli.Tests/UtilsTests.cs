// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions.TestingHelpers;

namespace Cli.Tests;

[TestClass]
public class UtilsTests
{
    [TestInitialize]
    public void TestInitialize()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });

        SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
    }

    [TestMethod]
    public void ConstructRestOptionsWithNullDisablesRest()
    {
        EntityRestOptions options = ConstructRestOptions(null, Array.Empty<SupportedHttpVerb>());
        Assert.IsFalse(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithTrueEnablesRest()
    {
        EntityRestOptions options = ConstructRestOptions("true", Array.Empty<SupportedHttpVerb>());
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithFalseDisablesRest()
    {
        EntityRestOptions options = ConstructRestOptions("false", Array.Empty<SupportedHttpVerb>());
        Assert.IsFalse(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithCustomPathSetsPath()
    {
        EntityRestOptions options = ConstructRestOptions("customPath", Array.Empty<SupportedHttpVerb>());
        Assert.AreEqual("/customPath", options.Path);
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithCustomPathAndMethodsSetsPathAndMethods()
    {
        EntityRestOptions options = ConstructRestOptions("customPath", new[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post });
        Assert.AreEqual("/customPath", options.Path);
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(2, options.Methods.Length);
        Assert.IsTrue(options.Methods.Contains(SupportedHttpVerb.Get));
        Assert.IsTrue(options.Methods.Contains(SupportedHttpVerb.Post));
    }

    [TestMethod]
    public void ConstructGraphQLOptionsWithNullWillDisable()
    {
        EntityGraphQLOptions options = ConstructGraphQLTypeDetails(null, null);
        Assert.IsFalse(options.Enabled);
    }

    [TestMethod]
    public void ConstructGraphQLOptionsWithTrueWillEnable()
    {
        EntityGraphQLOptions options = ConstructGraphQLTypeDetails("true", null);
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructGraphQLOptionsWithFalseWillDisable()
    {
        EntityGraphQLOptions options = ConstructGraphQLTypeDetails("false", null);
        Assert.IsFalse(options.Enabled);
    }

    [TestMethod]
    public void ConstructGraphQLOptionsWithSingularWillSetSingularAndDefaultPlural()
    {
        EntityGraphQLOptions options = ConstructGraphQLTypeDetails("singular", null);
        Assert.AreEqual("singular", options.Singular);
        Assert.AreEqual("singulars", options.Plural);
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructGraphQLOptionsWithSingularAndPluralWillSetSingularAndPlural()
    {
        EntityGraphQLOptions options = ConstructGraphQLTypeDetails("singular:plural", null);
        Assert.AreEqual("singular", options.Singular);
        Assert.AreEqual("plural", options.Plural);
        Assert.IsTrue(options.Enabled);
    }

    /// <summary>
    /// Test to check the precedence logic for config file in CLI
    /// </summary>
    [DataTestMethod]
    [DataRow("", "my-config.json", "my-config.json", DisplayName = "user provided the config file and environment variable was not set.")]
    [DataRow("Test", "my-config.json", "my-config.json", DisplayName = "user provided the config file and environment variable was set.")]
    [DataRow("Test", null, $"{RuntimeConfigLoader.CONFIGFILE_NAME}.Test{RuntimeConfigLoader.CONFIG_EXTENSION}", DisplayName = "config not provided, but environment variable was set.")]
    [DataRow("", null, $"{RuntimeConfigLoader.CONFIGFILE_NAME}{RuntimeConfigLoader.CONFIG_EXTENSION}", DisplayName = "neither config was provided, nor environment variable was set.")]
    public void TestConfigSelectionBasedOnCliPrecedence(
        string? environmentValue,
        string? userProvidedConfigFile,
        string expectedRuntimeConfigFile)
    {
        MockFileSystem fileSystem = new();
        fileSystem.AddFile(expectedRuntimeConfigFile, new MockFileData(""));

        RuntimeConfigLoader loader = new(fileSystem);

        string? envValueBeforeTest = Environment.GetEnvironmentVariable(RuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME);
        Environment.SetEnvironmentVariable(RuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
        Assert.IsTrue(TryGetConfigFileBasedOnCliPrecedence(loader, userProvidedConfigFile, out string? actualRuntimeConfigFile));
        Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        Environment.SetEnvironmentVariable(RuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, envValueBeforeTest);
    }

    /// <summary>
    /// Test to verify negative/positive string numerals are correctly parsed as integers
    /// Decimal values are parsed as double.
    /// Boolean string is correctly parsed as boolean
    /// everything else is parsed as string.
    /// </summary>
    [TestMethod]
    public void TestTryParseSourceParameterDictionary()
    {
        IEnumerable<string>? parametersList = new string[] { "param1:123", "param2:-243", "param3:220.12", "param4:True", "param5:dab" };
        Assert.IsTrue(TryParseSourceParameterDictionary(parametersList, out Dictionary<string, object>? sourceParameters));
        Assert.IsNotNull(sourceParameters);
        Assert.AreEqual(sourceParameters.GetValueOrDefault("param1"), 123);
        Assert.AreEqual(sourceParameters.GetValueOrDefault("param2"), -243);
        Assert.AreEqual(sourceParameters.GetValueOrDefault("param3"), 220.12);
        Assert.AreEqual(sourceParameters.GetValueOrDefault("param4"), true);
        Assert.AreEqual(sourceParameters.GetValueOrDefault("param5"), "dab");
    }

    /// <summary>
    /// Validates permissions operations are valid for the provided source type.
    /// </summary>
    /// <param name="operations">CRUD + Execute + *</param>
    /// <param name="sourceType">Table, StoredProcedure, View</param>
    /// <param name="isSuccess">True/False</param>
    [DataTestMethod]
    [DataRow(new string[] { "*" }, EntityType.StoredProcedure, true, DisplayName = "PASS: Stored-Procedure with wildcard CRUD operation.")]
    [DataRow(new string[] { "execute" }, EntityType.StoredProcedure, true, DisplayName = "PASS: Stored-Procedure with execute operation only.")]
    [DataRow(new string[] { "create", "read" }, EntityType.StoredProcedure, false, DisplayName = "FAIL: Stored-Procedure with more than 1 CRUD operation.")]
    [DataRow(new string[] { "*" }, EntityType.Table, true, DisplayName = "PASS: Table with wildcard CRUD operation.")]
    [DataRow(new string[] { "create" }, EntityType.Table, true, DisplayName = "PASS: Table with 1 CRUD operation.")]
    [DataRow(new string[] { "create", "read" }, EntityType.Table, true, DisplayName = "PASS: Table with more than 1 CRUD operation.")]
    [DataRow(new string[] { "*" }, EntityType.View, true, DisplayName = "PASS: View with wildcard CRUD operation.")]
    [DataRow(new string[] { "create" }, EntityType.View, true, DisplayName = "PASS: View with 1 CRUD operation.")]
    [DataRow(new string[] { "create", "read" }, EntityType.View, true, DisplayName = "PASS: View with more than 1 CRUD operation.")]

    public void TestStoredProcedurePermissions(
        string[] operations,
        EntityType sourceType,
        bool isSuccess)
    {
        Assert.AreEqual(isSuccess, VerifyOperations(operations, sourceType));
    }

    /// <summary>
    /// Test to verify that CLI is able to figure out if the api path prefix for rest/graphql contains invalid characters.
    /// </summary>
    [DataTestMethod]
    [DataRow("/", "REST", true, DisplayName = "Only forward slash as api path")]
    [DataRow("/$%^", "REST", false, DisplayName = "Api path containing only reserved characters.")]
    [DataRow("/rest-api", "REST", true, DisplayName = "Valid api path")]
    [DataRow("/graphql@api", "GraphQL", false, DisplayName = "Api path containing some reserved characters.")]
    [DataRow("/api path", "REST", true, DisplayName = "Api path containing space.")]
    public void TestApiPathIsWellFormed(string apiPath, string apiType, bool expectSuccess)
    {
        Assert.AreEqual(expectSuccess, IsApiPathValid(apiPath, apiType));
    }

    /// <summary>
    /// Test to verify that both Audience and Issuer is mandatory when Authentication Provider is
    /// neither EasyAuthType or Simulator. If Authentication Provider is either EasyAuth or Simulator
    /// audience and issuer are ignored.
    /// </summary>
    [DataTestMethod]
    [DataRow("StaticWebApps", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: Audience and Issuer ignored with StaticWebApps.")]
    [DataRow("StaticWebApps", null, "issuer-xxx", true, DisplayName = "PASS: Issuer ignored with StaticWebApps.")]
    [DataRow("StaticWebApps", "aud-xxx", null, true, DisplayName = "PASS: Audience ignored with StaticWebApps.")]
    [DataRow("StaticWebApps", null, null, true, DisplayName = "PASS: StaticWebApps correctly configured with neither audience nor issuer.")]
    [DataRow("AppService", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: Audience and Issuer ignored with AppService.")]
    [DataRow("AppService", null, "issuer-xxx", true, DisplayName = "PASS: Issuer ignored with AppService.")]
    [DataRow("AppService", "aud-xxx", null, true, DisplayName = "PASS: Audience ignored with AppService.")]
    [DataRow("AppService", null, null, true, DisplayName = "PASS: AppService correctly configured with neither audience nor issuer.")]
    [DataRow("Simulator", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: Audience and Issuer ignored with Simulator.")]
    [DataRow("Simulator", null, "issuer-xxx", true, DisplayName = "PASS: Issuer ignored with Simulator.")]
    [DataRow("Simulator", "aud-xxx", null, true, DisplayName = "PASS: Audience ignored with Simulator.")]
    [DataRow("Simulator", null, null, true, DisplayName = "PASS: Simulator correctly configured with neither audience nor issuer.")]
    [DataRow("AzureAD", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: AzureAD correctly configured with both audience and issuer.")]
    [DataRow("AzureAD", null, "issuer-xxx", false, DisplayName = "FAIL: AzureAD incorrectly configured with no audience specified.")]
    [DataRow("AzureAD", "aud-xxx", null, false, DisplayName = "FAIL: AzureAD incorrectly configured with no issuer specified.")]
    [DataRow("AzureAD", null, null, false, DisplayName = "FAIL: AzureAD incorrectly configured with no audience or issuer specified.")]
    public void TestValidateAudienceAndIssuerForAuthenticationProvider(
        string authenticationProvider,
        string? audience,
        string? issuer,
        bool expectSuccess)
    {
        Assert.AreEqual(
            expectSuccess,
            ValidateAudienceAndIssuerForJwtProvider(authenticationProvider, audience, issuer)
        );
    }
}

