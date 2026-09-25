// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Product;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Exercises the product veto with real provider connection-string serialization, without
    /// opening connections. Environment-sensitive tests run in isolation and restore prior values.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class ProductTelemetryOptOutTests
    {
        private const string PAYLOAD = "+XXSX||MMMM00MMM00MMMMMMMMM|MMM?MMMMMMMMM?+";
        private string? _originalProductOptOut;
        private string? _originalAppNameOptOut;
        private string? _originalHostLabel;

        [TestInitialize]
        public void SaveAndClearEnvironment()
        {
            _originalProductOptOut = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR);
            _originalAppNameOptOut = Environment.GetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR);
            _originalHostLabel = Environment.GetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV);
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
            Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, null);
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);
        }

        [TestCleanup]
        public void RestoreEnvironment()
        {
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, _originalProductOptOut);
            Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, _originalAppNameOptOut);
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, _originalHostLabel);
        }

        [DataTestMethod]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow(" \t\r\n ", false)]
        [DataRow("0", false)]
        [DataRow("false", false)]
        [DataRow(" FALSE ", false)]
        [DataRow("yes", false)]
        [DataRow("on", false)]
        [DataRow("01", false)]
        [DataRow("true1", false)]
        [DataRow("t r u e", false)]
        [DataRow("1", true)]
        [DataRow(" 1\t", true)]
        [DataRow("true", true)]
        [DataRow("TRUE", true)]
        [DataRow(" \tTrUe\r\n", true)]
        public void Policy_RecognizesOnlyExplicitOptOutValues(string? value, bool expected)
        {
            Assert.AreEqual(expected, ProductTelemetryPolicy.IsOptedOut(value));
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, value);
            Assert.AreEqual(expected, ProductTelemetryPolicy.IsOptedOut());
        }

        [DataTestMethod]
        [DataRow(true, "key")]
        [DataRow(false, "key")]
        [DataRow(true, "token")]
        [DataRow(false, "token")]
        [DataRow(true, "default")]
        [DataRow(false, "default")]
        public void CosmosClientApplicationNameHonorsTheUmbrellaOptOut(bool optedOut, string credentials)
        {
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, optedOut ? "1" : null);
            string connection = "AccountEndpoint=https://localhost:8081/;";
            if (credentials == "key")
            {
                connection += "AccountKey=" + Convert.ToBase64String(new byte[64]) + ";";
            }

            RuntimeConfig config = CreateConfig(new(DatabaseType.CosmosDB_NoSQL, connection));
            FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            using RuntimeConfigProvider provider = new(loader);
            if (credentials == "token")
            {
                provider.ManagedIdentityAccessToken[config.DefaultDataSourceName] = "synthetic-not-used";
            }

            CosmosClientProvider clients = new(provider);
            using CosmosClient client = clients.Clients.Values.Single()!;
            Assert.AreEqual(optedOut ? null : ProductInfo.GetDataApiBuilderUserAgent(), client.ClientOptions.ApplicationName);
            // Construction only: no request or credential token acquisition is performed.
        }

        [TestMethod]
        public void Policy_ValueOverloadDoesNotReadEnvironmentOrLegacyOptOut()
        {
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "1");
            Assert.IsFalse(ProductTelemetryPolicy.IsOptedOut(null));
            Assert.IsFalse(ProductTelemetryPolicy.IsOptedOut("false"));

            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
            Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, "1");
            Assert.IsFalse(ProductTelemetryPolicy.IsOptedOut());
            Assert.IsTrue(ProductTelemetryPolicy.IsOptedOut(" TrUe "));
        }

        [DataTestMethod]
        [DataRow("1", null, null)]
        [DataRow(" true ", "1", null)]
        [DataRow("TRUE", null, "dab_hosted")]
        [DataRow(" TrUe ", "1", "dab_hosted_")]
        [DataRow("1", "true", "custom_host")]
        public void GlobalOptOut_VetoesSegmentButLeavesPureCliEncodingIndependent(
            string productOptOut, string? legacyOptOut, string? hostLabel)
        {
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, hostLabel);
            DataSource source = new(DatabaseType.MSSQL, string.Empty);
            RuntimeConfig config = CreateConfig(source);
            string encoded = ApplicationNameTelemetry.EncodeTelemetryString(config, source);

            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, productOptOut);
            Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, legacyOptOut);

            Assert.AreEqual(string.Empty, ApplicationNameTelemetry.BuildApplicationNameSegment(config, source));
            Assert.AreEqual(encoded, ApplicationNameTelemetry.EncodeTelemetryString(config, source));
            Assert.IsTrue(encoded.Contains("+XXSX||", StringComparison.Ordinal));
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, true)]
        [DataRow(DatabaseType.MSSQL, false)]
        [DataRow(DatabaseType.DWSQL, true)]
        [DataRow(DatabaseType.DWSQL, false)]
        [DataRow(DatabaseType.PostgreSQL, true)]
        [DataRow(DatabaseType.PostgreSQL, false)]
        public void GlobalOptOut_PreservesUndecoratedSerializedConnectionStrings(DatabaseType databaseType, bool withConfig)
        {
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, " TrUe ");
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, "dab_hosted");
            string?[] customerNames =
            {
                null, string.Empty, " ", GetApplicationName(databaseType, string.Empty),
                "  Customer ; \"quotes\" + | café,尾  ", "my_dab_app", "my_dab_oss_1.2.3",
                "dab_custom_1.2.3", "dab_oss_notes", "prefixdab_oss_1.2.3", "customer|dab_oss_not-a-version"
            };

            foreach (string? customerName in customerNames)
            {
                DbConnectionStringBuilder original = CreateConnectionStringBuilder(databaseType, customerName);
                string serialized = original.ConnectionString;

                string actual = Rewrite(databaseType, serialized, withConfig);

                Assert.AreEqual(serialized, actual, "No DAB block: preserve the entire input byte-for-byte.");
                Assert.AreEqual(GetApplicationName(databaseType, serialized), GetApplicationName(databaseType, actual));
                // Compare equivalent parse boundaries. A provider setter can retain an empty
                // key that its parser drops, even when the serialized text is unchanged.
                DbConnectionStringBuilder baseline = CreateConnectionStringBuilder(databaseType);
                baseline.ConnectionString = serialized;
                DbConnectionStringBuilder reparsed = CreateConnectionStringBuilder(databaseType);
                reparsed.ConnectionString = actual;
                Assert.AreEqual(baseline.ShouldSerialize("Application Name"), reparsed.ShouldSerialize("Application Name"));
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, " Server = localhost ; Initial Catalog = synthetic ; Integrated Security = true ; App = \" my_dab_app ; + \" ; ")]
        [DataRow(DatabaseType.PostgreSQL, " Host = localhost ; Database = synthetic ; Application Name = \" my_dab_app ; + \" ; ")]
        public void GlobalOptOut_DoesNotNormalizeUndecoratedRawConnectionString(DatabaseType databaseType, string connectionString)
        {
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "1");
            Assert.AreEqual(connectionString, Rewrite(databaseType, connectionString, withConfig: true));
            Assert.AreEqual(connectionString, Rewrite(databaseType, connectionString, withConfig: false));
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, true)]
        [DataRow(DatabaseType.MSSQL, false)]
        [DataRow(DatabaseType.DWSQL, true)]
        [DataRow(DatabaseType.DWSQL, false)]
        [DataRow(DatabaseType.PostgreSQL, true)]
        [DataRow(DatabaseType.PostgreSQL, false)]
        public void GlobalOptOut_RemovesPreviouslyInjectedSegment(DatabaseType databaseType, bool withConfig)
        {
            foreach (string? hostLabel in new string?[] { null, "dab_hosted", "dab_hosted_" })
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, hostLabel);
                foreach (string? legacyOptOut in new string?[] { null, "1" })
                {
                    Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, legacyOptOut);
                    foreach (string? customerName in new string?[] { null, "  Customer ; \"x\" + | café,尾,  " })
                    {
                        Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
                        string original = CreateConnectionStringBuilder(databaseType, customerName).ConnectionString;
                        string decorated = Rewrite(databaseType, original, withConfig);
                        Assert.AreNotEqual(original, decorated, "Exercise an actually injected provider connection string.");
                        Assert.AreEqual(decorated, Rewrite(databaseType, decorated, withConfig), "Enabled idempotency is unchanged.");

                        Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, " TRUE ");
                        string actual = Rewrite(databaseType, decorated, withConfig);

                        Assert.AreEqual(original, actual, "Remove the marker, version, payload and only the appended comma.");
                        Assert.AreEqual(GetApplicationName(databaseType, original), GetApplicationName(databaseType, actual));
                        Assert.AreEqual(actual, Rewrite(databaseType, actual, withConfig), "Repeated opted-out embedding is stable.");
                    }
                }
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, true)]
        [DataRow(DatabaseType.MSSQL, false)]
        [DataRow(DatabaseType.DWSQL, true)]
        [DataRow(DatabaseType.DWSQL, false)]
        [DataRow(DatabaseType.PostgreSQL, true)]
        [DataRow(DatabaseType.PostgreSQL, false)]
        public void GlobalOptOut_RemovesRecognizedSuffixAfterRawDabPrefix(DatabaseType databaseType, bool withConfig)
        {
            DataSource source = new(databaseType, string.Empty);
            string segment = ApplicationNameTelemetry.EncodeTelemetryString(CreateConfig(source), source);
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "1");

            foreach (string customerName in new[] { "my_dab_app", "my_dab_oss_1.2.3", "dab_oss_notes,", "hash==|my_dab_app" })
            {
                // A previous host may have composed this prefix after DAB embedded telemetry.
                // The enabled Contains(dab_) guard must not prevent opted-out removal.
                string decorated = CreateConnectionStringBuilder(databaseType, customerName + "," + segment).ConnectionString;
                string actual = Rewrite(databaseType, decorated, withConfig);

                Assert.AreEqual(customerName, GetApplicationName(databaseType, actual));
                Assert.AreEqual(CreateConnectionStringBuilder(databaseType, customerName).ConnectionString, actual);
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL)]
        [DataRow(DatabaseType.DWSQL)]
        [DataRow(DatabaseType.PostgreSQL)]
        public void GlobalOptOut_RemovesCurrentCustomHostedVersionedSegments(DatabaseType databaseType)
        {
            foreach (string label in new[] { "custom_host", "custom_host_", "my_dab_host", "host.with+punctuation", "host,dab_oss", "host|dab_hosted" })
            {
                Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, label);
                foreach (string? legacyOptOut in new string?[] { null, "1" })
                {
                    Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, legacyOptOut);
                    Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
                    string original = CreateConnectionStringBuilder(databaseType, "Customer").ConnectionString;
                    string decorated = Rewrite(databaseType, original, withConfig: true);

                    Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "1");
                    // The receiving loader may not yet have a runtime config.
                    Assert.AreEqual(original, Rewrite(databaseType, decorated, withConfig: false));
                }
            }
        }

        [DataTestMethod]
        [DataRow(null, null)]
        [DataRow("", "")]
        [DataRow("dab_oss_0.0.0", "")]
        [DataRow("dab_oss_123.456.789", "")]
        [DataRow("dab_hosted_1.2.3", "")]
        [DataRow("Customer,dab_hosted", "Customer")]
        [DataRow("Customer,dab_hosted_", "Customer")]
        [DataRow("my_dab_app,dab_oss_1.2.3" + PAYLOAD, "my_dab_app")]
        [DataRow("Customer,,dab_oss_1.2.3" + PAYLOAD, "Customer,")]
        [DataRow("hash==|dab_oss_1.2.3" + PAYLOAD, "hash==|")]
        [DataRow("hash==|my_dab_app,dab_hosted_1.2.3" + PAYLOAD, "hash==|my_dab_app")]
        [DataRow("Customer,dab_oss_1.2.3,dab_hosted_2.3.4" + PAYLOAD, "Customer")]
        [DataRow("Customer,dab_hosted_9.8.7+XXPXQ|10|MMMM00MMM00MMMMMMMMM9|MMM?MMMMMMMMM?9+", "Customer")]
        [DataRow("my_dab_app", "my_dab_app")]
        [DataRow("my_dab_oss_1.2.3", "my_dab_oss_1.2.3")]
        [DataRow("Customer,dab_custom_1.2.3" + PAYLOAD, "Customer,dab_custom_1.2.3" + PAYLOAD)]
        [DataRow("Customer,dab_oss_notes", "Customer,dab_oss_notes")]
        [DataRow("Customer,dab_oss_01.2.3", "Customer,dab_oss_01.2.3")]
        [DataRow("Customer,dab_oss_1.2", "Customer,dab_oss_1.2")]
        [DataRow("Customer,dab_oss_1.2.3.4", "Customer,dab_oss_1.2.3.4")]
        [DataRow("Customer,dab_oss_1.2.3-preview", "Customer,dab_oss_1.2.3-preview")]
        [DataRow("Customer,dab_oss_1.2.3+commit", "Customer,dab_oss_1.2.3+commit")]
        [DataRow("Customer,dab_oss_1.2.3+XXSX||MMM|MM", "Customer,dab_oss_1.2.3+XXSX||MMM|MM")]
        [DataRow("Customer,dab_oss_1.2.3+customer text+", "Customer,dab_oss_1.2.3+customer text+")]
        [DataRow("Customer,dab_oss_1.2.3+XXSX||lowercase|MM+", "Customer,dab_oss_1.2.3+XXSX||lowercase|MM+")]
        [DataRow("Customer,dab_oss_1.2.3+XXXX||MY|APP+", "Customer,dab_oss_1.2.3+XXXX||MY|APP+")]
        [DataRow("Customer,dab_oss_1.2.3+XXSX||MM|MM|extra+", "Customer,dab_oss_1.2.3+XXSX||MM|MM|extra+")]
        [DataRow("Customer,dab_oss_1.2.3" + PAYLOAD + ",suffix", "Customer,dab_oss_1.2.3" + PAYLOAD + ",suffix")]
        [DataRow("Customer,dab_oss_1.2.3 ", "Customer,dab_oss_1.2.3 ")]
        [DataRow("Customer,DAB_OSS_1.2.3", "Customer,DAB_OSS_1.2.3")]
        public void Removal_RecognizesCompleteTerminalSegmentsConservatively(string? input, string? expected)
        {
            Assert.AreEqual(expected, ApplicationNameTelemetry.RemoveApplicationNameSegments(input));
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL)]
        [DataRow(DatabaseType.PostgreSQL)]
        public void GlobalOptOut_RestoresProviderDefaultWhenNoCustomerPrefixRemains(DatabaseType databaseType)
        {
            foreach (string? originalName in new[] { string.Empty, GetApplicationName(databaseType, string.Empty) })
            {
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
                string original = CreateConnectionStringBuilder(databaseType, originalName).ConnectionString;
                string decorated = Rewrite(databaseType, original, withConfig: true);
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "1");

                string actual = Rewrite(databaseType, decorated, withConfig: true);
                Assert.AreEqual(GetApplicationName(databaseType, string.Empty), GetApplicationName(databaseType, actual));
                DbConnectionStringBuilder reparsed = CreateConnectionStringBuilder(databaseType);
                reparsed.ConnectionString = actual;
                Assert.IsFalse(reparsed.ShouldSerialize("Application Name"), "Do not serialize an empty DAB-owned AppName.");
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, true)]
        [DataRow(DatabaseType.MSSQL, false)]
        [DataRow(DatabaseType.DWSQL, true)]
        [DataRow(DatabaseType.DWSQL, false)]
        [DataRow(DatabaseType.PostgreSQL, true)]
        [DataRow(DatabaseType.PostgreSQL, false)]
        public void EnabledAndLegacyOnlyBehaviorIsUnchanged(DatabaseType databaseType, bool withConfig)
        {
            foreach (string? globalValue in new string?[] { null, "0", " false ", "yes" })
            {
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, globalValue);
                foreach (string? legacyValue in new string?[] { null, "1", " 1 ", "true" })
                {
                    Environment.SetEnvironmentVariable(ApplicationNameTelemetry.OPT_OUT_ENV_VAR, legacyValue);
                    foreach (string? label in new string?[] { null, "dab_hosted" })
                    {
                        Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, label);
                        string original = CreateConnectionStringBuilder(databaseType, "Customer").ConnectionString;
                        DataSource source = new(databaseType, original);
                        string expectedSegment = !withConfig
                            ? ProductInfo.GetDataApiBuilderUserAgent()
                            : legacyValue?.Trim() == "1"
                                ? ProductInfo.GetTelemetryApplicationNameBase()
                                : ApplicationNameTelemetry.EncodeTelemetryString(CreateConfig(source), source);

                        string actual = Rewrite(databaseType, original, withConfig);

                        Assert.AreEqual("Customer," + expectedSegment, GetApplicationName(databaseType, actual));
                        Assert.AreEqual(actual, Rewrite(databaseType, actual, withConfig));

                        string rawDabName = CreateConnectionStringBuilder(databaseType, "customer_dab_note").ConnectionString;
                        Assert.AreEqual(rawDabName, Rewrite(databaseType, rawDabName, withConfig),
                            "Keep the existing broad Contains(dab_) idempotency guard when globally enabled.");
                    }
                }
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL)]
        [DataRow(DatabaseType.DWSQL)]
        [DataRow(DatabaseType.PostgreSQL)]
        public void ConfigParsing_UsesVetoForNewAndPreviouslyDecoratedStrings(DatabaseType databaseType)
        {
            string original = CreateConnectionStringBuilder(databaseType, "Customer").ConnectionString;
            string decorated = Rewrite(databaseType, original, withConfig: true);
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "true");

            foreach (string input in new[] { original, decorated })
            {
                RuntimeConfig config = CreateConfig(new DataSource(databaseType, input));
                bool parsed = RuntimeConfigLoader.TryParseConfig(
                    json: config.ToJson(),
                    config: out RuntimeConfig result,
                    replacementSettings: new(doReplaceEnvVar: true));

                Assert.IsTrue(parsed);
                Assert.IsNotNull(result.DataSource);
                Assert.AreEqual(original, result.DataSource.ConnectionString);
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL)]
        [DataRow(DatabaseType.PostgreSQL)]
        public void GlobalOptOut_DoesNotGuessOwnershipOfOpaqueLegacyCustomLabel(DatabaseType databaseType)
        {
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, "opaque_host_label");
            string original = CreateConnectionStringBuilder(databaseType, "Customer").ConnectionString;
            string decorated = Rewrite(databaseType, original, withConfig: false);
            Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, "1");

            // No version/framing identifies this historical custom fallback as DAB-owned. Retaining
            // it is intentional; supplying the undecorated original is necessary in this case.
            Assert.AreEqual(decorated, Rewrite(databaseType, decorated, withConfig: false));
            Assert.AreEqual(original, Rewrite(databaseType, original, withConfig: false));
        }

        private static RuntimeConfig CreateConfig(DataSource source) =>
            new(Schema: "synthetic", DataSource: source, Entities: new(new Dictionary<string, Entity>()));

        private static DbConnectionStringBuilder CreateConnectionStringBuilder(DatabaseType databaseType, string? applicationName = null)
        {
            DbConnectionStringBuilder builder = databaseType == DatabaseType.PostgreSQL
                ? new NpgsqlConnectionStringBuilder { Host = "localhost", Database = "synthetic", Username = "synthetic" }
                : new SqlConnectionStringBuilder { DataSource = "localhost", InitialCatalog = "synthetic", IntegratedSecurity = true };
            if (applicationName is not null)
            {
                builder["Application Name"] = applicationName;
            }

            return builder;
        }

        private static string? GetApplicationName(DatabaseType databaseType, string connectionString) =>
            databaseType == DatabaseType.PostgreSQL
                ? new NpgsqlConnectionStringBuilder(connectionString).ApplicationName
                : new SqlConnectionStringBuilder(connectionString).ApplicationName;

        private static string Rewrite(DatabaseType databaseType, string connectionString, bool withConfig)
        {
            if (!withConfig)
            {
                return databaseType == DatabaseType.PostgreSQL
                    ? RuntimeConfigLoader.GetPgSqlConnectionStringWithApplicationName(connectionString)
                    : RuntimeConfigLoader.GetMsSqlConnectionStringWithApplicationName(connectionString);
            }

            DataSource source = new(databaseType, connectionString);
            return RuntimeConfigLoader.GetConnectionStringWithApplicationName(connectionString, CreateConfig(source), source);
        }
    }
}
