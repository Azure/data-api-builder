using System;
using System.Collections.Generic;
using Azure.DataGateway.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Configuration
{
    [TestClass]
    public class AuthenticationConfigValidatorUnitTests
    {
        private const string DEFAULT_CONNECTION_STRING = "Server=tcp:127.0.0.1";
        private const string DEFAULT_RESOLVER_FILE = "sql-config.json";
        private const string DEFAULT_ISSUER = "https://login.microsoftonline.com";

        #region Positive Tests
        [TestMethod("AuthN config passes validation with EasyAuth as default Provider")]
        public void ValidateEasyAuthConfig()
        {
            RuntimeConfig config =
                CreateRuntimeConfigWithAuthN(new AuthenticationConfig());
            RuntimeConfigPostConfiguration dgPostConfig = new();

            try
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
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

            RuntimeConfigPostConfiguration dgPostConfig = new();

            try
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
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

            RuntimeConfigPostConfiguration dgPostConfig = new();
            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
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
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
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
            RuntimeConfigPostConfiguration dgPostConfig = new();

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER,
                IssuerKey: "XYZ");
            authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            config = CreateRuntimeConfigWithAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });
        }
        #endregion
        #region Helper Functions
        private static RuntimeConfig CreateRuntimeConfigWithAuthN(AuthenticationConfig authNConfig)
        {
            DataSource dataSource = new()
            {
                DatabaseType = DatabaseType.mssql,
                ConnectionString = DEFAULT_CONNECTION_STRING,
                ResolverConfigFile = DEFAULT_RESOLVER_FILE
            };

            HostGlobalSettings hostGlobal = new(Authentication: authNConfig);
            Dictionary<GlobalSettingsType, object> runtimeSettings = new();
            runtimeSettings.TryAdd(GlobalSettingsType.Host, hostGlobal);
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

            return config;
        }
        #endregion
    }
}
