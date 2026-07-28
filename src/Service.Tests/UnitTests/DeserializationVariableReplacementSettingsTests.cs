// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="DeserializationVariableReplacementSettings"/> covering environment
    /// variable replacement, Azure Key Vault local-file secret resolution, and remote client
    /// construction (no network access). Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class DeserializationVariableReplacementSettingsTests
    {
        private const string ENV_VAR = "DAB_TEST_REPLACEMENT_VAR";

        /// <summary>
        /// Applies the first matching replacement strategy to the given input.
        /// </summary>
        private static string ApplyFirstMatch(DeserializationVariableReplacementSettings settings, string input)
        {
            foreach (System.Collections.Generic.KeyValuePair<Regex, Func<Match, string>> strategy in settings.ReplacementStrategies)
            {
                Match match = strategy.Key.Match(input);
                if (match.Success)
                {
                    return strategy.Value(match);
                }
            }

            return input;
        }

        [TestMethod]
        public void Constructor_NoReplacement_RegistersNoStrategies()
        {
            DeserializationVariableReplacementSettings settings = new();

            Assert.AreEqual(0, settings.ReplacementStrategies.Count);
        }

        [TestMethod]
        public void Constructor_EnvReplacementEnabled_RegistersOneStrategy()
        {
            DeserializationVariableReplacementSettings settings = new(doReplaceEnvVar: true);

            Assert.AreEqual(1, settings.ReplacementStrategies.Count);
            Assert.IsTrue(settings.DoReplaceEnvVar);
        }

        [TestMethod]
        public void EnvReplacement_VariableSet_ReturnsValue()
        {
            string? original = Environment.GetEnvironmentVariable(ENV_VAR);
            try
            {
                Environment.SetEnvironmentVariable(ENV_VAR, "resolved-value");
                DeserializationVariableReplacementSettings settings = new(doReplaceEnvVar: true);

                string result = ApplyFirstMatch(settings, $"@env('{ENV_VAR}')");

                Assert.AreEqual("resolved-value", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ENV_VAR, original);
            }
        }

        [TestMethod]
        public void EnvReplacement_MissingVariable_ThrowMode_Throws()
        {
            string? original = Environment.GetEnvironmentVariable(ENV_VAR);
            try
            {
                Environment.SetEnvironmentVariable(ENV_VAR, null);
                DeserializationVariableReplacementSettings settings = new(
                    doReplaceEnvVar: true,
                    envFailureMode: EnvironmentVariableReplacementFailureMode.Throw);

                Assert.ThrowsException<DataApiBuilderException>(
                    () => ApplyFirstMatch(settings, $"@env('{ENV_VAR}')"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(ENV_VAR, original);
            }
        }

        [TestMethod]
        public void EnvReplacement_MissingVariable_IgnoreMode_ReturnsOriginal()
        {
            string? original = Environment.GetEnvironmentVariable(ENV_VAR);
            try
            {
                Environment.SetEnvironmentVariable(ENV_VAR, null);
                DeserializationVariableReplacementSettings settings = new(
                    doReplaceEnvVar: true,
                    envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

                string result = ApplyFirstMatch(settings, $"@env('{ENV_VAR}')");

                Assert.AreEqual($"@env('{ENV_VAR}')", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ENV_VAR, original);
            }
        }

        [TestMethod]
        public void AkvFileReplacement_ResolvesSecretsFromFile()
        {
            string path = WriteAkvFile(
                "# a comment",
                "secret-name=secretvalue",
                "quoted=\"quotedvalue\"",
                "malformed-line-without-equals",
                "=leadingequals",
                "dup=first",
                "dup=second");
            try
            {
                DeserializationVariableReplacementSettings settings = new(
                    azureKeyVaultOptions: new AzureKeyVaultOptions(endpoint: path),
                    doReplaceAkvVar: true);

                Assert.AreEqual(1, settings.ReplacementStrategies.Count);
                Assert.AreEqual("secretvalue", ApplyFirstMatch(settings, "@akv('secret-name')"));
                Assert.AreEqual("quotedvalue", ApplyFirstMatch(settings, "@akv('quoted')"));
                // Duplicate keys keep the first value.
                Assert.AreEqual("first", ApplyFirstMatch(settings, "@akv('dup')"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AkvFileReplacement_InvalidSecretName_Throws()
        {
            string path = WriteAkvFile("valid=ok");
            try
            {
                DeserializationVariableReplacementSettings settings = new(
                    azureKeyVaultOptions: new AzureKeyVaultOptions(endpoint: path),
                    doReplaceAkvVar: true);

                Assert.ThrowsException<DataApiBuilderException>(
                    () => ApplyFirstMatch(settings, "@akv('bad name')"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AkvFileReplacement_MissingSecret_IgnoreMode_ReturnsOriginal()
        {
            string path = WriteAkvFile("present=value");
            try
            {
                DeserializationVariableReplacementSettings settings = new(
                    azureKeyVaultOptions: new AzureKeyVaultOptions(endpoint: path),
                    doReplaceAkvVar: true,
                    envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore);

                Assert.AreEqual("@akv('absent')", ApplyFirstMatch(settings, "@akv('absent')"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [DataTestMethod]
        [DataRow(AKVRetryPolicyMode.Fixed)]
        [DataRow(AKVRetryPolicyMode.Exponential)]
        public void Constructor_RemoteAkvEndpointWithRetryPolicy_RegistersStrategy(AKVRetryPolicyMode mode)
        {
            AzureKeyVaultOptions options = new(
                endpoint: "https://fake-vault.vault.azure.net/",
                retryPolicy: new AKVRetryPolicyOptions(mode: mode, maxCount: 2, delaySeconds: 1, maxDelaySeconds: 5, networkTimeoutSeconds: 10));

            DeserializationVariableReplacementSettings settings = new(
                azureKeyVaultOptions: options,
                doReplaceAkvVar: true);

            Assert.AreEqual(1, settings.ReplacementStrategies.Count);
        }

        [TestMethod]
        public void Constructor_RemoteAkvEndpointNoRetryPolicy_RegistersStrategy()
        {
            AzureKeyVaultOptions options = new(endpoint: "https://fake-vault.vault.azure.net/");

            DeserializationVariableReplacementSettings settings = new(
                azureKeyVaultOptions: options,
                doReplaceAkvVar: true);

            Assert.AreEqual(1, settings.ReplacementStrategies.Count);
        }

        private static string WriteAkvFile(params string[] lines)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dab-test-{Guid.NewGuid():N}.akv");
            File.WriteAllLines(path, lines);
            return path;
        }
    }
}
