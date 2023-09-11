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
                CreateRuntimeConfigWithOptionalAuthN(new AuthenticationOptions(EasyAuthType.StaticWebApps.ToString(), null));

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            // Since we added the config file to the filesystem above after the config loader was initialized
            // in TestInitialize, we need to update the ConfigfileName, otherwise it will be an empty string.
            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfig();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [TestMethod("AuthN validation passes when all values are provided when provider not EasyAuth")]
        public void ValidateJwtConfigParamsSet()
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: "https://login.microsoftonline.com/common");
            AuthenticationOptions authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);
            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfig();
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
            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            try
            {
                _runtimeConfigValidator.ValidateConfig();
            }
            catch (NotSupportedException e)
            {
                Assert.Fail(message: e.Message);
            }
        }

        [TestMethod("AuthN validation fails when either Issuer or Audience not provided not EasyAuth")]
        public void ValidateFailureWithIncompleteJwtConfig()
        {
            JwtOptions jwt = new(
                Audience: "12345",
                Issuer: string.Empty);
            AuthenticationOptions authNConfig = new(
                Provider: "AzureAD",
                Jwt: jwt);

            RuntimeConfig config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            _mockFileSystem.AddFile(
                FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME,
                new MockFileData(config.ToJson())
            );

            _runtimeConfigLoader.UpdateConfigFilePath(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfig();
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
                _runtimeConfigValidator.ValidateConfig();
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
                _runtimeConfigValidator.ValidateConfig();
            });

            jwt = new(
                Audience: string.Empty,
                Issuer: DEFAULT_ISSUER);
            authNConfig = new(Provider: "EasyAuth", Jwt: jwt);
            config = CreateRuntimeConfigWithOptionalAuthN(authNConfig);

            Assert.ThrowsException<NotSupportedException>(() =>
            {
                _runtimeConfigValidator.ValidateConfig();
            });
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
                    Host: hostOptions
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
            return config;
        }
    }
}
