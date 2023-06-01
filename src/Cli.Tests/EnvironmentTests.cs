// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests;

/// <summary>
/// Contains test involving environment variables.
/// </summary>
[TestClass]
public class EnvironmentTests
{
    public const string TEST_ENV_VARIABLE = "DAB_TEST_ENVIRONMENT";
    /// <summary>
    /// Test to verify that environment variable setup in the system is picked up correctly
    /// when no .env file is present.
    /// </summary>
    [TestMethod]
    public void TestEnvironmentVariableIsConsumedCorrectly()
    {
        string jsonWithEnvVariable = @"{""envValue"": ""@env('DAB_TEST_ENVIRONMENT')""}";

        // No environment File, No environment variable set in the system
        Assert.AreEqual(null, Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));

        // Configuring environment variable in the system
        Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, "TEST");

        // Test environment variable is correctly resolved in the config file
        string? resolvedJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(jsonWithEnvVariable);
        Assert.IsNotNull(resolvedJson);
        Assert.IsTrue(JToken.DeepEquals(
            JObject.Parse(@"{""envValue"": ""TEST""}"),
            JObject.Parse(resolvedJson)), "JSON resolved with environment variable correctly");

        // removing Environment variable from the System
        Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, null);
        Assert.ThrowsException<DataApiBuilderException>(() =>
            RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(jsonWithEnvVariable),
            $"Environmental Variable, {TEST_ENV_VARIABLE}, not found.");
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
        string jsonWithEnvVariable = @"{""envValue"": ""@env('DAB_TEST_ENVIRONMENT')""}";

        // No environment File, No environment variable set in the system
        Assert.IsNull(Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));

        // Creating environment variable file
        File.Create(".env").Close();
        File.WriteAllText(".env", $"{TEST_ENV_VARIABLE}=DEVELOPMENT");
        DotNetEnv.Env.Load();

        // Test environment variable is picked up from the .env file and is correctly resolved in the config file.
        Assert.AreEqual("DEVELOPMENT", Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));
        string? resolvedJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(jsonWithEnvVariable);
        Assert.IsNotNull(resolvedJson);
        Assert.IsTrue(JToken.DeepEquals(
            JObject.Parse(@"{""envValue"": ""DEVELOPMENT""}"),
            JObject.Parse(resolvedJson)), "JSON resolved with environment variable correctly");
    }

    /// <summary>
    /// This test setups a environment variable in the system and also creates a .env file containing
    /// same variable with different value to show the value stored in .env file is given
    /// precedence over the system variable.
    /// </summary>
    [TestMethod]
    public void TestPrecedenceOfEnvironmentFileOverExistingVariables()
    {
        // The variable set in the .env file takes precedence over the environment value set in the system.
        Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, "TEST");

        // Creating environment variable file
        File.Create(".env").Close();
        File.WriteAllText(".env", $"{TEST_ENV_VARIABLE}=DEVELOPMENT");
        DotNetEnv.Env.Load();     // It contains value DEVELOPMENT
        Assert.AreEqual("DEVELOPMENT", Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));

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
            JObject.Parse(resolvedJson)), "JSON resolved with environment variable correctly");

        // Removing the .env file it will then use the value of system environment variable.

    }

    /// <summary>
    /// Test to verify that no error is thrown if .env file is not present, and existing system variables is used.
    /// </summary>
    [TestMethod]
    public void TestSystemEnvironmentVariableIsUsedInAbsenceOfEnvironmentFile()
    {
        Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, "TEST");
        Assert.IsFalse(File.Exists(".env"));
        DotNetEnv.Env.Load(); // No error is thrown
        Assert.AreEqual("TEST", Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));
    }

    /// <summary>
    /// Test to verify that if the environment variables are not resolved correctly, runtime engine will not start.
    /// Here, in the first scenario, engine failed to start because the variable defined in the environment file
    /// is typed incorrectly and do not match with the one present in the config.
    /// </summary>
    [DataRow("COMM_STRINX=test_connection_string", true, DisplayName = "Incorrect Variable name used in the environment file.")]
    [DataRow("CONN_STRING=test_connection_string", false, DisplayName = "Correct Variable name used in the environment file.")]
    [DataTestMethod]
    public void TestFailureToStartWithUnresolvedJsonConfig(
        string environmentFileContent,
        bool isFailure
    )
    {
        // Creating environment variable file
        File.Create(".env").Close();
        File.WriteAllText(".env", environmentFileContent);
        if (File.Exists(TEST_RUNTIME_CONFIG_FILE))
        {
            File.Delete(TEST_RUNTIME_CONFIG_FILE);
        }

        string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql", "--connection-string", "@env('CONN_STRING')" };
        Program.Main(initArgs);

        using Process process = ExecuteDabCommand(
            "start",
            $"-c {TEST_RUNTIME_CONFIG_FILE}"
        );

        string? output = process.StandardOutput.ReadToEnd();
        Assert.IsNotNull(output);
        if (isFailure)
        {
            Assert.IsTrue(output.Contains("Error: Failed due to: Environmental Variable, CONN_STRING, not found."));
            Assert.IsTrue(output.Contains("Error: Failed to start the engine."));
            Assert.IsFalse(output.Contains("Starting the runtime engine..."));
        }
        else
        {
            Assert.IsTrue(output.Contains("Starting the runtime engine..."));
        }
    }

    [TestCleanup]
    public void CleanUp()
    {
        if (File.Exists(".env"))
        {
            File.Delete(".env");
        }
    }
}
