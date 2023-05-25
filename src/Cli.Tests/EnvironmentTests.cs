// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Contains test involving environment variables.
/// </summary>
[TestClass]
public class EnvironmentTests
{
    /// <summary>
    /// Test to verify that environment variable setup in the system is picked up correctly
    /// when no .env file is present.
    /// </summary>
    [TestMethod]
    public void TestEnvironmentVariableIsConsumedCorrectly()
    {
        string envVariableName = "DAB_TEST_ENVIRONMENT";
        string jsonWithEnvVariable = @"{""envValue"": ""@env('DAB_TEST_ENVIRONMENT')""}";

        // No environment File, No environment variable set in the system
        Assert.AreEqual(null, Environment.GetEnvironmentVariable(envVariableName));

        // Configuring environment variable in the system
        Environment.SetEnvironmentVariable(envVariableName, "TEST");

        // Test environment variable is correctly resolved in the config file
        string? resolvedJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(jsonWithEnvVariable);
        Assert.IsNotNull(resolvedJson);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""envValue"": ""TEST""}"), JObject.Parse(resolvedJson)));

        // removing Environment variable from the System
        Environment.SetEnvironmentVariable(envVariableName, null);
        Assert.ThrowsException<DataApiBuilderException>(() =>
            RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(jsonWithEnvVariable),
            $"Environmental Variable, {envVariableName}, not found.");
    }

    /// <summary>
    /// This test creates a .env file and adds a variable and verifies that the variable is
    /// correctly consumed.
    /// For this test there were no existing environment variables. The values are picked up
    /// directly from the `.env` file.
    /// </summary>
    [TestMethod]
    public void TestEnvironmentFileIsConsumedCorrectly()
    {
        string envVariableName = "DAB_TEST_ENVIRONMENT";
        string jsonWithEnvVariable = @"{""envValue"": ""@env('DAB_TEST_ENVIRONMENT')""}";

        // No environment File, No environment variable set in the system
        Assert.IsNull(Environment.GetEnvironmentVariable(envVariableName));

        // Creating environment variable file
        File.Create("test.env").Close();
        File.WriteAllText("test.env", $"{envVariableName}=DEVELOPMENT");
        DotNetEnv.Env.Load("test.env");

        // Test environment variable is picked up from the .env file and is correctly resolved in the config file.
        Assert.AreEqual("DEVELOPMENT", Environment.GetEnvironmentVariable(envVariableName));
        string? resolvedJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(jsonWithEnvVariable);
        Assert.IsNotNull(resolvedJson);
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse(@"{""envValue"": ""DEVELOPMENT""}"), JObject.Parse(resolvedJson)));
    }

    /// <summary>
    /// This test setups a environment variable in the system and also creates a .env file containing
    /// same variable with different value to show the value stored in .env file is given
    /// precedence over the system variable.
    /// It also verifies that if the .env file is removed, the existing variable is used.
    /// </summary>
    [TestMethod]
    public void TestPrecedenceOfEnvironmentFileOverExistingVariables()
    {
        string envVariableName = "DAB_TEST_ENVIRONMENT";

        // The variable set in the .env file takes precedence over the environment value set in the system.
        Environment.SetEnvironmentVariable(envVariableName, "TEST");

        // Creating environment variable file
        File.Create("test.env").Close();
        File.WriteAllText("test.env", $"{envVariableName}=DEVELOPMENT");
        DotNetEnv.Env.Load("test.env");     // It contains value DEVELOPMENT
        Assert.AreEqual("DEVELOPMENT", Environment.GetEnvironmentVariable(envVariableName));

        // If a variable is not present in the .env file then the system defined variable would be used if defined.
        Environment.SetEnvironmentVariable("HOSTING_TEST_ENVIRONMENT", "PHOENIX_TEST");
        string? resolvedJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(
            @"{
                ""envValue"": ""@env('DAB_TEST_ENVIRONMENT')"",
                ""hostingEnvValue"": ""@env('HOSTING_TEST_ENVIRONMENT')""
                }"
        );
        Assert.IsNotNull(resolvedJson);
        Assert.IsTrue(JToken.DeepEquals(
            JObject.Parse(
                @"{
                    ""envValue"": ""DEVELOPMENT"",
                    ""hostingEnvValue"": ""PHOENIX_TEST""
                }"),
            JObject.Parse(resolvedJson)));

        // Removing the .env file it will then use the value of system environment variable.
        Environment.SetEnvironmentVariable(envVariableName, "TEST");
        File.Delete("test.env");
        DotNetEnv.Env.Load("test.env");
        Assert.AreEqual("TEST", Environment.GetEnvironmentVariable(envVariableName));
    }

    [TestCleanup]
    public void CleanUp()
    {
        if (File.Exists("test.env"))
        {
            File.Delete("test.env");
        }
    }
}
