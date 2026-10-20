// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Config.Telemetry.TelemetryConfigurationPresence;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Pure capture/projection tests plus isolated loader gate tests. All configurations are
    /// synthetic; no files, hosts, database connections, exporters, or telemetry identities are used.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryPresenceTests
    {
        private const string SENTINEL = "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274";

        [DataTestMethod]
        [DataRow(null, null, false)]
        [DataRow("", null, false)]
        [DataRow("false", null, false)]
        [DataRow("0", null, false)]
        [DataRow("yes", null, false)]
        [DataRow("01", null, false)]
        [DataRow("1", null, true)]
        [DataRow(" TrUe ", null, true)]
        [DataRow("true", "false", true)]
        [DataRow("1", "yes", true)]
        [DataRow("true", "1", false)]
        [DataRow("1", " TRUE ", false)]
        public void CapturePolicyIsPureDefaultOffAndVetoWins(string? testMode, string? optOut, bool expected)
        {
            Assert.AreEqual(expected, IsCaptureEnabled(testMode, optOut));
        }

        [TestMethod]
        public void AmbientTestModeAloneDoesNotAuthorizeConfigurationCapture()
        {
            string? previousMode = Environment.GetEnvironmentVariable(TEST_MODE_ENV_VAR);
            string? previousOptOut = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR);
            try
            {
                Environment.SetEnvironmentVariable(TEST_MODE_ENV_VAR, "1");
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, null);
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig("{}", out RuntimeConfig? config));
                Assert.IsNull(config.TelemetryPresence, "Parsing alone has no enabled host session or validated destination.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(TEST_MODE_ENV_VAR, previousMode);
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, previousOptOut);
            }
        }

        [TestMethod]
        public void DisabledCaptureDoesNotInspectJsonOrConfig()
        {
            Assert.IsNull(TryCapture("invalid JSON", null!));
            Assert.IsNull(TryCapture("invalid JSON", null!, enabled: false));
            Assert.IsNotNull(TryCapture("{}", Parse("{}"), enabled: true));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("{")]
        [DataRow("{\"runtime\":{},}")]
        [DataRow("null")]
        [DataRow("[]")]
        [DataRow("true")]
        public void InvalidOrNonObjectInputProducesNoProvenance(string json)
        {
            Assert.IsNull(TryCapture(json, Parse("{}"), enabled: true));
        }

        [TestMethod]
        public void OmissionAndExplicitNullHaveDifferentValueFreeTokens()
        {
            TelemetryConfigurationPresence missing = Capture("{}");
            Assert.AreEqual(SETTING_COUNT, missing.Settings.Count);
            Assert.IsTrue(missing.Settings.Values.All(value => value == Presence.Missing));

            TelemetryConfigurationPresence explicitNull = Capture("""
                {
                  "runtime": null, "autoentities": null, "azure-key-vault": null,
                  "data-source-files": null, "data-source": null
                }
                """);
            Assert.IsTrue(explicitNull.Settings.Values.All(value => value == Presence.ExplicitNull));
            CollectionAssert.AreEquivalent(missing.Settings.Keys.ToArray(), explicitNull.Settings.Keys.ToArray());
            Assert.IsTrue(missing.Entities.Values.All(value => value.Total == 0));
            Assert.IsTrue(explicitNull.Entities.Values.All(value => value.Total == 0));
        }

        [DataTestMethod]
        [DataRow("{}")]
        [DataRow("{\"runtime\":null}")]
        [DataRow("{\"runtime\":{\"rest\":true,\"graphql\":false,\"mcp\":true}}")]
        [DataRow("{\"runtime\":{\"rest\":{},\"graphql\":{},\"mcp\":{},\"health\":{},\"cache\":{},\"host\":{},\"pagination\":{}}}")]
        [DataRow("{\"runtime\":{\"rest\":null,\"graphql\":null,\"mcp\":null,\"health\":null,\"cache\":null,\"host\":null,\"pagination\":null}}")]
        [DataRow("{\"runtime\":{\"REST\":false},\"runtime.rest.enabled\":true}")]
        [DataRow("{ /* allowed by the loader */ \"runtime\":{\"rest\":{\"request-body-strict\":false}}}")]
        [DataRow("{\"data-source\":{\"database-type\":\"mssql\"}}")]
        [DataRow("{\"data-source\":{\"database-type\":\"mssql\",\"options\":null,\"user-delegated-auth\":null}}")]
        [DataRow("{\"data-source\":{\"database-type\":\"mssql\",\"options\":{\"set-session-context\":\"false\"},\"user-delegated-auth\":{\"enabled\":true}}}")]
        [DataRow("{\"runtime\":{\"graphql\":{\"multiple-mutations\":{\"create\":{}}},\"cache\":{\"enabled\":null,\"level-2\":{},\"ttl-seconds\":null}}}")]
        [DataRow("{\"runtime\":{\"mcp\":{\"dml-tools\":{\"AGGREGATE-RECORDS\":{\"QUERY-TIMEOUT\":60}}}}}")]
        public void RetainedPresenceMatchesTransientOriginalForRuntimeAndSources(string json)
        {
            AssertMatchesOriginal(json);
        }

        [TestMethod]
        public void AllFixedRuntimePathsFeedTheSameSnapshotAsOriginalInput()
        {
            AssertMatchesOriginal("""
                {
                  "data-source": {
                    "database-type": "mssql", "options": { "set-session-context": false },
                    "user-delegated-auth": { "enabled": false }
                  },
                  "azure-key-vault": { "endpoint": "https://synthetic-vault.vault.azure.net/" }, "autoentities": {}, "data-source-files": [],
                  "runtime": {
                    "rest": { "enabled": true, "request-body-strict": false },
                    "graphql": { "enabled": true, "multiple-mutations": { "create": { "enabled": true } } },
                    "mcp": { "enabled": true, "dml-tools": { "aggregate-records": { "query-timeout": 60 } } },
                    "health": { "enabled": false },
                    "cache": { "enabled": true, "level-2": { "enabled": true }, "ttl-seconds": 40 },
                    "host": { "mode": "development", "authentication": { "provider": "Simulator" }, "max-response-size-mb": 16 },
                    "pagination": { "default-page-size": 10, "max-page-size": 100 },
                    "embeddings": {
                      "provider": "openai", "base-url": "synthetic", "api-key": "synthetic",
                      "ENABLED": false, "ENDPOINT": { "ENABLED": true }
                    },
                    "telemetry": {
                      "open-telemetry": { "enabled": false }, "application-insights": { "enabled": true },
                      "azure-log-analytics": { "enabled": false }, "file": { "enabled": true, "path": "synthetic" }
                    }
                  }
                }
                """);
        }

        [DataTestMethod]
        [DataRow("true", Presence.Present, "enabled")]
        [DataRow("false", Presence.Present, "disabled")]
        [DataRow("{}", Presence.Missing, "missing")]
        [DataRow("null", Presence.ExplicitNull, "unknown")]
        [DataRow("\"CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274\"", Presence.Present, "enabled")]
        [DataRow("{\"enabled\":false}", Presence.Present, "disabled")]
        public void EntityApiShorthandAndNullAreNormalizedWithoutRetainingTheirValues(string option, Presence presence, string configured)
        {
            string json = $$"""
                { "entities": { "Synthetic": { "source": "synthetic", "rest": {{option}}, "graphql": {{option}} } } }
                """;
            RuntimeConfig config = WithPresence(json);
            Assert.AreEqual(default(EntityPresence).Include(presence), config.TelemetryPresence!.Entities[EntityFeature.Rest]);
            Assert.AreEqual(default(EntityPresence).Include(presence), config.TelemetryPresence.Entities[EntityFeature.GraphQL]);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            Assert.AreEqual(configured, snapshot["entities.any.rest.configured"]);
            Assert.AreEqual(configured, snapshot["entities.any.graphql.configured"]);
            AssertMatchesOriginal(json);
        }

        [DataTestMethod]
        [DataRow("false", "disabled")]
        [DataRow("true", "enabled")]
        public void MixedDefaultOnEntitiesDoNotTurnExplicitFalseIntoExplicitTrue(string enabled, string expected)
        {
            string json = $$"""
                {
                  "entities": {
                    "Explicit": {
                      "source": { "object": "synthetic", "type": "stored-procedure" },
                      "rest": {{enabled}}, "graphql": {{enabled}}, "cache": { "enabled": {{enabled}} },
                      "mcp": { "dml-tools": {{enabled}}, "custom-tool": {{enabled}} }
                    },
                    "Defaulted": { "source": { "object": "synthetic", "type": "stored-procedure" }, "rest": {}, "graphql": {}, "cache": {}, "mcp": {} }
                  }
                }
                """;
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(WithPresence(json));
            foreach (string key in new[] { "cache", "rest", "graphql", "mcp_dml", "mcp_custom_tool" })
            {
                Assert.AreEqual(expected, snapshot["entities.any." + key + ".configured"], key);
            }

            AssertMatchesOriginal(json);
        }

        [DataTestMethod]
        [DataRow("false", "unknown")]
        [DataRow("true", "enabled")]
        public void NullUncertaintyIsNotErasedByNegativesButExplicitPositiveWins(string enabled, string expected)
        {
            string json = $$"""
                {
                  "entities": {
                    "Explicit": {
                      "source": { "object": "synthetic", "type": "stored-procedure" },
                      "rest": {{enabled}}, "graphql": {{enabled}}, "cache": { "enabled": {{enabled}} },
                      "mcp": { "dml-tools": {{enabled}}, "custom-tool": {{enabled}} }
                    },
                    "Nulls": {
                      "source": { "object": "synthetic", "type": "stored-procedure" },
                      "rest": null, "graphql": null, "cache": { "enabled": null }, "mcp": null
                    }
                  }
                }
                """;
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(WithPresence(json));
            foreach (string key in new[] { "cache", "rest", "graphql", "mcp_dml", "mcp_custom_tool" })
            {
                Assert.AreEqual(expected, snapshot["entities.any." + key + ".configured"], key);
            }

            AssertMatchesOriginal(json);
        }

        [TestMethod]
        public void McpShorthandDoesNotConfigureCustomToolAndTablesAreNotApplicable()
        {
            const string json = """
                {
                  "entities": {
                    "Procedure": { "source": { "object": "synthetic", "type": "stored-procedure" }, "mcp": true },
                    "Table": { "source": "synthetic", "mcp": { "custom-tool": true } }
                  }
                }
                """;
            RuntimeConfig config = WithPresence(json);
            Assert.AreEqual(new EntityPresence(1, 0, 0, 0), config.TelemetryPresence!.Entities[EntityFeature.McpCustomTool]);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            Assert.AreEqual("missing", snapshot["entities.any.mcp_custom_tool.configured"]);
            Assert.AreEqual("enabled", snapshot["entities.any.mcp_dml.configured"]);
            AssertMatchesOriginal(json);
        }

        [TestMethod]
        public void DuplicateEntityPropertiesUseOnlyTheAcceptedLastDefinition()
        {
            const string json = """
                { "entities": {
                  "Synthetic": { "source": "first", "rest": true },
                  "Synthetic": { "source": "last", "rest": false }
                } }
                """;
            RuntimeConfig config = WithPresence(json);
            Assert.AreEqual(new EntityPresence(0, 0, 1, 0), config.TelemetryPresence!.Entities[EntityFeature.Rest]);
            Assert.AreEqual("disabled", EngineTelemetrySnapshotFactory.Create(config)["entities.any.rest.configured"]);
            AssertMatchesOriginal(json);
        }

        [TestMethod]
        public void RetainedShapeContainsNoValuesNamesOrConfigReferences()
        {
            const string json = """
                {
                  "$schema": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274",
                  "data-source": { "database-type": "mssql", "connection-string": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274" },
                  "runtime": {
                    "rest": { "enabled": true, "path": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274" },
                    "cache": { "enabled": true, "ttl-seconds": 40 },
                    "host": { "authentication": { "provider": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274" } }
                  },
                  "entities": {
                    "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274": {
                      "source": { "object": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274", "type": "stored-procedure" },
                      "rest": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274", "graphql": true,
                      "cache": { "enabled": true }, "mcp": { "custom-tool": true },
                      "description": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274",
                      "permissions": [ { "role": "CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274", "actions": ["execute"] } ]
                    }
                  }
                }
                """;
            TelemetryConfigurationPresence first = Capture(json);
            TelemetryConfigurationPresence differentValues = Capture(json.Replace(SENTINEL, "DIFFERENT_CUSTOMER_VALUE", StringComparison.Ordinal)
                .Replace(": true", ": false", StringComparison.Ordinal).Replace(": 40", ": 99", StringComparison.Ordinal));
            CollectionAssert.AreEquivalent(first.Settings.ToArray(), differentValues.Settings.ToArray());
            CollectionAssert.AreEquivalent(first.Entities.ToArray(), differentValues.Entities.ToArray());
            Assert.IsFalse(JsonSerializer.Serialize(first).Contains(SENTINEL, StringComparison.Ordinal));
            Assert.AreEqual(StringComparer.Ordinal, first.Settings.KeyComparer);

            FieldInfo[] fields = typeof(TelemetryConfigurationPresence).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            CollectionAssert.AreEquivalent(new[]
            {
                typeof(ImmutableDictionary<string, Presence>), typeof(ImmutableDictionary<EntityFeature, EntityPresence>)
            }, fields.Select(field => field.FieldType).ToArray());
            Assert.IsTrue(typeof(EntityPresence).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(field => field.FieldType == typeof(long)));
        }

        [TestMethod]
        public void PureCaptureDoesNotLoadSourceFilesOrRetainTheirPaths()
        {
            RuntimeConfig config = Parse("{}") with { DataSourceFiles = new([SENTINEL]) };
            TelemetryConfigurationPresence? presence = TryCapture("""
                                {
                                    "data-source-files": ["CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274"],
                                    "azure-key-vault": { "endpoint": "@env('CUSTOMER_INPUT_MUST_NOT_SURVIVE_59b274')" }
                                }
                                """, config, enabled: true);
            Assert.IsNotNull(presence);
            Assert.AreEqual(Presence.Present, presence.Settings["data-source-files"]);
            Assert.AreEqual(Presence.Present, presence.Settings["azure-key-vault"]);
            Assert.IsFalse(JsonSerializer.Serialize(presence).Contains(SENTINEL, StringComparison.Ordinal));
            Assert.AreEqual(0, config.ChildConfigs.Count);
        }

        [TestMethod]
        public void UnknownKeysAndLargeEntitySetsCannotGrowTheMetadataSchema()
        {
            const int entityCount = 1000;
            Dictionary<string, object> entities = Enumerable.Range(0, entityCount).ToDictionary(
                index => SENTINEL + index,
                _ => (object)new { source = SENTINEL, rest = false, graphql = true, mcp = false });
            Dictionary<string, object> root = new() { ["entities"] = entities };
            for (int index = 0; index < entityCount; index++)
            {
                root.Add(SENTINEL + index, new string('x', 100));
            }

            TelemetryConfigurationPresence presence = Capture(JsonSerializer.Serialize(root));
            TelemetryConfigurationPresence empty = Capture("{}");
            Assert.AreEqual(SETTING_COUNT, presence.Settings.Count);
            Assert.AreEqual(ENTITY_FEATURE_COUNT, presence.Entities.Count);
            CollectionAssert.AreEquivalent(empty.Settings.Keys.ToArray(), presence.Settings.Keys.ToArray());
            Assert.AreEqual((long)entityCount, presence.Entities[EntityFeature.Rest].Total);
            Assert.AreEqual(0L, presence.Entities[EntityFeature.McpCustomTool].Total);
            string serialized = JsonSerializer.Serialize(presence);
            Assert.IsFalse(serialized.Contains(SENTINEL, StringComparison.Ordinal));
            Assert.IsTrue(serialized.Length < 8192);
        }

        [TestMethod]
        public void CloneKeepsImmutablePresenceButSerializationCannotReadOrWriteIt()
        {
            RuntimeConfig config = WithPresence("{}");
            RuntimeConfig clone = config with { Schema = "synthetic" };
            Assert.AreSame(config.TelemetryPresence, clone.TelemetryPresence);
            using JsonDocument serialized = JsonDocument.Parse(clone.ToJson());
            Assert.IsFalse(serialized.RootElement.TryGetProperty("telemetry-presence", out _));
            Assert.IsFalse(serialized.RootElement.TryGetProperty(nameof(RuntimeConfig.TelemetryPresence), out _));
            Assert.IsNull(Parse("""{ "telemetry-presence": { "Settings": { "runtime.rest.enabled": "Present" } } }""").TelemetryPresence);
        }

        [TestMethod]
        public void ExplicitTransientInputTakesPrecedenceOverRetainedPresence()
        {
            RuntimeConfig config = WithPresence("""{ "runtime": { "rest": false } }""");
            using JsonDocument original = JsonDocument.Parse("{}");
            Assert.AreEqual("missing", EngineTelemetrySnapshotFactory.Create(config, original.RootElement)["runtime.rest.configured"]);
            Assert.AreEqual("disabled", EngineTelemetrySnapshotFactory.Create(config)["runtime.rest.configured"]);
            Assert.AreEqual("unknown", EngineTelemetrySnapshotFactory.Create(config with { TelemetryPresence = null })["runtime.rest.configured"]);
        }

        [TestMethod]
        public void NestedChildProvenanceSurvivesMergingWithoutDuplicatingCountsOrSources()
        {
            RuntimeConfig first = WithPresence("""
                { "data-source": { "database-type": "mssql" }, "entities": { "First": { "source": "synthetic", "rest": false, "graphql": false } } }
                """);
            RuntimeConfig second = WithPresence("""
                { "data-source": { "database-type": "mssql" }, "entities": { "Second": { "source": "synthetic" } } }
                """);
            RuntimeConfig child = Merge(WithPresence("{}"), first, second);
            RuntimeConfig root = Merge(WithPresence("{}"), child);
            root.ChildConfigs.Add(("repeated-synthetic-reference", child));
            child.ChildConfigs.Add(("synthetic-cycle", root));

            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(root);
            Assert.AreEqual("disabled", snapshot["entities.any.rest.configured"]);
            Assert.AreEqual("disabled", snapshot["entities.any.graphql.configured"]);
            Assert.AreEqual("missing", snapshot["entities.any.cache.configured"]);
            Assert.AreEqual("missing", snapshot["data_sources.obo.configured"]);
            Assert.AreEqual("missing", snapshot["data_sources.session_context.configured"]);
            Assert.AreEqual("missing", snapshot["integrations.key_vault.configured"]);
            Assert.AreEqual("2-10", snapshot["scale.data_source_count"]);
            Assert.AreEqual("2-10", snapshot["scale.entity_count"]);
            Assert.AreEqual(0L, root.TelemetryPresence!.Entities[EntityFeature.Rest].Total);
            Assert.AreEqual(0L, child.TelemetryPresence!.Entities[EntityFeature.Rest].Total);
            Assert.AreSame(first.TelemetryPresence, root.ChildConfigs[0].Config.ChildConfigs[0].Config.TelemetryPresence);
        }

        [TestMethod]
        public void ChildExplicitNullIsNotChangedIntoOmissionByAnEmptyRoot()
        {
            RuntimeConfig child = WithPresence("""
                {
                  "data-source": { "database-type": "mssql", "user-delegated-auth": null, "options": null },
                  "azure-key-vault": null,
                  "entities": { "Synthetic": { "source": "synthetic", "rest": null } }
                }
                """);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(Merge(WithPresence("{}"), child));
            Assert.AreEqual("unknown", snapshot["entities.any.rest.configured"]);
            Assert.AreEqual("unknown", snapshot["data_sources.obo.configured"]);
            Assert.AreEqual("unknown", snapshot["data_sources.session_context.configured"]);
            Assert.AreEqual("unknown", snapshot["integrations.key_vault.configured"]);
            Assert.AreEqual("1", snapshot["scale.data_source_count"]);
        }

        [TestMethod]
        public void UncapturedChildAndAddedEntitiesRemainUnknownRatherThanMissing()
        {
            RuntimeConfig captured = WithPresence("""{ "entities": { "Original": { "source": "synthetic" } } }""");
            RuntimeConfig expanded = captured with
            {
                Entities = Parse("""{ "entities": { "Original": { "source": "synthetic" }, "Generated": { "source": "synthetic" } } }""").Entities
            };
            Assert.AreEqual("unknown", EngineTelemetrySnapshotFactory.Create(expanded)["entities.any.rest.configured"]);

            RuntimeConfig uncaptured = Parse("""
                { "data-source": { "database-type": "mssql" }, "entities": { "Child": { "source": "synthetic" } } }
                """);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(Merge(WithPresence("{}"), uncaptured));
            Assert.AreEqual("unknown", snapshot["entities.any.rest.configured"]);
            Assert.AreEqual("unknown", snapshot["data_sources.obo.configured"]);
        }

        [DataTestMethod]
        [DataRow("{}", "0")]
        [DataRow("{\"data-source\":null}", "0")]
        [DataRow("{\"data-source\":{\"database-type\":\"mssql\"}}", "1")]
        public void PresenceDoesNotInventADefaultDataSource(string json, string expectedCount)
        {
            Assert.AreEqual(expectedCount, EngineTelemetrySnapshotFactory.Create(WithPresence(json))["scale.data_source_count"]);
        }

        [DataTestMethod]
        [DataRow(null, null, false)]
        [DataRow("true", null, true)]
        [DataRow(" 1 ", "false", true)]
        [DataRow("true", "1", false)]
        [DataRow("1", " TrUe ", false)]
        [DataRow("yes", null, false)]
        public void LoaderCapturesOnlyUnderSyntheticGateAndPreservesPresenceThroughItsClone(string? testMode, string? optOut, bool expected)
        {
            string? previousMode = Environment.GetEnvironmentVariable(TEST_MODE_ENV_VAR);
            string? previousOptOut = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR);
            try
            {
                Environment.SetEnvironmentVariable(TEST_MODE_ENV_VAR, testMode);
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, optOut);
                using IDisposable? capture = BeginCapture(() => IsCaptureEnabled(testMode, optOut));
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(
                    """{ "data-source": { "database-type": "mssql" } }""", out RuntimeConfig? config,
                    connectionString: "synthetic override"));
                Assert.IsNotNull(config);
                Assert.AreEqual(expected, config.TelemetryPresence is not null);
                Assert.AreEqual("synthetic override", config.DataSource!.ConnectionString);
                Assert.AreEqual(expected ? "missing" : "unknown", EngineTelemetrySnapshotFactory.Create(config)["runtime.rest.configured"]);

                Assert.IsFalse(RuntimeConfigLoader.TryParseConfig("{ invalid JSON", out RuntimeConfig? invalid, out string? error));
                Assert.IsNull(invalid);
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
            finally
            {
                Environment.SetEnvironmentVariable(TEST_MODE_ENV_VAR, previousMode);
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, previousOptOut);
            }
        }

        [TestMethod]
        public void FileLoadingUsesEnabledSessionPolicyAndStopsCapturingAfterDisable()
        {
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => new DiscardingExporter(), enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            MockFileSystem files = new();
            files.AddFile("synthetic-config.json", new MockFileData("{}"));
            using FileSystemRuntimeConfigLoader loader = new(files, isCliLoader: true);
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            Assert.IsTrue(loader.TryLoadConfig("synthetic-config.json", out RuntimeConfig? enabled));
            Assert.IsNotNull(enabled.TelemetryPresence);
            Assert.IsFalse(IsCaptureEnabled(), "Permission must not survive the authorized load's scope.");
            session.Disable();
            Assert.IsTrue(loader.TryLoadConfig("synthetic-config.json", out RuntimeConfig? disabled));
            Assert.IsNull(disabled.TelemetryPresence);
        }

        [TestMethod]
        public void NestedLoadInheritsPermissionButIndependentParsingDoesNot()
        {
            using (BeginCapture(() => true))
            {
                using (BeginCapture(permission: null))
                {
                    Assert.IsTrue(RuntimeConfigLoader.TryParseConfig("{}", out RuntimeConfig? child));
                    Assert.IsNotNull(child.TelemetryPresence);
                }

                using (BeginCapture(() => false))
                {
                    Assert.IsTrue(RuntimeConfigLoader.TryParseConfig("{}", out RuntimeConfig? denied));
                    Assert.IsNull(denied.TelemetryPresence);
                }

                Assert.IsTrue(IsCaptureEnabled());
            }

            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig("{}", out RuntimeConfig? independent));
            Assert.IsNull(independent.TelemetryPresence);
        }

        private sealed class DiscardingExporter : IEngineTelemetryExporter
        {
            public System.Threading.Tasks.ValueTask<bool> ExportAsync(EngineTelemetryEvent telemetryEvent, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.ValueTask.FromResult(true);

            public void Dispose() { }
        }

        private static RuntimeConfig Parse(string json) => JsonSerializer.Deserialize<RuntimeConfig>(json, RuntimeConfigLoader.GetSerializationOptions())!;

        private static TelemetryConfigurationPresence Capture(string json)
        {
            TelemetryConfigurationPresence? presence = TryCapture(json, Parse(json), enabled: true);
            Assert.IsNotNull(presence);
            return presence;
        }

        private static RuntimeConfig WithPresence(string json)
        {
            RuntimeConfig config = Parse(json);
            TelemetryConfigurationPresence? presence = TryCapture(json, config, enabled: true);
            Assert.IsNotNull(presence);
            return config with { TelemetryPresence = presence };
        }

        private static void AssertMatchesOriginal(string json)
        {
            RuntimeConfig config = WithPresence(json);
            ImmutableDictionary<string, string> original;
            using (JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }))
            {
                original = EngineTelemetrySnapshotFactory.Create(config, document.RootElement);
            }

            // Neither the pure capture helper's document nor this explicit-input document is alive.
            ImmutableDictionary<string, string> retained = EngineTelemetrySnapshotFactory.Create(config);
            CollectionAssert.AreEquivalent(original.ToArray(), retained.ToArray());
        }

        private static RuntimeConfig Merge(RuntimeConfig root, params RuntimeConfig[] children)
        {
            // Build the already-loaded graph in memory. Do not invoke the file-loading constructor
            // with data-source-files; these tests must never open even a synthetic filename.
            RuntimeConfig[] configs = new[] { root }.Concat(children).ToArray();
            Dictionary<string, DataSource> sources = configs.SelectMany(config => config.GetDataSourceNamesToDataSourcesIterator())
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Dictionary<string, Entity> entities = configs.SelectMany(config => config.Entities)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Dictionary<string, string> mappings = configs.SelectMany(config => config.Entities.Select(pair =>
                new KeyValuePair<string, string>(pair.Key, config.GetDataSourceNameFromEntityName(pair.Key))))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            RuntimeConfig merged = new(root.Schema, root.DataSource!, root.Runtime!, new RuntimeEntities(entities),
                root.DefaultDataSourceName, sources, mappings)
            {
                TelemetryPresence = root.TelemetryPresence,
                AzureKeyVault = root.AzureKeyVault
            };
            foreach (RuntimeConfig child in children)
            {
                merged.ChildConfigs.Add(("synthetic-child-reference", child));
            }

            return merged;
        }
    }
}
