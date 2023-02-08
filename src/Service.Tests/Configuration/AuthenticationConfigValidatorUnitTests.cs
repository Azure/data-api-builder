// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class AuthenticationConfigValidatorUnitTests
    {
        private const string DEFAULT_CONNECTION_STRING = "Server=tcp:127.0.0.1";
        private const string DEFAULT_ISSUER = "https://login.microsoftonline.com";

        #region Positive Tests
        [TestMethod("AuthN config passes validation with EasyAuth as default Provider")]
        public void ValidateEasyAuthConfig()
        {
            RuntimeConfig config =
                CreateRuntimeConfigWithOptionalAuthN(new AuthenticationConfig(EasyAuthType.StaticWebApps.ToString()));

            RuntimeConfigValidator configValidator = GetMockConfigValidator(ref config);

            try
            {
                configValidator.ValidateConfig();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [TestMethod("AuthN validation passes when all values are provided when provider not EasyAuth")]
        public void ValidateJwtConfigParamsSet()
        {
            Jwt jwt = new(
                Audience: "12345",
                Issuer: "https://login.microsoftonline.com/common");
            AuthenticationConfig authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            RuntimeConfigValidator configValidator = GetMockConfigValidator(ref config);

            try
            {
                configValidator.ValidateConfig();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [TestMethod("AuthN validation passes when no authN section in the config.")]
        public void ValidateAuthNSectionNotNecessary()
        {
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN();
            RuntimeConfigValidator configValidator = GetMockConfigValidator(ref config);

            try
            {
                configValidator.ValidateConfig();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        #endregion

        #region Negative Tests

        [TestMethod("AuthN validation fails when either Issuer or Audience not provided not EasyAuth")]
        public void ValidateFailureWithIncompleteJwtConfig()
        {
            Jwt jwt = new(
                Audience: "12345",
                Issuer: string.Empty);
            AuthenticationConfig authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);

            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            RuntimeConfigValidator configValidator = GetMockConfigValidator(ref config);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER);
            authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);
            config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });
        }

        [TestMethod("AuthN validation fails when either Issuer or Audience are provided for EasyAuth")]
        public void ValidateFailureWithUnneededEasyAuthConfig()
        {
            Jwt jwt = new(
                Audience: "12345",
                Issuer: string.Empty);
            AuthenticationConfig authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            RuntimeConfigValidator configValidator = GetMockConfigValidator(ref config);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER);
            authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });
        }
        #endregion

        #region Helper Functions
        private static RuntimeConfig
            CreateRuntimeConfigWithOptionalAuthN(
                AuthenticationConfig authNConfig = null)
        {
            DataSource dataSource = new(
                DatabaseType: DatabaseType.mssql)
            {
                ConnectionString = DEFAULT_CONNECTION_STRING
            };

            HostGlobalSettings hostGlobal = new(Authentication: authNConfig);
            JsonElement hostGlobalJson = JsonSerializer.SerializeToElement(hostGlobal);
            Dictionary<GlobalSettingsType, object> runtimeSettings = new();
            runtimeSettings.TryAdd(GlobalSettingsType.Host, hostGlobalJson);
            Dictionary<string, Entity> entities = new();
            RuntimeConfig config = new(
                Schema: RuntimeConfig.SCHEMA,
                DataSource: dataSource,
                RuntimeSettings: runtimeSettings,
                Entities: entities
            );

            config.DetermineGlobalSettings();
            return config;
        }

        public static RuntimeConfigValidator GetMockConfigValidator(ref RuntimeConfig config)
        {
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(config);
            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            RuntimeConfigValidator configValidator =
                new(configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object);
            return configValidator;
        }
        #endregion
    }
}
