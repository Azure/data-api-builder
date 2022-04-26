using System;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Configuration
{
    [TestClass]
    public class AuthenticationConfigValidatorUnitTests
    {
        private const string DEFAULT_CONNECTION_STRING = "Server=tcp:127.0.0.1";
        private const string DEFAULT_RESOLVER_FILE = "cosmos-config.json";
        private const string DEFAULT_ISSUER = "https://login.microsoftonline.com";

        #region Positive Tests
        [TestMethod("AuthN config passes validation with EasyAuth as Provider")]
        public void ValidateEasyAuthConfig()
        {
            DataGatewayConfig config = CreateDGConfig();
            config.Authentication = new AuthenticationProviderConfig()
            {
                Provider = "EasyAuth"
            };

            DataGatewayConfigPostConfiguration dgPostConfig = new();

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
            DataGatewayConfig config = CreateDGConfig();
            config.Authentication = new AuthenticationProviderConfig()
            {
                Provider = "AzureAD",
                Issuer = "https://login.microsoftonline.com/common",
                Audience = "12345"
            };

            DataGatewayConfigPostConfiguration dgPostConfig = new();

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
        [TestMethod("Authentication config section with no values set for params fails validation")]
        public void ValidateAuthenticationConfigSet()
        {
            DataGatewayConfig config = CreateDGConfig();
            config.Authentication = new AuthenticationProviderConfig();

            DataGatewayConfigPostConfiguration dgPostConfig = new();

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });
        }

        [TestMethod("AuthN validation fails when either Issuer or Audience not provided not EasyAuth")]
        public void ValidateFailureWithIncompleteJwtConfig()
        {
            DataGatewayConfig config = CreateDGConfig();
            config.Authentication = new AuthenticationProviderConfig()
            {
                Provider = "AzureAD",
                Issuer = "",
                Audience = "12345"
            };

            DataGatewayConfigPostConfiguration dgPostConfig = new();

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });

            config.Authentication = new AuthenticationProviderConfig()
            {
                Provider = "AzureAD",
                Issuer = DEFAULT_ISSUER,
                Audience = ""
            };

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });
        }

        [TestMethod("AuthN validation fails when either Issuer or Audience are provided for EasyAuth")]
        public void ValidateFailureWithUnneededEasyAuthConfig()
        {
            DataGatewayConfig config = CreateDGConfig();
            config.Authentication = new AuthenticationProviderConfig()
            {
                Provider = "EasyAuth",
                Issuer = DEFAULT_ISSUER,
                Audience = ""
            };

            DataGatewayConfigPostConfiguration dgPostConfig = new();

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });

            config.Authentication = new AuthenticationProviderConfig()
            {
                Provider = "EasyAuth",
                Issuer = "",
                Audience = "12345"
            };

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                dgPostConfig.PostConfigure(name: string.Empty, options: config);
            });
        }
        #endregion
        #region Helper Functions
        private static DataGatewayConfig CreateDGConfig()
        {
            DatabaseConnectionConfig connection = new()
            {
                ConnectionString = DEFAULT_CONNECTION_STRING
            };

            DataGatewayConfig config = new()
            {
                DatabaseType = DatabaseType.mssql,
                DatabaseConnection = connection,
                ResolverConfigFile = DEFAULT_RESOLVER_FILE
            };

            return config;
        }
        #endregion
    }
}
