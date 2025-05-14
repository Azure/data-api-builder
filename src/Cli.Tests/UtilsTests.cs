// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

[TestClass]
public class UtilsTests
{
    [TestInitialize]
    public void TestInitialize()
    {
        ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

        SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
        SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
    }

    [TestMethod]
    public void ConstructRestOptionsForCosmosDbNoSQLIgnoresOtherParamsAndDisables()
    {
        EntityRestOptions options = ConstructRestOptions(restRoute: "true", supportedHttpVerbs: null, isCosmosDbNoSql: true);
        Assert.IsFalse(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithNullEnablesRest()
    {
        EntityRestOptions options = ConstructRestOptions(restRoute: null, supportedHttpVerbs: null, isCosmosDbNoSql: false);
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithTrueEnablesRest()
    {
        EntityRestOptions options = ConstructRestOptions(restRoute: "true", supportedHttpVerbs: null, isCosmosDbNoSql: false);
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithFalseDisablesRest()
    {
        EntityRestOptions options = ConstructRestOptions(restRoute: "false", supportedHttpVerbs: null, isCosmosDbNoSql: false);
        Assert.IsFalse(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithCustomPathSetsPath()
    {
        EntityRestOptions options = ConstructRestOptions(restRoute: "customPath", supportedHttpVerbs: null, isCosmosDbNoSql: false);
        Assert.AreEqual("/customPath", options.Path);
        Assert.IsTrue(options.Enabled);
    }

    [TestMethod]
    public void ConstructRestOptionsWithCustomPathAndMethodsSetsPathAndMethods()
    {
        EntityRestOptions options = ConstructRestOptions("customPath", new[] { SupportedHttpVerb.Get, SupportedHttpVerb.Post }, false);
        Assert.AreEqual("/customPath", options.Path);
        Assert.IsTrue(options.Enabled);
        Assert.IsNotNull(options.Methods);
        Assert.AreEqual(2, options.Methods.Length);
        Assert.IsTrue(options.Methods.Contains(SupportedHttpVerb.Get));
        Assert.IsTrue(options.Methods.Contains(SupportedHttpVerb.Post));
    }

    [TestMethod]
    public void ConstructGraphQLOptionsWithNullWillEnable()
    {
        EntityGraphQLOptions options = ConstructGraphQLTypeDetails(null, null);
        Assert.IsTrue(options.Enabled);
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
        Assert.AreEqual("", options.Plural);
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
    [DataRow("Test", null, $"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}", DisplayName = "config not provided, but environment variable was set.")]
    [DataRow("", null, $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}", DisplayName = "neither config was provided, nor environment variable was set.")]
    public void TestConfigSelectionBasedOnCliPrecedence(
        string? environmentValue,
        string? userProvidedConfigFile,
        string expectedRuntimeConfigFile)
    {
        MockFileSystem fileSystem = new();
        fileSystem.AddFile(expectedRuntimeConfigFile, new MockFileData(""));

        FileSystemRuntimeConfigLoader loader = new(fileSystem);

        string? envValueBeforeTest = Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME);
        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
        Assert.IsTrue(TryGetConfigFileBasedOnCliPrecedence(loader, userProvidedConfigFile, out string? actualRuntimeConfigFile));
        Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, envValueBeforeTest);
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
    /// <param name="entitySourceType">Table, StoredProcedure, View</param>
    /// <param name="isSuccess">True/False</param>
    [DataTestMethod]
    [DataRow(new string[] { "*" }, EntitySourceType.StoredProcedure, true, DisplayName = "PASS: Stored-Procedure with wildcard CRUD operation.")]
    [DataRow(new string[] { "execute" }, EntitySourceType.StoredProcedure, true, DisplayName = "PASS: Stored-Procedure with execute operation only.")]
    [DataRow(new string[] { "create", "read" }, EntitySourceType.StoredProcedure, false, DisplayName = "FAIL: Stored-Procedure with more than 1 CRUD operation.")]
    [DataRow(new string[] { "*" }, EntitySourceType.Table, true, DisplayName = "PASS: Table with wildcard CRUD operation.")]
    [DataRow(new string[] { "create" }, EntitySourceType.Table, true, DisplayName = "PASS: Table with 1 CRUD operation.")]
    [DataRow(new string[] { "create", "read" }, EntitySourceType.Table, true, DisplayName = "PASS: Table with more than 1 CRUD operation.")]
    [DataRow(new string[] { "*" }, EntitySourceType.View, true, DisplayName = "PASS: View with wildcard CRUD operation.")]
    [DataRow(new string[] { "create" }, EntitySourceType.View, true, DisplayName = "PASS: View with 1 CRUD operation.")]
    [DataRow(new string[] { "create", "read" }, EntitySourceType.View, true, DisplayName = "PASS: View with more than 1 CRUD operation.")]

    public void TestStoredProcedurePermissions(
        string[] operations,
        EntitySourceType entitySourceType,
        bool isSuccess)
    {
        Assert.AreEqual(isSuccess, VerifyOperations(operations, entitySourceType));
    }

    /// <summary>
    /// Test to verify that CLI is able to figure out if the api path prefix for rest/graphql contains invalid characters.
    /// </summary>
    [DataTestMethod]
    [DataRow("/", true, DisplayName = "Only forward slash as api path")]
    [DataRow("/$%^", false, DisplayName = "Api path containing only reserved characters.")]
    [DataRow("/rest-api", true, DisplayName = "Valid api path")]
    [DataRow("/graphql@api", false, DisplayName = "Api path containing some reserved characters.")]
    [DataRow("/api path", true, DisplayName = "Api path containing space.")]
    public void TestApiPathIsWellFormed(string apiPath, bool expectSuccess)
    {
        Assert.AreEqual(expectSuccess, IsURIComponentValid(apiPath));
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
    [DataRow("EntraID", "aud-xxx", "issuer-xxx", true, DisplayName = "PASS: EntraID correctly configured with both audience and issuer.")]
    [DataRow("EntraID", null, "issuer-xxx", false, DisplayName = "FAIL: EntraID incorrectly configured with no audience specified.")]
    [DataRow("EntraID", "aud-xxx", null, false, DisplayName = "FAIL: EntraID incorrectly configured with no issuer specified.")]
    [DataRow("EntraID", null, null, false, DisplayName = "FAIL: EntraID incorrectly configured with no audience or issuer specified.")]
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

    /// <summary>
    /// Test to verify that when DAB_ENVIRONMENT variable is set, also base config and
    /// dab-config.{DAB_ENVIRONMENT}.json file is present, then when DAB engine is started, it will merge
    /// the two config and use the merged config to startup the engine.
    /// Here, baseConfig(dab-config.json) has no connection_string, while dab-config.Test.json has a defined connection string.
    /// once the `dab start` is executed the merge happens and the merged file contains the connection string from the
    /// Test config.
    /// Scenarios Covered:
    /// 1. Merging of Array: Complete override of Book permissions from the second config (environment based config).
    /// 2. Merging Property when present in both config: Connection string in the second config overrides that of the first.
    /// 3. Non-merging when a property in the environmentConfig file is null: {data-source.options} is null in the environment config,
    /// So it is added to the merged config as it is with no change.
    /// 4. Merging when a property is only present in the environmentConfig file: Publisher entity is present only in environment config,
    /// So it is directly added to the merged config.
    /// 5. Properties of same name but different level do not conflict: source is both a entityName and a property inside book entity, both are
    /// treated differently.
    /// </summary>
    [TestMethod]
    public void TestMergeConfig()
    {
        MockFileSystem fileSystem = new();
        fileSystem.AddFile(DEFAULT_CONFIG_FILE_NAME, new MockFileData(BASE_CONFIG));
        fileSystem.AddFile("dab-config.Test.json", new MockFileData(ENV_BASED_CONFIG));

        FileSystemRuntimeConfigLoader loader = new(fileSystem);

        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, "Test");

        Assert.IsTrue(ConfigMerger.TryMergeConfigsIfAvailable(fileSystem, loader, new StringLogger(), out string? mergedConfig), "Failed to merge config files");
        Assert.AreEqual(mergedConfig, "dab-config.Test.merged.json");
        Assert.IsTrue(fileSystem.File.Exists(mergedConfig));
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(MERGED_CONFIG), JObject.Parse(fileSystem.File.ReadAllText(mergedConfig))));
    }

    /// <summary>
    /// Test to verify that merged config file is only used for the below scenario
    /// 1. Environment value is set.
    /// 2. Both Base and envBased config file is present.
    /// In all other cases, the TryMergeConfigsIfAvailable method should return false
    /// and out param for the mergedConfigFile should be null.
    /// </summary>
    [DataTestMethod]
    [DataRow("", false, false, null, false, DisplayName = "If environment value is not set, merged config file is not generated.")]
    [DataRow("", false, true, null, false, DisplayName = "If environment value is not set, merged config file is not generated.")]
    [DataRow("", true, false, null, false, DisplayName = "If environment value is not set, merged config file is not generated.")]
    [DataRow("", true, true, null, false, DisplayName = "If environment value is not set, merged config file is not generated.")]
    [DataRow(null, false, false, null, false, DisplayName = "If environment variable is removed, merged config file is not generated.")]
    [DataRow(null, false, true, null, false, DisplayName = "If environment variable is removed, merged config file is not generated.")]
    [DataRow(null, true, false, null, false, DisplayName = "If environment variable is removed, merged config file is not generated.")]
    [DataRow(null, true, true, null, false, DisplayName = "If environment variable is removed, merged config file is not generated.")]
    [DataRow("Test", false, false, null, false, DisplayName = "Environment value set but base config not available, merged config file is not generated.")]
    [DataRow("Test", false, true, null, false, DisplayName = "Environment value set but base config not available, merged config file is not generated.")]
    [DataRow("Test", true, false, null, false, DisplayName = "Environment value set but env based config not available, merged config file is not generated.")]
    [DataRow("Test", true, true, "dab-config.Test.merged.json", true, DisplayName = "Environment value set and both base and envConfig available, merged config file is generated.")]
    public void TestMergeConfigAvailability(
        string? environmentValue,
        bool isBaseConfigPresent,
        bool isEnvironmentBasedConfigPresent,
        string? expectedMergedConfigFileName,
        bool expectedIsMergedConfigAvailable)
    {
        MockFileSystem fileSystem = new();

        // Setting up the test scenarios
        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
        string baseConfig = "dab-config.json";
        string envBasedConfig = "dab-config.Test.json";

        if (isBaseConfigPresent)
        {
            fileSystem.AddFile(baseConfig, new("{}"));
        }

        if (isEnvironmentBasedConfigPresent)
        {
            fileSystem.AddFile(envBasedConfig, new("{}"));
        }

        FileSystemRuntimeConfigLoader loader = new(fileSystem);

        Assert.AreEqual(
            expectedIsMergedConfigAvailable,
            ConfigMerger.TryMergeConfigsIfAvailable(fileSystem, loader, new StringLogger(), out string? mergedConfigFile),
            "Availability of merge config should match");
        Assert.AreEqual(expectedMergedConfigFileName, mergedConfigFile, "Merge config file name should match expected");

        Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, null);
    }
}

