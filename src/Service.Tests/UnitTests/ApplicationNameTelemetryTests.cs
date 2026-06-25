// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers.GraphQLTestHelpers;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="ApplicationNameTelemetry"/> – the encoder/decoder that embeds DAB
    /// telemetry into the SQL Server Application Name.
    /// </summary>
    [TestClass]
    public class ApplicationNameTelemetryTests
    {
        private const string OPT_OUT_VAR = ApplicationNameTelemetry.OPT_OUT_ENV_VAR;
        private const string APP_NAME_VAR = ProductInfo.DAB_APP_NAME_ENV;

        /// <summary>Ensure a clean environment for the env-sensitive tests.</summary>
        [TestInitialize]
        public void ClearEnvironment()
        {
            Environment.SetEnvironmentVariable(OPT_OUT_VAR, null);
            Environment.SetEnvironmentVariable(APP_NAME_VAR, null);
        }

        [TestCleanup]
        public void ResetEnvironment()
        {
            Environment.SetEnvironmentVariable(OPT_OUT_VAR, null);
            Environment.SetEnvironmentVariable(APP_NAME_VAR, null);
        }

        /// <summary>
        /// The encoded string must start with the product user agent, be wrapped in '+', and contain
        /// exactly three '|'-separated sections of fixed widths (context=4, runtime=20, entity=14).
        /// </summary>
        [TestMethod]
        public void EncodeTelemetryString_HasExpectedShape()
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), Source(DatabaseType.MSSQL));

            Assert.IsTrue(telemetry.StartsWith(ProductInfo.DAB_USER_AGENT + "+", StringComparison.Ordinal), telemetry);
            Assert.IsTrue(telemetry.EndsWith("+", StringComparison.Ordinal), telemetry);

            (string context, string runtime, string entity) = Sections(telemetry);
            Assert.AreEqual(4, context.Length, "context width");
            Assert.AreEqual(20, runtime.Length, "runtime width");
            Assert.AreEqual(14, entity.Length, "entity width");
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, 'S')]
        [DataRow(DatabaseType.DWSQL, 'D')]
        [DataRow(DatabaseType.PostgreSQL, 'P')]
        [DataRow(DatabaseType.MySQL, 'M')]
        [DataRow(DatabaseType.CosmosDB_NoSQL, 'C')]
        public void EncodeTelemetryString_EncodesSource(DatabaseType dbType, char expectedSource)
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), Source(dbType));
            (string context, _, _) = Sections(telemetry);

            // Context = [Protocol][Object][Source][Role]; only Source is known at pool time.
            Assert.AreEqual("XX", context[..2], "Protocol/Object are placeholders");
            Assert.AreEqual(expectedSource, context[2], "Source");
            Assert.AreEqual('X', context[3], "Role is a placeholder");
        }

        [TestMethod]
        public void EncodeTelemetryString_NoLiveSource_EmitsAllPlaceholders()
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), liveDataSource: null);
            (string context, _, _) = Sections(telemetry);
            Assert.AreEqual("XXXX", context);
        }

        [TestMethod]
        public void EncodeTelemetryString_RuntimeFlags_MissingSectionsEncodeAsM()
        {
            // No Runtime section at all -> all runtime "enabled"-style flags are 'M'.
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(runtime: null), Source(DatabaseType.MSSQL));
            (_, string runtime, _) = Sections(telemetry);

            Assert.AreEqual('M', runtime[0], "rest.enabled missing");
            Assert.AreEqual('M', runtime[1], "graphql.enabled missing");
            Assert.AreEqual('M', runtime[2], "mcp.enabled missing");
            Assert.AreEqual('M', runtime[3], "host.mode missing");
            Assert.AreEqual('M', runtime[17], "auth.provider missing");
        }

        [TestMethod]
        public void EncodeTelemetryString_RuntimeFlags_ReflectConfiguredValues()
        {
            RuntimeOptions runtime = new(
                Rest: new RestRuntimeOptions(Enabled: false, RequestBodyStrict: false),
                GraphQL: new GraphQLRuntimeOptions(Enabled: true, MultipleMutationOptions: new MultipleMutationOptions(new MultipleCreateOptions(true))),
                Mcp: new McpRuntimeOptions(Enabled: false),
                Host: new HostOptions(Cors: null, Authentication: new AuthenticationOptions("StaticWebApps"), Mode: HostMode.Production),
                Telemetry: new TelemetryOptions(
                    ApplicationInsights: new ApplicationInsightsOptions(Enabled: true),
                    OpenTelemetry: new OpenTelemetryOptions(Enabled: false),
                    AzureLogAnalytics: new AzureLogAnalyticsOptions(enabled: true),
                    File: new FileSinkOptions(enabled: true)),
                Cache: new RuntimeCacheOptions(Enabled: true) { Level2 = new RuntimeCacheLevel2Options(Enabled: true) },
                Health: new RuntimeHealthCheckConfig(enabled: false),
                Embeddings: new EmbeddingsOptions(
                    Provider: EmbeddingProviderType.AzureOpenAI,
                    BaseUrl: "https://example.openai.azure.com",
                    ApiKey: "test-key",
                    Enabled: true,
                    Endpoint: new EmbeddingsEndpointOptions { Enabled = false }));

            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(runtime: runtime), Source(DatabaseType.MSSQL));
            (_, string r, _) = Sections(telemetry);

            Assert.AreEqual('0', r[0], "rest.enabled=false");
            Assert.AreEqual('1', r[1], "graphql.enabled=true");
            Assert.AreEqual('0', r[2], "mcp.enabled=false");
            Assert.AreEqual('1', r[3], "host.mode=Production");
            Assert.AreEqual('0', r[6], "health.enabled=false");
            Assert.AreEqual('1', r[7], "cache.enabled=true");
            Assert.AreEqual('1', r[8], "cache.l2=true");
            Assert.AreEqual('0', r[11], "rest.request-body-strict=false");
            Assert.AreEqual('1', r[12], "graphql.multiple-mutations.create.enabled=true");
            Assert.AreEqual('0', r[13], "open-telemetry.enabled=false");
            Assert.AreEqual('1', r[14], "application-insights.enabled=true");
            Assert.AreEqual('1', r[15], "azure-log-analytics.enabled=true");
            Assert.AreEqual('1', r[16], "file-sink.enabled=true");
            Assert.AreEqual('W', r[17], "auth.provider=StaticWebApps");
            Assert.AreEqual('1', r[18], "embedding.enabled=true");
            Assert.AreEqual('0', r[19], "embedding.endpoint.enabled=false");
        }

        [TestMethod]
        public void EncodeTelemetryString_HostMode_EncodesDevAndProd()
        {
            RuntimeOptions dev = new(Rest: null, GraphQL: null, Mcp: null, Host: new HostOptions(null, null, HostMode.Development));
            RuntimeOptions prod = new(Rest: null, GraphQL: null, Mcp: null, Host: new HostOptions(null, null, HostMode.Production));

            Assert.AreEqual('0', Sections(ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(runtime: dev), Source(DatabaseType.MSSQL))).runtime[3]);
            Assert.AreEqual('1', Sections(ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(runtime: prod), Source(DatabaseType.MSSQL))).runtime[3]);
        }

        [TestMethod]
        public void EncodeTelemetryString_Obo_EncodesPresence()
        {
            DataSource oboSource = new(DatabaseType.MSSQL, "Server=localhost;Database=test;")
            {
                UserDelegatedAuth = new UserDelegatedAuthOptions(Enabled: true)
            };
            RuntimeConfig config = new(Schema: "t", DataSource: oboSource, Entities: new(new Dictionary<string, Entity>()));

            (_, string runtime, _) = Sections(ApplicationNameTelemetry.EncodeTelemetryString(config, oboSource));
            Assert.AreEqual('1', runtime[9], "data-source.obo=true");
        }

        [DataTestMethod]
        [DataRow("Unauthenticated", 'U')]
        [DataRow("Simulator", 'S')]
        [DataRow("StaticWebApps", 'W')]
        [DataRow("AppService", 'A')]
        [DataRow("AzureAD", 'E')]
        [DataRow("EntraID", 'E')]
        [DataRow("SomeCustomJwtProvider", 'C')]
        public void EncodeTelemetryString_EncodesAuthProvider(string provider, char expected)
        {
            RuntimeOptions runtime = new(
                Rest: null, GraphQL: null, Mcp: null,
                Host: new HostOptions(Cors: null, Authentication: new AuthenticationOptions(provider)));

            (_, string r, _) = Sections(ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(runtime: runtime), Source(DatabaseType.MSSQL)));
            Assert.AreEqual(expected, r[17], $"auth.provider letter for '{provider}'");
        }

        [TestMethod]
        public void EncodeTelemetryString_AuthProvider_MissingWhenNoAuthentication()
        {
            RuntimeOptions runtime = new(Rest: null, GraphQL: null, Mcp: null, Host: new HostOptions(Cors: null, Authentication: null));
            (_, string r, _) = Sections(ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(runtime: runtime), Source(DatabaseType.MSSQL)));
            Assert.AreEqual('M', r[17], "auth.provider is M when no authentication is configured");
        }

        [TestMethod]
        public void EncodeTelemetryString_EntityFlags_MissingWhenNoEntities()
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), Source(DatabaseType.MSSQL));
            (_, _, string entity) = Sections(telemetry);

            // Tri-state flags are 'M' (no entities); the two unmodeled concepts are '?'.
            Assert.AreEqual('M', entity[0], "any table");
            Assert.AreEqual('?', entity[3], "MCP persisted documents (not modeled)");
            Assert.AreEqual('?', entity[13], "parameter embed (not modeled)");
        }

        [TestMethod]
        public void EncodeTelemetryString_EntityFlags_ReflectEntities()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Tbl"] = GenerateEmptyEntity(EntitySourceType.Table),
                ["Vw"] = GenerateEmptyEntity(EntitySourceType.View),
                ["Described"] = GenerateEmptyEntity() with { Description = "a description" },
                ["Cached"] = GenerateEmptyEntity() with { Cache = new EntityCacheOptions(Enabled: true) },
                ["CustomRole"] = GenerateEmptyEntity() with { Permissions = new[] { new EntityPermission("manager", Array.Empty<EntityAction>()) } },
                ["Policy"] = GenerateEmptyEntity() with
                {
                    Permissions = new[]
                    {
                        new EntityPermission("anonymous", new[]
                        {
                            new EntityAction(EntityActionOperation.Read, null, new EntityActionPolicy(Database: "@item.id eq 1")),
                        }),
                    },
                },
                ["Related"] = GenerateEmptyEntity() with
                {
                    Relationships = new Dictionary<string, EntityRelationship>
                    {
                        ["rel"] = new(Cardinality.One, "Tbl", Array.Empty<string>(), Array.Empty<string>(), null, Array.Empty<string>(), Array.Empty<string>()),
                    },
                },
            };

            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(entities: entities), Source(DatabaseType.MSSQL));
            (_, _, string e) = Sections(telemetry);

            Assert.AreEqual('1', e[0], "any table");
            Assert.AreEqual('1', e[1], "any view");
            Assert.AreEqual('0', e[2], "any stored procedure");
            Assert.AreEqual('1', e[5], "any rest.enabled");
            Assert.AreEqual('1', e[6], "any graphql.enabled");
            Assert.AreEqual('1', e[9], "any custom roles");
            Assert.AreEqual('1', e[10], "any policies");
            Assert.AreEqual('1', e[11], "any descriptions");
            Assert.AreEqual('1', e[12], "any relationships");
            Assert.AreEqual('1', e[4], "any cache");
        }

        [TestMethod]
        public void EncodeTelemetryString_EntityFlags_ReflectMcpToolUsage()
        {
            // One entity opts into MCP DML tools only, another opts into the MCP custom tool only,
            // so the "any entity uses ..." flags for both must be set.
            Dictionary<string, Entity> entities = new()
            {
                ["Dml"] = GenerateEmptyEntity() with { Mcp = new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: true) },
                ["Custom"] = GenerateEmptyEntity() with { Mcp = new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false) },
            };

            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(entities: entities), Source(DatabaseType.MSSQL));
            (_, _, string e) = Sections(telemetry);

            Assert.AreEqual('1', e[7], "any mcp dml-tools");
            Assert.AreEqual('1', e[8], "any mcp custom-tool");
        }

        // ----- Opt-out + DAB_APP_NAME_ENV -----------------------------------------------------

        [TestMethod]
        public void BuildApplicationNameSegment_OptedIn_ContainsPayload()
        {
            string segment = ApplicationNameTelemetry.BuildApplicationNameSegment(BuildConfig(), Source(DatabaseType.MSSQL));
            Assert.IsTrue(segment.StartsWith(ProductInfo.DAB_USER_AGENT + "+", StringComparison.Ordinal), segment);
            Assert.IsTrue(segment.EndsWith("+", StringComparison.Ordinal), segment);
        }

        [TestMethod]
        public void BuildApplicationNameSegment_OptedOut_OmitsPayload()
        {
            Environment.SetEnvironmentVariable(OPT_OUT_VAR, "1");
            string segment = ApplicationNameTelemetry.BuildApplicationNameSegment(BuildConfig(), Source(DatabaseType.MSSQL));
            Assert.AreEqual(ProductInfo.DAB_USER_AGENT, segment);
        }

        [DataTestMethod]
        [DataRow("0")]
        [DataRow("")]
        [DataRow("true")]
        [DataRow("yes")]
        public void BuildApplicationNameSegment_InvalidOptOutValue_KeepsTelemetry(string optOutValue)
        {
            Environment.SetEnvironmentVariable(OPT_OUT_VAR, optOutValue);
            string segment = ApplicationNameTelemetry.BuildApplicationNameSegment(BuildConfig(), Source(DatabaseType.MSSQL));
            Assert.IsTrue(segment.EndsWith("+", StringComparison.Ordinal), $"telemetry should remain on for '{optOutValue}': {segment}");
        }

        [TestMethod]
        public void BuildApplicationNameSegment_AppNameEnv_RidesAsPrefixWithoutSuppressingTelemetry()
        {
            Environment.SetEnvironmentVariable(APP_NAME_VAR, "dab_hosted");
            string segment = ApplicationNameTelemetry.BuildApplicationNameSegment(BuildConfig(), Source(DatabaseType.MSSQL));

            Assert.IsTrue(segment.StartsWith("dab_hosted," + ProductInfo.DAB_USER_AGENT + "+", StringComparison.Ordinal), segment);
            Assert.IsTrue(segment.EndsWith("+", StringComparison.Ordinal), segment);
        }

        [TestMethod]
        public void BuildApplicationNameSegment_AppNameEnvWithOptOut_PrefixWithoutPayload()
        {
            Environment.SetEnvironmentVariable(APP_NAME_VAR, "dab_hosted");
            Environment.SetEnvironmentVariable(OPT_OUT_VAR, "1");
            string segment = ApplicationNameTelemetry.BuildApplicationNameSegment(BuildConfig(), Source(DatabaseType.MSSQL));
            Assert.AreEqual("dab_hosted," + ProductInfo.DAB_USER_AGENT, segment);
        }

        // ----- Decode -------------------------------------------------------------------------

        [TestMethod]
        public void Decode_RoundTrips_ProducesReadableLines()
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), Source(DatabaseType.MSSQL));
            IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode(telemetry);

            Assert.IsTrue(lines.Any(l => l.StartsWith("Version: " + ProductInfo.DAB_USER_AGENT, StringComparison.Ordinal)), "version line");
            Assert.IsTrue(lines.Any(l => l.Contains("Source: S (SQL)")), "decoded source");
            Assert.IsTrue(lines.Any(l => l.Contains("runtime.rest.enabled")), "decoded runtime setting");
            Assert.IsTrue(lines.Any(l => l.Contains("entities.any.table")), "decoded entity setting");
        }

        [TestMethod]
        public void Decode_IgnoresUserPrefixAndOboHash()
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), Source(DatabaseType.MSSQL));
            string fullAppName = $"abc123hash==|MyCustomApp,{telemetry}";

            IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode(fullAppName);
            Assert.IsTrue(lines.Any(l => l.StartsWith("Version: " + ProductInfo.DAB_USER_AGENT, StringComparison.Ordinal)), string.Join('\n', lines));
        }

        [TestMethod]
        public void Decode_TruncatedPayload_DoesNotThrowAndDecodesPartial()
        {
            string telemetry = ApplicationNameTelemetry.EncodeTelemetryString(BuildConfig(), Source(DatabaseType.MSSQL));
            // Simulate SQL Server truncation by cutting the string mid-payload.
            string truncated = telemetry[..(telemetry.Length - 10)];

            IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode(truncated);
            Assert.IsTrue(lines.Count > 1, "should decode the portion that survived truncation");
            Assert.IsTrue(lines.Any(l => l.StartsWith("Version:", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void Decode_NoMarker_ReturnsFriendlyMessage()
        {
            IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode("SomeUnrelatedApplicationName");
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains(lines[0], "No DAB telemetry found");
        }

        [TestMethod]
        public void Decode_VersionOnly_ReportsNoPayload()
        {
            IReadOnlyList<string> lines = ApplicationNameTelemetry.Decode("dab_oss_1.2.3");
            Assert.IsTrue(lines.Any(l => l.Contains("Version: dab_oss_1.2.3")));
            Assert.IsTrue(lines.Any(l => l.Contains("none")), "should report no payload");
        }

        [TestMethod]
        public void Decode_NullOrEmpty_ReturnsFriendlyMessage()
        {
            Assert.AreEqual(1, ApplicationNameTelemetry.Decode(null).Count);
            Assert.AreEqual(1, ApplicationNameTelemetry.Decode("   ").Count);
        }

        /// <summary>
        /// Per-pool consistency: the OBO flag must reflect the LIVE data source being encoded, not the
        /// config's default data source. This matters in multi-database setups where data sources differ.
        /// </summary>
        [TestMethod]
        public void EncodeTelemetryString_Obo_ReflectsLiveDataSourceNotDefault()
        {
            // Default data source has OBO OFF.
            DataSource defaultSource = new(DatabaseType.MSSQL, "Server=localhost;Database=default;");
            // A different (live) data source has OBO ON.
            DataSource liveOboSource = new(DatabaseType.PostgreSQL, "Host=localhost;Database=live;Username=u;")
            {
                UserDelegatedAuth = new UserDelegatedAuthOptions(Enabled: true)
            };
            RuntimeConfig config = new(Schema: "t", DataSource: defaultSource, Entities: new(new Dictionary<string, Entity>()));

            // Encoding for the live OBO-enabled pool reports obo=1 even though the default is off.
            (_, string liveRuntime, _) = Sections(ApplicationNameTelemetry.EncodeTelemetryString(config, liveOboSource));
            Assert.AreEqual('1', liveRuntime[9], "obo must reflect the live data source");

            // Encoding with no live source falls back to the default data source (obo off).
            (_, string defaultRuntime, _) = Sections(ApplicationNameTelemetry.EncodeTelemetryString(config, liveDataSource: null));
            Assert.AreEqual('0', defaultRuntime[9], "obo falls back to the default data source when no live source");
        }

        // ----- Helpers ------------------------------------------------------------------------

        /// <summary>Builds a live data source of the given type for the per-pool encoder inputs.</summary>
        private static DataSource Source(DatabaseType type) =>
            new(type, "Server=localhost;Database=test;", Options: null);

        private static RuntimeConfig BuildConfig(
            RuntimeOptions runtime = null,
            Dictionary<string, Entity> entities = null)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType.MSSQL, "Server=localhost;Database=test;", Options: null),
                Entities: new RuntimeEntities(entities ?? new Dictionary<string, Entity>()),
                Runtime: runtime);
        }

        private static (string context, string runtime, string entity) Sections(string telemetry)
        {
            int firstPlus = telemetry.IndexOf('+');
            string payload = telemetry[(firstPlus + 1)..].TrimEnd('+');
            string[] parts = payload.Split('|');
            return (parts[0], parts[1], parts[2]);
        }
    }
}
