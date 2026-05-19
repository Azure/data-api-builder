// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class AuthenticationOptionsValidatorUnitTests
    {
        private const string DEFAULT_CONNECTION_STRING = "Server=tcp:127.0.0.1";
        private const string DEFAULT_ISSUER = "https://login.microsoftonline.com";

        private MockFileSystem _mockFileSystem;
        private FileSystemRuntimeConfigLoader _runtimeConfigLoader;
        private RuntimeConfigProvider _runtimeConfigProvider;
        private RuntimeConfigValidator _runtimeConfigValidator;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockFileSystem = new MockFileSystem();
            _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_mockFileSystem);
            _runtimeConfigProvider = new RuntimeConfigProvider(_runtimeConfigLoader);
            Mock<ILogger<RuntimeConfigValidator>> logger = new();
            _runtimeConfigValidator = new RuntimeConfigValidator(_runtimeConfigProvider, _mockFileSystem, logger.Object);
        }

        [TestMethod("AuthN config passes validation with EasyAuth as default Provider")]
        public void ValidateEasyAuthConfig()
        {
            RuntimeConfig config =
                CreateRuntimeConfigWithOptionalAuthN(new AuthenticationOptions(EasyAuthType.AppService.ToString(), null));

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            // Since we added the config file to the filesystem above after the config loader was initialized
            // in TestInitialize, we need to update the ConfigfileName, otherwise it will be an empty string.
            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [DataTestMethod("AuthN validation passes when all values are provided when provider not EasyAuth")]
        [DataRow("AzureAD")]
        [DataRow("EntraID")]
        public void ValidateJwtConfigParamsSet(string authenticationProvider)
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: "https://login.microsoftonline.com/common");
            AuthenticationOptions authNConfig = new(
                Provider: authenticationProvider,
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [DataTestMethod("Custom JWT roles settings validation passes when valid")]
        [DataRow("realm_access.roles", null, null)]
        [DataRow(null, "delimited-string", null)]
        [DataRow("['app.roles']", "array", null)]
        [DataRow("resource_access['dab-api'].roles", "array", null)]
        [DataRow("@env('JWT_ROLES_PATH')", "array", null)]
        [DataRow("scope", "delimited-string", "@env('JWT_ROLES_DELIMITER')")]
        [DataRow("scope", "delimited-string", "@akv('jwt-roles-delimiter')")]
        public void ValidateCustomJwtRolesSettings(string rolesPath, string rolesFormat, string rolesDelimiter)
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: "https://login.microsoftonline.com/common",
                RolesPath: rolesPath,
                RolesFormat: rolesFormat,
                RolesDelimiter: rolesDelimiter);
            AuthenticationOptions authNConfig = new(
                Provider: "Custom",
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [TestMethod("Custom JWT roles settings fail for non-Custom providers")]
        public void ValidateCustomJwtRolesSettingsFailForNonCustomProvider()
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: DEFAULT_ISSUER,
                RolesPath: "groups");
            AuthenticationOptions authNConfig = new(
                Provider: "EntraID",
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            });
        }

        [DataTestMethod("Custom JWT roles settings fail with invalid values")]
        [DataRow("groups[0]", null, null)]
        [DataRow("realm_access..roles", null, null)]
        [DataRow("", null, null)]
        [DataRow("   ", null, null)]
        [DataRow(null, "semicolon-delimited", null)]
        [DataRow(null, "@env('JWT_ROLES_FORMAT')", null)]
        [DataRow(null, "array", ",")]
        [DataRow(null, "string", ",")]
        [DataRow(null, "delimited-string", "")]
        public void ValidateCustomJwtRolesSettingsFailWithInvalidValues(string rolesPath, string rolesFormat, string rolesDelimiter)
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: DEFAULT_ISSUER,
                RolesPath: rolesPath,
                RolesFormat: rolesFormat,
                RolesDelimiter: rolesDelimiter);
            AuthenticationOptions authNConfig = new(
                Provider: "Custom",
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            });
        }

        [TestMethod("AuthN validation passes when no authN section in the config.")]
        public void ValidateAuthNSectionNotNecessary()
        {
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN();
            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [DataTestMethod("AuthN validation fails when either Issuer or Audience not provided not EasyAuth")]
        [DataRow("AzureAD")]
        [DataRow("EntraID")]
        public void ValidateFailureWithIncompleteJwtConfig(string authenticationProvider)
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: string.Empty);
            AuthenticationOptions authNConfig = new(
                Provider: authenticationProvider,
                Jwt: jwt);

            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER);
            authNConfig = new(
                Provider: authenticationProvider,
                Jwt: jwt);
            config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            });
        }

        [TestMethod("AuthN validation fails when either Issuer or Audience are provided for EasyAuth")]
        public void ValidateFailureWithUnneededEasyAuthConfig()
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: string.Empty);
            AuthenticationOptions authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER);
            authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfigProperties();
            });
        }

        [TestMethod("Unauthenticated provider is correctly identified by IsUnauthenticatedAuthenticationProvider method")]
        public void ValidateUnauthenticatedProviderIdentification()
        {
            // Test with Unauthenticated provider
            AuthenticationOptions unauthenticatedOptions = new(Provider: "Unauthenticated");
            Assert.IsTrue(unauthenticatedOptions.IsUnauthenticatedAuthenticationProvider());

            // Test case-insensitivity
            AuthenticationOptions unauthenticatedOptionsLower = new(Provider: "unauthenticated");
            Assert.IsTrue(unauthenticatedOptionsLower.IsUnauthenticatedAuthenticationProvider());

            // Test that other providers are not identified as Unauthenticated
            AuthenticationOptions appServiceOptions = new(Provider: "AppService");
            Assert.IsFalse(appServiceOptions.IsUnauthenticatedAuthenticationProvider());

            AuthenticationOptions simulatorOptions = new(Provider: "Simulator");
            Assert.IsFalse(simulatorOptions.IsUnauthenticatedAuthenticationProvider());
        }

        private static RuntimeConfig CreateRuntimeConfigWithOptionalAuthN(AuthenticationOptions authNConfig = null)
        {
            DataSource dataSource = new(DatabaseType.MSSQL, DEFAULT_CONNECTION_STRING, new());

            HostOptions hostOptions = new(Cors: null, Authentication: authNConfig);
            RuntimeConfig config = new(
                Schema: FileSystemRuntimeConfigLoader.SCHEMA,
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(),
                    Host: hostOptions
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
            return config;
        }
    }
}
