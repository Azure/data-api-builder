using System;
using System.Collections.Generic;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Azure.DataGateway.Service.Tests.Configuration
{
    [TestClass]
    public class AuthenticationConfigValidatorUnitTests
    {
        private const string DEFAULT_RESOLVER_FILE = "sql-config.json";
        private const string DEFAULT_ISSUER = "https://login.microsoftonline.com";

        #region Positive Tests
        [TestMethod("AuthN config passes validation with EasyAuth as default Provider")]
        public void ValidateEasyAuthConfig()
        {
            RuntimeConfig config =
                CreateRuntimeConfigWithAuthN(new AuthenticationConfig());
            RuntimeConfigValidator configValidator = new(config);

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
                Issuer: "https://login.microsoftonline.com/common",
                IssuerKey: "XYZ");
            AuthenticationConfig authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithAuthN(authNConfig);

            RuntimeConfigValidator configValidator = new(config);

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
                Issuer: string.Empty,
                IssuerKey: string.Empty);
            AuthenticationConfig authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);

            RuntimeConfig config = CreateRuntimeConfigWithAuthN(authNConfig);

            RuntimeConfigValidator configValidator = new(config);
            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER,
                IssuerKey: "XYZ");
            authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);
            config = CreateRuntimeConfigWithAuthN(authNConfig);

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
                Issuer: string.Empty,
                IssuerKey: string.Empty);
            AuthenticationConfig authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithAuthN(authNConfig);
            RuntimeConfigValidator configValidator = new(config);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER,
                IssuerKey: "XYZ");
            authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            config = CreateRuntimeConfigWithAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                configValidator.ValidateConfig();
            });
        }
        #endregion
        #region Helper Functions
        private static RuntimeConfig CreateRuntimeConfigWithAuthN(AuthenticationConfig authNConfig)
        {
            DataSource dataSource = new(
                DatabaseType: DatabaseType.mssql,
                ResolverConfigFile: DEFAULT_RESOLVER_FILE);

            HostGlobalSettings hostGlobal = new(Authentication: authNConfig);
            JsonElement hostGlobalJson = JsonSerializer.SerializeToElement(hostGlobal);
            Dictionary<GlobalSettingsType, object> runtimeSettings = new();
            runtimeSettings.TryAdd(GlobalSettingsType.Host, hostGlobalJson);
            Dictionary<string, Entity> entities = new();
            RuntimeConfig config = new(
                Schema: RuntimeConfig.SCHEMA,
                DataSource: dataSource,
                CosmosDb: null,
                MsSql: null,
                PostgreSql: null,
                MySql: null,
                RuntimeSettings: runtimeSettings,
                Entities: entities
            );

            config.DetermineGlobalSettings();
            return config;
        }
        #endregion
    }
}
