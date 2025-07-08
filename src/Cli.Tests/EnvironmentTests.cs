// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.Converters;

namespace Cli.Tests
{
    /// <summary>
    /// Contains test involving environment variables.
    /// </summary>
    [TestClass]
    public class EnvironmentTests : VerifyBase
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private JsonSerializerOptions _options;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        /// <summary>
        /// Test initialize
        /// </summary>
        [TestInitialize]
        public void TestInitialize()
        {
            StringJsonConverterFactory converterFactory = new(EnvironmentVariableReplacementFailureMode.Throw);
            _options = new()
            {
                PropertyNameCaseInsensitive = true
            };
            _options.Converters.Add(converterFactory);
        }

        record TestObject(string? EnvValue, string? HostingEnvValue);

        public const string TEST_ENV_VARIABLE = "DAB_TEST_ENVIRONMENT";
        /// <summary>
        /// Test to verify that environment variable setup in the system is picked up correctly
        /// when no .env file is present.
        /// </summary>
        [TestMethod]
        public async Task TestEnvironmentVariableIsConsumedCorrectly()
        {
            string jsonWithEnvVariable = @"{""envValue"": ""@env('DAB_TEST_ENVIRONMENT')""}";

            // No environment File, No environment variable set in the system
            Assert.AreEqual(null, Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));

            // Configuring environment variable in the system
            Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, "TEST");

            // Test environment variable is correctly resolved in the config file
            TestObject? result = JsonSerializer.Deserialize<TestObject>(jsonWithEnvVariable, _options);
            await Verify(result);

            // removing Environment variable from the System
            Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, null);
            Assert.ThrowsException<DataApiBuilderException>(() =>
                JsonSerializer.Deserialize<TestObject>(jsonWithEnvVariable, _options),
                $"Environmental Variable, {TEST_ENV_VARIABLE}, not found.");
        }

        /// <summary>
        /// This test creates a .env file and adds a variable and verifies that the variable is
        /// correctly consumed.
        /// For this test there were no existing environment variables. The values are picked up
        /// directly from the `.env` file.
        /// </summary>
        [TestMethod]
        public Task TestEnvironmentFileIsConsumedCorrectly()
        {
            string jsonWithEnvVariable = @"{""envValue"": ""@env('DAB_TEST_ENVIRONMENT')""}";

            // No environment File, no environment variable set in the system
            Assert.IsNull(Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));

            // Creating environment variable file
            File.Create(".env").Close();
            File.WriteAllText(".env", $"{TEST_ENV_VARIABLE}=DEVELOPMENT");
            DotNetEnv.Env.Load();

            // Test environment variable is picked up from the .env file and is correctly resolved in the config file.
            Assert.AreEqual("DEVELOPMENT", Environment.GetEnvironmentVariable(TEST_ENV_VARIABLE));
            TestObject? result = JsonSerializer.Deserialize<TestObject>(jsonWithEnvVariable, _options);
            return Verify(result);
        }

        /// <summary>
        /// This test setups an environment variable in the system and also creates a .env file containing
        /// the same variable with different value. In such a case, the value stored for variable in .env file is given
        /// precedence over the value specified in the system.
        /// </summary>
        [TestMethod]
        public Task TestPrecedenceOfEnvironmentFileOverExistingVariables()
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
            TestObject? result = JsonSerializer.Deserialize<TestObject>(
                @"{
                ""envValue"": ""@env('DAB_TEST_ENVIRONMENT')"",
                ""hostingEnvValue"": ""@env('HOSTING_TEST_ENVIRONMENT')""
                }", options: _options);
            return Verify(result);
        }

        /// <summary>
        /// Test to verify that no error is thrown if .env file is not present, and existing system variables are used.
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
        /// Tests the behavior of the <c>Main</c> method when the <c>ASPNETCORE_URLS</c> environment variable is set to an invalid value.
        /// </summary>
        [TestMethod]
        public void TestStartWithEnvFileIsSuccessful()
        {
            string expectedEnvVarName = "CONN_STRING";
            BootstrapTestEnvironment(expectedEnvVarName + "=test_connection_string", expectedEnvVarName);

            // Trying to start the runtime engine
            using Process process = ExecuteDabCommand(
                "start",
                $"-c {TEST_RUNTIME_CONFIG_FILE}"
            );

            Assert.IsFalse(process.StandardError.BaseStream.CanSeek, "Should not be able to seek stream as there should be no errors.");
            process.Kill();
        }

        /// <summary>
        /// Validates that engine startup fails when the CONN_STRING environment
        /// variable is not found. This test simulates this by defining the
        /// environment variable COMM_STRINGX and not setting the expected
        /// variable CONN_STRING. This will cause an exception during the post
        /// processing of the deserialization of the config. We verify the expected
        /// error message returns.
        /// </summary>
        [TestMethod]
        public async Task FailureToStartEngineWhenEnvVarNamedWrong()
        {
            string expectedEnvVarName = "WRONG_CONN_STRING";
            BootstrapTestEnvironment("COMM_STRINX=test_connection_string", expectedEnvVarName);

            // Trying to start the runtime engine
            using Process process = ExecuteDabCommand(
                "start",
                $"-c {TEST_RUNTIME_CONFIG_FILE}"
            );

            string? output = await process.StandardError.ReadLineAsync();
            Assert.AreEqual("Deserialization of the configuration file failed during a post-processing step.", output);
            output = await process.StandardError.ReadToEndAsync();
            StringAssert.Contains(output, "Environmental Variable, "
                + expectedEnvVarName + ", not found.", StringComparison.Ordinal);
            process.Kill();
        }

        /// <summary>
        /// Setup test environment
        /// </summary>
        /// <param name="envFileContents"></param>
        /// <param name="connStringEnvName"></param>
        private static void BootstrapTestEnvironment(string envFileContents, string connStringEnvName)
        {
            // Creating environment variable file
            File.Create(".env").Close();
            File.WriteAllText(".env", envFileContents);
            if (File.Exists(TEST_RUNTIME_CONFIG_FILE))
            {
                File.Delete(TEST_RUNTIME_CONFIG_FILE);
            }

            string[] initArgs = { "init", "-c", TEST_RUNTIME_CONFIG_FILE, "--database-type", "mssql",
            "--connection-string", "@env('" + connStringEnvName + "')" };
            Program.Main(initArgs);
        }

        /// <summary>
        /// Test cleanup
        /// </summary>
        [TestCleanup]
        public void CleanUp()
        {
            if (File.Exists(".env"))
            {
                File.Delete(".env");
            }

            Environment.SetEnvironmentVariable(TEST_ENV_VARIABLE, null);
        }
    }

}
