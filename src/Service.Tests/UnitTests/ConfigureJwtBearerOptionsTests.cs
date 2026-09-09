// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass]
    public class ConfigureJwtBearerOptionsTests
    {
        [TestMethod]
        public void Configure_HotReloadDisabled_DoesNotChangeOptions()
        {
            RuntimeConfigProvider provider = CreateProvider(
                CreateConfig(HostMode.Production, new AuthenticationOptions("AzureAD", new JwtOptions("aud", "https://issuer"))));
            provider.IsLateConfigured = true;
            JwtBearerOptions options = new() { MapInboundClaims = true, Audience = "original" };

            new ConfigureJwtBearerOptions(provider).Configure("Bearer", options);

            Assert.IsTrue(options.MapInboundClaims);
            Assert.AreEqual("original", options.Audience);
        }

        [TestMethod]
        public void Configure_MissingAuthentication_DoesNotChangeOptions()
        {
            RuntimeConfigProvider provider = CreateProvider(CreateConfig(HostMode.Development, authentication: null));
            JwtBearerOptions options = new() { MapInboundClaims = true };

            new ConfigureJwtBearerOptions(provider).Configure("Bearer", options);

            Assert.IsTrue(options.MapInboundClaims);
            Assert.IsNull(options.Audience);
        }

        [DataTestMethod]
        [DataRow("Custom")]
        [DataRow("AzureAD")]
        [DataRow("EntraID")]
        public void Configure_JwtAuthentication_UpdatesAllTokenOptions(string providerName)
        {
            RuntimeConfigProvider provider = CreateProvider(CreateConfig(
                HostMode.Development,
                new AuthenticationOptions(providerName, new JwtOptions("api://audience", "https://issuer.example"))));
            JwtBearerOptions options = new() { MapInboundClaims = true };
            ConfigureJwtBearerOptions configurator = new(provider);

            configurator.Configure(options);

            Assert.IsFalse(options.MapInboundClaims);
            Assert.AreEqual("api://audience", options.Audience);
            Assert.AreEqual("https://issuer.example", options.Authority);
            Assert.AreEqual("api://audience", options.TokenValidationParameters.ValidAudience);
            Assert.AreEqual("https://issuer.example", options.TokenValidationParameters.ValidIssuer);
            Assert.AreEqual(AuthenticationOptions.ROLE_CLAIM_TYPE, options.TokenValidationParameters.RoleClaimType);
        }

        private static RuntimeConfigProvider CreateProvider(RuntimeConfig config)
        {
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem())
            {
                RuntimeConfig = config
            };
            return new RuntimeConfigProvider(loader);
        }

        private static RuntimeConfig CreateConfig(HostMode mode, AuthenticationOptions? authentication)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "Server=test;", null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: authentication, Mode: mode)),
                Entities: new(new Dictionary<string, Entity>()));
        }
    }
}
