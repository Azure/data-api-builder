// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Offline, in-memory Core projections. Child declarations are attached only after
    /// construction; no files, environment, validation, identity, host or exporter is used.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [TestCategory("EngineTelemetry")]
    public class CliTelemetryConfigurationSnapshotTests
    {
        private const string PRIVATE_VALUE = "PRIVATE_CONFIGURATION_817da";
        private const string CHILD_FILE = PRIVATE_VALUE + ".json";

        [TestMethod]
        public void UnknownIsReusableAndHasTheWholeSchemaWithoutInventedDefaults()
        {
            CliTelemetryConfigurationSnapshot unknown = CliTelemetryConfigurationSnapshot.Unknown;
            Assert.AreSame(unknown, CliTelemetryConfigurationSnapshot.Unknown);
            Assert.AreSame(unknown, CliTelemetryConfigurationSnapshot.Create(null));
            Assert.AreSame(unknown, CliTelemetryConfigurationSnapshot.Create(null, saved: true));
            Dictionary<string, string> properties = Properties(unknown);
            Assert.AreEqual("cli-configuration-v1", properties["snapshot_schema"]);
            Assert.IsTrue(properties.Where(pair => pair.Key != "snapshot_schema").All(pair => pair.Value == "unknown"));
            CollectionAssert.AreEquivalent(
                EngineTelemetrySnapshotFactory.Create(NewConfig()).Keys.Append("observation").ToArray(), properties.Keys.ToArray());
            AssertWireShape(unknown);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void EveryEngineCategoryIsPreservedWithOnlyTheCliSchemaAndObservationChanged(bool saved)
        {
            RuntimeConfig config = WithPresence("""
                {
                  "data-source": { "database-type": "mssql", "options": { "set-session-context": false }, "user-delegated-auth": { "enabled": false } },
                  "azure-key-vault": { "endpoint": "https://PRIVATE_CONFIGURATION_817da.invalid" }, "autoentities": {},
                  "runtime": {
                    "rest": { "enabled": true, "request-body-strict": false },
                    "graphql": { "enabled": true, "multiple-mutations": { "create": { "enabled": true } } },
                    "mcp": { "enabled": true, "dml-tools": { "aggregate-records": { "query-timeout": 60 } } },
                    "health": { "enabled": false }, "cache": { "enabled": true, "level-2": { "enabled": true }, "ttl-seconds": 40 },
                    "host": { "mode": "development", "authentication": { "provider": "PRIVATE_CONFIGURATION_817da" }, "max-response-size-mb": 16 },
                    "pagination": { "default-page-size": 10, "max-page-size": 100 },
                    "embeddings": { "provider": "openai", "base-url": "PRIVATE_CONFIGURATION_817da", "api-key": "PRIVATE_CONFIGURATION_817da", "enabled": false, "endpoint": { "enabled": true } },
                    "telemetry": {
                      "open-telemetry": { "enabled": false }, "application-insights": { "enabled": true },
                      "azure-log-analytics": { "enabled": false }, "file": { "enabled": true, "path": "PRIVATE_CONFIGURATION_817da" }
                    }
                  },
                  "entities": {
                    "Table": { "source": "PRIVATE_CONFIGURATION_817da", "rest": false, "graphql": true, "cache": { "enabled": true }, "mcp": true, "permissions": [] },
                    "View": { "source": { "object": "PRIVATE_CONFIGURATION_817da", "type": "view" }, "permissions": [] },
                    "Procedure": { "source": { "object": "PRIVATE_CONFIGURATION_817da", "type": "stored-procedure" }, "mcp": { "custom-tool": true }, "permissions": [] }
                  }
                }
                """) with
            {
                Autoentities = new(new Dictionary<string, Autoentity> { [PRIVATE_VALUE] = new(null, null, []) })
            };
            Dictionary<string, string> properties = AssertEngineEquivalent(config, saved);
            AssertPair(properties, "runtime.rest.strict_body", "disabled", "disabled");
            AssertPair(properties, "runtime.cache.l2", "enabled", "enabled");
            AssertPair(properties, "runtime.graphql.multiple_create", "enabled", "enabled");
            AssertPair(properties, "runtime.embeddings.endpoint", "enabled", "disabled");
            AssertPair(properties, "authentication.provider", "jwt", "jwt");
            AssertPair(properties, "integrations.key_vault", "enabled", "unknown");
            AssertPair(properties, "integrations.autoentities", "enabled", "enabled");
            AssertPair(properties, "limits.default_page_size", "1-10", "1-10");
            AssertPair(properties, "limits.max_page_size", "11-100", "11-100");
            AssertPair(properties, "limits.max_response_bytes", "1048577-16777216", "1048577-16777216");
            AssertPair(properties, "limits.cache_ttl_seconds", "31-60", "31-60");
            AssertPair(properties, "limits.mcp_aggregate_query_timeout_seconds", "31-60", "31-60");
            AssertPair(properties, "limits.query_timeout_seconds", "unsupported", "unsupported");
            Assert.AreEqual("unsupported", properties["entities.any.persisted_document"]);
            Assert.AreEqual("unsupported", properties["entities.any.parameter_embeddings"]);
        }

        [TestMethod]
        public void MissingExplicitNullAndUnavailableProvenanceRemainDifferent()
        {
            RuntimeConfig missing = WithPresence("""
                { "entities": { "Entity": { "source": "synthetic", "permissions": [] } } }
                """);
            RuntimeConfig explicitNull = WithPresence("""
                { "runtime": null, "entities": { "Entity": { "source": "synthetic", "rest": null, "graphql": null, "cache": { "enabled": null }, "permissions": [] } } }
                """);
            Dictionary<string, string> omitted = AssertEngineEquivalent(missing);
            Dictionary<string, string> nulls = AssertEngineEquivalent(explicitNull);
            Dictionary<string, string> unavailable = AssertEngineEquivalent(missing with { TelemetryPresence = null });
            foreach (string key in new[] { "runtime.rest", "runtime.graphql", "runtime.cache", "entities.any.rest", "entities.any.graphql", "entities.any.cache" })
            {
                Assert.AreEqual("missing", omitted[key + ".configured"], key);
                Assert.AreEqual("unknown", nulls[key + ".configured"], key);
                Assert.AreEqual("unknown", unavailable[key + ".configured"], key);
            }

            Assert.AreEqual("enabled", unavailable["runtime.rest.effective"]);
            Assert.AreEqual("loaded", unavailable["observation"], "A known model without input provenance is not a missing model.");
        }

        [TestMethod]
        public void SavedObservationUsesTheSuppliedSavedRepresentationPresenceWithoutRecapturingInput()
        {
            RuntimeConfig loaded = WithPresence("{}");
            RuntimeConfig saved = loaded with { Runtime = EmptyRuntime() with { Rest = new(Enabled: true) } };
            saved = AttachPresence(saved, """{ "runtime": { "rest": { "enabled": true } } }""");
            Assert.AreEqual("missing", Properties(CliTelemetryConfigurationSnapshot.Create(loaded))["runtime.rest.configured"]);
            Dictionary<string, string> after = AssertEngineEquivalent(saved, saved: true);
            AssertPair(after, "runtime.rest", "enabled", "enabled");
            Assert.AreEqual("saved", after["observation"]);
            Assert.AreEqual("unknown", AssertEngineEquivalent(saved with { TelemetryPresence = null }, saved: true)["runtime.rest.configured"]);
        }

        [TestMethod]
        public void ConfiguredIntentDoesNotBypassRuntimeOrDatabaseGates()
        {
            RuntimeOptions runtime = EmptyRuntime() with
            {
                Cache = new RuntimeCacheOptions(true, 300) { Level2 = new(true) },
                Rest = new(Enabled: false),
                GraphQL = new(Enabled: false),
                Mcp = new(Enabled: false)
            };
            Entity entity = NewEntity() with { Cache = new(true), Mcp = new(true, true) };
            DataSource source = new(DatabaseType.MSSQL, PRIVATE_VALUE, new() { ["set-session-context"] = true });
            Dictionary<string, string> properties = AssertEngineEquivalent(NewConfig(runtime, new() { [PRIVATE_VALUE] = entity }, source));
            AssertPair(properties, "runtime.cache", "enabled", "disabled");
            AssertPair(properties, "runtime.cache.l2", "enabled", "disabled");
            AssertPair(properties, "entities.any.cache", "enabled", "disabled");
            AssertPair(properties, "data_sources.session_context", "enabled", "enabled");
            Assert.AreEqual("disabled", properties["entities.any.rest.effective"]);
            Assert.AreEqual("disabled", properties["entities.any.graphql.effective"]);
            Assert.AreEqual("disabled", properties["entities.any.mcp_dml.effective"]);

            RuntimeConfig cosmos = WithPresence("""
                { "data-source": { "database-type": "cosmosdb_nosql" }, "runtime": { "rest": true },
                  "entities": { "Entity": { "source": "synthetic", "rest": true, "permissions": [] } } }
                """);
            Dictionary<string, string> cosmosProperties = AssertEngineEquivalent(cosmos);
            AssertPair(cosmosProperties, "runtime.rest", "enabled", "disabled");
            AssertPair(cosmosProperties, "entities.any.rest", "enabled", "disabled");
            AssertPair(cosmosProperties, "data_sources.obo", "unsupported", "unsupported");
            AssertPair(cosmosProperties, "data_sources.session_context", "unsupported", "unsupported");
        }

        [DataTestMethod]
        [DataRow(EntitySourceType.Table, true, true, false, "enabled", "not_applicable")]
        [DataRow(EntitySourceType.View, true, false, true, "disabled", "not_applicable")]
        [DataRow(EntitySourceType.StoredProcedure, true, true, false, "disabled", "enabled")]
        [DataRow(EntitySourceType.StoredProcedure, true, false, true, "enabled", "enabled")]
        [DataRow(EntitySourceType.StoredProcedure, false, true, true, "disabled", "disabled")]
        public void McpDmlToolsAndCustomStoredProcedureApplicabilityUseEngineRules(
            EntitySourceType type, bool mcp, bool read, bool execute, string dmlEffective, string customEffective)
        {
            RuntimeOptions runtime = EmptyRuntime() with
            {
                Mcp = new(Enabled: mcp, DmlTools: new(allToolsEnabled: false, readRecords: read, executeEntity: execute))
            };
            RuntimeConfig config = NewConfig(runtime, new() { [PRIVATE_VALUE] = NewEntity(type) with { Mcp = new(true, true) } });
            Dictionary<string, string> properties = AssertEngineEquivalent(config);
            AssertPair(properties, "entities.any.mcp_dml", "enabled", dmlEffective);
            AssertPair(properties, "entities.any.mcp_custom_tool", type == EntitySourceType.StoredProcedure ? "enabled" : "not_applicable", customEffective);
        }

        [TestMethod]
        public void MissingMcpToolModelRemainsUnknownAndEntityDisableStillWins()
        {
            RuntimeConfig config = NewConfig(entities: new() { [PRIVATE_VALUE] = NewEntity() });
            Assert.AreEqual("unknown", AssertEngineEquivalent(config)["entities.any.mcp_dml.effective"]);
            RuntimeConfig disabled = config with
            {
                Entities = RawEntities(new() { [PRIVATE_VALUE] = NewEntity() with { Mcp = new(null, false) } })
            };
            AssertPair(AssertEngineEquivalent(disabled), "entities.any.mcp_dml", "disabled", "disabled");
        }

        [TestMethod]
        public void RolesPolicyPresenceRelationshipsAndAllDestinationsCannotLeakCustomerValues()
        {
            RuntimeOptions runtime = EmptyRuntime() with
            {
                Rest = new(Path: PRIVATE_VALUE),
                GraphQL = new(Path: PRIVATE_VALUE),
                Mcp = new(Path: PRIVATE_VALUE, Description: PRIVATE_VALUE, AllowedHosts: [PRIVATE_VALUE]),
                Host = new(null, new(PRIVATE_VALUE), HostMode.Development),
                BaseRoute = PRIVATE_VALUE,
                Cache = new RuntimeCacheOptions(true, 72) { Level2 = new(true, PRIVATE_VALUE, PRIVATE_VALUE, PRIVATE_VALUE) },
                Embeddings = new(EmbeddingProviderType.OpenAI, PRIVATE_VALUE, PRIVATE_VALUE, Model: PRIVATE_VALUE, Endpoint: new(true, [PRIVATE_VALUE], PRIVATE_VALUE)),
                Telemetry = new(
                    ApplicationInsights: new(true, PRIVATE_VALUE),
                    OpenTelemetry: new(true, "https://" + PRIVATE_VALUE + ".invalid", PRIVATE_VALUE, ServiceName: PRIVATE_VALUE),
                    AzureLogAnalytics: new(true, new(PRIVATE_VALUE, PRIVATE_VALUE, PRIVATE_VALUE), PRIVATE_VALUE),
                    File: new(true, PRIVATE_VALUE))
            };
            Entity entity = NewEntity(EntitySourceType.StoredProcedure) with
            {
                Description = new string('x', 9000) + PRIVATE_VALUE,
                Source = new(PRIVATE_VALUE, EntitySourceType.StoredProcedure,
                    [new ParameterMetadata { Name = PRIVATE_VALUE, Description = PRIVATE_VALUE, Default = PRIVATE_VALUE }], [PRIVATE_VALUE]),
                Permissions = [new("Anonymous", []), new("AUTHENTICATED", []), new(PRIVATE_VALUE, [new(EntityActionOperation.Execute, null, new(PRIVATE_VALUE, PRIVATE_VALUE))])],
                Mappings = new() { [PRIVATE_VALUE] = PRIVATE_VALUE },
                Relationships = new() { [PRIVATE_VALUE] = Relationship() },
                Mcp = new(true, true)
            };
            RuntimeConfig config = NewConfig(runtime, new() { [PRIVATE_VALUE] = entity },
                new DataSource(DatabaseType.MSSQL, "@env('" + PRIVATE_VALUE + "')", new() { [PRIVATE_VALUE] = new NeverStringify() })
                { UserDelegatedAuth = new(true, PRIVATE_VALUE, PRIVATE_VALUE) }) with
            { Schema = PRIVATE_VALUE, AzureKeyVault = new(PRIVATE_VALUE + ".akv") };
            Dictionary<string, string> properties = AssertEngineEquivalent(config);
            foreach (string key in new[] { "custom_roles", "request_policy", "database_policy", "policies", "descriptions", "relationships" })
            {
                Assert.AreEqual("enabled", properties["entities.any." + key], key);
            }

            foreach (string key in new[] { "application_insights", "open_telemetry", "log_analytics", "file" })
            {
                Assert.AreEqual("enabled", properties["customer_telemetry." + key + ".effective"], key);
            }

            Assert.AreEqual("jwt", properties["authentication.provider.effective"]);
            Entity reservedOnly = entity with { Permissions = [new("anonymous", []), new("Authenticated", [])], Description = "", Relationships = new() };
            Dictionary<string, string> negatives = AssertEngineEquivalent(NewConfig(entities: new() { [PRIVATE_VALUE] = reservedOnly }));
            foreach (string key in new[] { "custom_roles", "request_policy", "database_policy", "policies", "descriptions", "relationships" })
            {
                Assert.AreEqual("disabled", negatives["entities.any." + key], key);
            }
        }

        [DataTestMethod]
        [DataRow(0, "0")]
        [DataRow(1, "1")]
        [DataRow(2, "2-10")]
        [DataRow(10, "2-10")]
        [DataRow(11, "11-50")]
        [DataRow(50, "11-50")]
        [DataRow(51, "51-100")]
        [DataRow(100, "51-100")]
        [DataRow(101, "101-500")]
        [DataRow(500, "101-500")]
        [DataRow(501, "501+")]
        public void EntityCountBucketsKeepTheEngineBoundaries(int count, string expected)
        {
            RuntimeConfig config = NewConfig(entities: EntitySet(count));
            Assert.AreEqual(expected, AssertEngineEquivalent(config)["scale.entity_count"]);
        }

        [TestMethod]
        public void ProviderChangingCloneUsesTheActualRootAndKeepsOtherMergedSourcesWithoutMutation()
        {
            RuntimeConfig loaded = Merge(NewConfig(), (CHILD_FILE, NewConfig(source: new(DatabaseType.MySQL, PRIVATE_VALUE))));
            RuntimeConfig changed = loaded with { DataSource = new(DatabaseType.CosmosDB_NoSQL, PRIVATE_VALUE) };
            Assert.AreEqual(DatabaseType.MSSQL, changed.GetDataSourceFromDataSourceName(changed.DefaultDataSourceName).DatabaseType);
            Dictionary<string, string> properties = AssertEngineEquivalent(changed, expected: NormalizeRootMap(changed));
            Assert.AreEqual("mysql,cosmosdb_nosql", properties["data_sources.types"]);
            Assert.AreEqual("2", properties["data_sources.distinct_type_count"]);
            Assert.AreEqual("2-10", properties["scale.data_source_count"]);
            Assert.AreEqual("disabled", properties["runtime.rest.effective"]);
            Assert.AreEqual(DatabaseType.MSSQL, changed.GetDataSourceFromDataSourceName(changed.DefaultDataSourceName).DatabaseType);
            Assert.AreEqual("mssql,mysql", Properties(CliTelemetryConfigurationSnapshot.Create(loaded))["data_sources.types"]);

            RuntimeConfig removed = changed with { DataSource = null };
            Assert.AreEqual("mysql", AssertEngineEquivalent(removed, expected: NormalizeRootMap(removed))["data_sources.types"]);
        }

        [TestMethod]
        public void CurrentRootEntitiesAreNotRemergedFromOldChildrenAndTheirMissingProvenanceIsNotInvented()
        {
            RuntimeConfig child = WithPresence("""
                { "data-source": { "database-type": "mssql" }, "entities": { "Old": { "source": "synthetic", "permissions": [] } } }
                """);
            RuntimeConfig loaded = Merge(WithPresence("{}"), (CHILD_FILE, child));
            RuntimeConfig saved = loaded with { Entities = RawEntities(new() { ["Current"] = NewEntity() with { Rest = new(Enabled: false) } }) };
            saved = AttachPresence(saved, """
                { "data-source-files": ["PRIVATE_CONFIGURATION_817da.json"], "entities": { "Current": { "source": "synthetic", "rest": false, "permissions": [] } } }
                """);
            Dictionary<string, string> properties = Properties(CliTelemetryConfigurationSnapshot.Create(saved, saved: true));
            Assert.AreEqual("saved", properties["observation"]);
            Assert.AreEqual("1", properties["scale.entity_count"]);
            Assert.AreEqual("disabled", properties["entities.any.rest.effective"]);
            Assert.AreEqual("unknown", properties["entities.any.rest.configured"]);
            Assert.AreEqual("unknown", properties["entities.any.cache.configured"]);
            Assert.AreSame(child, saved.ChildConfigs.Single().Config);
            Assert.IsTrue(child.Entities.ContainsKey("Old"));

            RuntimeConfig empty = saved with { Entities = RawEntities(new()) };
            Dictionary<string, string> removed = Properties(CliTelemetryConfigurationSnapshot.Create(empty, saved: true));
            Assert.AreEqual("0", removed["scale.entity_count"]);
            Assert.AreEqual("not_applicable", removed["entities.any.rest.effective"]);
            Assert.AreEqual("unknown", removed["entities.any.rest.configured"]);
        }

        [TestMethod]
        public void NestedLoadedChildProvenanceIsKeptForSourcesEntitiesAndKeyVault()
        {
            RuntimeConfig child = WithPresence("""
                { "data-source": { "database-type": "mssql", "options": null, "user-delegated-auth": null }, "azure-key-vault": null,
                  "entities": { "Child": { "source": "synthetic", "rest": null, "permissions": [] } } }
                """);
            RuntimeConfig root = Merge(WithPresence("{}"), (CHILD_FILE, Merge(WithPresence("{}"), ("nested", child))));
            root = AttachPresence(root, """{ "data-source-files": ["PRIVATE_CONFIGURATION_817da.json"] }""");
            Dictionary<string, string> properties = AssertEngineEquivalent(root);
            foreach (string key in new[] { "entities.any.rest", "data_sources.obo", "data_sources.session_context", "integrations.key_vault" })
            {
                Assert.AreEqual("unknown", properties[key + ".configured"], key);
            }

            AssertPair(properties, "integrations.multiple_source_files", "enabled", "enabled");
            Assert.AreEqual("1", properties["scale.data_source_count"]);
            Assert.AreEqual("1", properties["scale.entity_count"]);
            Assert.AreSame(child.TelemetryPresence, root.ChildConfigs[0].Config.ChildConfigs[0].Config.TelemetryPresence);
        }

        [DataTestMethod]
        [DataRow("missing")]
        [DataRow("reordered")]
        [DataRow("removed")]
        [DataRow("empty")]
        [DataRow("null_list")]
        [DataRow("null_child")]
        [DataRow("null_name")]
        [DataRow("extra_child")]
        [DataRow("unmerged")]
        [DataRow("mismatched_source")]
        public void IncompleteOrMismatchedLoadedChildrenFailClosedWithoutLoadingDeclarations(string change)
        {
            RuntimeConfig first = NewConfig();
            RuntimeConfig root = Merge(NewConfig(), (CHILD_FILE, first), ("second", NewConfig()));
            root = change switch
            {
                "missing" => root with { DataSourceFiles = new([CHILD_FILE, "second", "unloaded"]) },
                "reordered" => root with { DataSourceFiles = new(["second", CHILD_FILE]) },
                "removed" => root with { DataSourceFiles = null },
                "empty" => root with { DataSourceFiles = new([]) },
                "null_list" => root with { DataSourceFiles = new() },
                "unmerged" => NewConfig() with { DataSourceFiles = new([CHILD_FILE]) },
                _ => root
            };
            switch (change)
            {
                case "null_child":
                    root.ChildConfigs[0] = (CHILD_FILE, null!);
                    break;
                case "null_name":
                    root.ChildConfigs[0] = (null!, first);
                    break;
                case "extra_child":
                    root.ChildConfigs.Add(("extra", first));
                    break;
                case "unmerged":
                    root.ChildConfigs.Add((CHILD_FILE, first));
                    break;
                case "mismatched_source":
                    root.UpdateDataSourceNameToDataSource(first.DefaultDataSourceName, new(DatabaseType.MySQL, PRIVATE_VALUE));
                    break;
            }

            Assert.AreSame(CliTelemetryConfigurationSnapshot.Unknown, CliTelemetryConfigurationSnapshot.Create(root, saved: true));
        }

        [TestMethod]
        public void UnloadedDeclarationWithNoChildrenDoesNotBecomeAKnownPartialMap()
        {
            RuntimeConfig root = NewConfig() with { DataSourceFiles = new([CHILD_FILE]) };
            AssertUnknown(root);
            Assert.AreEqual(0, root.ChildConfigs.Count);
            AssertEngineEquivalent(NewConfig() with { DataSourceFiles = new() });
            AssertEngineEquivalent(NewConfig() with { DataSourceFiles = new([]) });
        }

        [TestMethod]
        public void SharedChildReferencesAreNotCyclesOrDuplicatePresenceButActiveAncestorCyclesFailClosed()
        {
            RuntimeConfig child = WithPresence("""
                { "data-source": { "database-type": "mssql" }, "entities": { "Child": { "source": "synthetic", "rest": false, "permissions": [] } } }
                """);
            RuntimeConfig root = Merge(WithPresence("{}"), (CHILD_FILE, child), (CHILD_FILE, child));
            Dictionary<string, string> properties = AssertEngineEquivalent(root);
            Assert.AreEqual("disabled", properties["entities.any.rest.configured"]);
            Assert.AreEqual("1", properties["scale.entity_count"]);
            RuntimeConfig cyclicChild = child with { DataSourceFiles = new(["ancestor"]) };
            root.ChildConfigs[0] = (CHILD_FILE, cyclicChild);
            cyclicChild.ChildConfigs.Add(("ancestor", root));
            AssertUnknown(root);
        }

        [DataTestMethod]
        [DataRow(32, true)]
        [DataRow(33, false)]
        [DataRow(500, false)]
        public void DepthIsConservativelyBoundedBeforeRecursingFurther(int depth, bool known)
        {
            RuntimeConfig root = NewConfig();
            for (int index = 1; index < depth; index++)
            {
                root = Merge(WithPresence("{}"), (CHILD_FILE, root));
            }

            Assert.AreEqual(known ? "loaded" : "unknown", Properties(CliTelemetryConfigurationSnapshot.Create(root))["observation"]);
        }

        [TestMethod]
        public void PreviouslyPreparedSharedSubtreeStillCountsTowardDepthOnALongerPath()
        {
            RuntimeConfig shared = Merge(WithPresence("{}"), (CHILD_FILE, NewConfig()));
            RuntimeConfig longer = shared;
            for (int index = 0; index < CliTelemetryConfigurationSnapshot.MAX_DEPTH - 2; index++)
            {
                longer = Merge(WithPresence("{}"), (CHILD_FILE, longer));
            }

            AssertUnknown(Merge(WithPresence("{}"), ("short", shared), ("long", longer)));
        }

        [DataTestMethod]
        [DataRow(255, true)]
        [DataRow(256, false)]
        public void SourcesShareThe256NodeBudgetWithConfigurations(int count, bool known)
        {
            Dictionary<string, DataSource> sources = Enumerable.Range(0, count).ToDictionary(
                index => index.ToString(CultureInfo.InvariantCulture), _ => new DataSource(DatabaseType.MSSQL, PRIVATE_VALUE));
            RuntimeConfig root = new(string.Empty, sources["0"], EmptyRuntime(), RawEntities(new()), "0", sources, new());
            Dictionary<string, string> properties = Properties(CliTelemetryConfigurationSnapshot.Create(root));
            Assert.AreEqual(known ? "loaded" : "unknown", properties["observation"]);
            Assert.AreEqual(known ? "101-500" : "unknown", properties["scale.data_source_count"]);
        }

        [DataTestMethod]
        [DataRow(255, true)]
        [DataRow(256, false)]
        public void WideConfigurationGraphsAlsoShareTheBudget(int childCount, bool known)
        {
            (string, RuntimeConfig)[] children = Enumerable.Range(0, childCount)
                .Select(index => (index.ToString(CultureInfo.InvariantCulture), WithPresence("{}"))).ToArray();
            RuntimeConfig root = Merge(WithPresence("{}"), children);
            Assert.AreEqual(known ? "loaded" : "unknown", Properties(CliTelemetryConfigurationSnapshot.Create(root))["observation"]);
        }

        [TestMethod]
        public void SharedReferencesCannotEvadeTheEdgeBudget()
        {
            RuntimeConfig child = WithPresence("{}");
            (string, RuntimeConfig)[] children = Enumerable.Repeat((CHILD_FILE, child), CliTelemetryConfigurationSnapshot.MAX_CHILD_REFERENCES + 1).ToArray();
            AssertUnknown(Merge(WithPresence("{}"), children));
        }

        [DataTestMethod]
        [DataRow(4096, true)]
        [DataRow(4097, false)]
        public void EntityEnumerationHonorsTheSharedModelBudget(int count, bool known)
        {
            RuntimeConfig config = NewConfig(entities: EntitySet(count));
            Assert.AreEqual(known ? "loaded" : "unknown", Properties(CliTelemetryConfigurationSnapshot.Create(config))["observation"]);
        }

        [DataTestMethod]
        [DataRow(false, 4094, true)]
        [DataRow(false, 4095, false)]
        [DataRow(true, 4094, true)]
        [DataRow(true, 4095, false)]
        public void NestedActionsAndPermissionsConsumeTheSameBudgetAsEntitiesAndAutoentities(bool autoentity, int actionCount, bool known)
        {
            EntityAction[] actions = Enumerable.Repeat(new EntityAction(EntityActionOperation.Read, null, null), actionCount).ToArray();
            EntityPermission[] permissions = [new("anonymous", actions)];
            RuntimeConfig config = autoentity
                ? NewConfig() with { Autoentities = new(new Dictionary<string, Autoentity> { [PRIVATE_VALUE] = new(null, null, permissions) }) }
                : NewConfig(entities: new() { [PRIVATE_VALUE] = NewEntity() with { Permissions = permissions } });
            Assert.AreEqual(known ? "loaded" : "unknown", Properties(CliTelemetryConfigurationSnapshot.Create(config))["observation"]);
        }

        [TestMethod]
        public void RelationshipNodesCannotEscapeTheSharedBudget()
        {
            Dictionary<string, EntityRelationship> relationships = Enumerable.Range(0, CliTelemetryConfigurationSnapshot.MAX_MODEL_NODES)
                .ToDictionary(index => index.ToString(CultureInfo.InvariantCulture), _ => Relationship());
            AssertUnknown(NewConfig(entities: new() { [PRIVATE_VALUE] = NewEntity() with { Relationships = relationships } }));
        }

        [DataTestMethod]
        [DataRow("entities")]
        [DataRow("entity_dictionary")]
        [DataRow("entity")]
        [DataRow("source")]
        [DataRow("permissions")]
        [DataRow("permission")]
        [DataRow("role")]
        [DataRow("actions")]
        [DataRow("action")]
        [DataRow("relationship")]
        [DataRow("autoentities")]
        [DataRow("autoentity")]
        [DataRow("autoentity_permissions")]
        [DataRow("mapped_source")]
        public void MalformedRequiredNodesFailClosedRatherThanBecomingDisabledFeatures(string node)
        {
            Entity entity = node switch
            {
                "entity" => null!,
                "source" => NewEntity() with { Source = null! },
                "permissions" => NewEntity() with { Permissions = null! },
                "permission" => NewEntity() with { Permissions = [null!] },
                "role" => NewEntity() with { Permissions = [new(null!, [])] },
                "actions" => NewEntity() with { Permissions = [new("anonymous", null!)] },
                "action" => NewEntity() with { Permissions = [new("anonymous", [null!])] },
                "relationship" => NewEntity() with { Relationships = new() { [PRIVATE_VALUE] = null! } },
                _ => NewEntity()
            };
            RuntimeConfig config = NewConfig(entities: new() { [PRIVATE_VALUE] = entity });
            config = node switch
            {
                "entities" => config with { Entities = null! },
                "entity_dictionary" => config with { Entities = RawEntities(new()) with { Entities = null! } },
                "autoentities" => config with { Autoentities = null! },
                "autoentity" => config with { Autoentities = new(new Dictionary<string, Autoentity> { [PRIVATE_VALUE] = null! }) },
                "autoentity_permissions" => config with { Autoentities = new(new Dictionary<string, Autoentity> { [PRIVATE_VALUE] = new Autoentity(null, null, []) with { Permissions = null! } }) },
                _ => config
            };
            if (node == "mapped_source")
            {
                Dictionary<string, DataSource> sources = new() { ["root"] = new(DatabaseType.MSSQL, PRIVATE_VALUE) };
                config = new(string.Empty, sources["root"], EmptyRuntime(), RawEntities(new()), "root", sources, new());
                sources.Add("malformed", null!); // Inject after RuntimeConfig's own setup.
            }

            AssertUnknown(config);
        }

        [TestMethod]
        public void InvalidEnumsAndInvalidOptionValuesRetainEngineUnknownCategoriesWithoutRawNumbersOrObjects()
        {
            RuntimeConfig config = NewConfig(EmptyRuntime() with { Host = new(null, null, (HostMode)98173) },
                new() { [PRIVATE_VALUE] = NewEntity((EntitySourceType)98174) }, new((DatabaseType)98175, PRIVATE_VALUE));
            Dictionary<string, string> properties = AssertEngineEquivalent(config);
            Assert.AreEqual("unknown", properties["data_sources.types"]);
            Assert.AreEqual("unknown", properties["host.mode.effective"]);
            Assert.AreEqual("unknown", properties["entities.any.table"]);
            RuntimeConfig invalidOption = NewConfig(source: new(DatabaseType.MSSQL, PRIVATE_VALUE,
                new() { ["set-session-context"] = new NeverStringify() }));
            AssertPair(AssertEngineEquivalent(invalidOption), "data_sources.session_context", "unknown", "disabled");
        }

        [TestMethod]
        public void BasePreparationDoesNotInvokeArbitraryRuntimeOverrides()
        {
            RuntimeConfig ordinary = NewConfig(EmptyRuntime() with { Cache = new(true, 60) }, new() { [PRIVATE_VALUE] = NewEntity() });
            RuntimeConfig derived = new HostileRuntimeConfig(ordinary);
            AssertEngineEquivalent(derived, expected: ordinary);
        }

        [TestMethod]
        public void CustomEntityDictionaryIsReadOnceWithoutTrustingCountOrPassingItToTheFactory()
        {
            int enumerations = 0;
            Dictionary<string, Entity> entities = new() { [PRIVATE_VALUE] = NewEntity() };
            IEnumerable<KeyValuePair<string, Entity>> Entries()
            {
                Assert.AreEqual(1, ++enumerations);
                foreach (KeyValuePair<string, Entity> entry in entities)
                {
                    yield return entry;
                }
            }

            RuntimeConfig ordinary = NewConfig(EmptyRuntime() with { Cache = new(true) }, entities);
            RuntimeConfig custom = ordinary with { Entities = RawEntities(new()) with { Entities = new SequenceDictionary<Entity>(Entries()) } };
            AssertEngineEquivalent(custom, expected: ordinary);
            Assert.AreEqual(1, enumerations);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UnboundedModelDictionariesStopAfterTheBudgetAndOneLookahead(bool autoentities)
        {
            int moves = 0;
            int disposals = 0;
            IEnumerable<KeyValuePair<string, Entity>> Entries()
            {
                try
                {
                    while (true)
                    {
                        yield return new((++moves).ToString(CultureInfo.InvariantCulture), NewEntity());
                    }
                }
                finally
                {
                    disposals++;
                }
            }

            RuntimeConfig config = autoentities
                ? NewConfig() with
                {
                    Autoentities = new(new SequenceDictionary<Autoentity>(Entries().Select(pair =>
                        new KeyValuePair<string, Autoentity>(pair.Key, new(null, null, [])))))
                }
                : NewConfig() with { Entities = RawEntities(new()) with { Entities = new SequenceDictionary<Entity>(Entries()) } };
            AssertUnknown(config);
            Assert.AreEqual(CliTelemetryConfigurationSnapshot.MAX_MODEL_NODES + 1, moves);
            Assert.AreEqual(1, disposals);
        }

        [DataTestMethod]
        [DataRow("duplicate")]
        [DataRow("null_key")]
        [DataRow("null_value")]
        public void InvalidDictionaryEntriesCannotProduceAPartialProjection(string invalid)
        {
            KeyValuePair<string, Entity>[] entries = invalid switch
            {
                "duplicate" => [new(PRIVATE_VALUE, NewEntity()), new(PRIVATE_VALUE, NewEntity())],
                "null_key" => [new(null!, NewEntity())],
                _ => [new(PRIVATE_VALUE, null!)]
            };
            RuntimeConfig config = NewConfig() with
            {
                Entities = RawEntities(new()) with { Entities = new SequenceDictionary<Entity>(entries) }
            };
            AssertUnknown(config);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        public void UnboundedSourceDeclarationsUseOnlyLoadedChildrenAndOneLookahead(int childCount)
        {
            int moves = 0;
            int disposals = 0;
            IEnumerable<string> References()
            {
                try
                {
                    while (true)
                    {
                        moves++;
                        yield return CHILD_FILE;
                    }
                }
                finally
                {
                    System.Threading.Interlocked.Increment(ref disposals);
                }
            }

            RuntimeConfig config = childCount == 0 ? NewConfig() : Merge(NewConfig(), (CHILD_FILE, NewConfig()));
            config = config with { DataSourceFiles = new(References()) };
            AssertUnknown(config);
            Assert.AreEqual(childCount + 1, moves);
            Assert.AreEqual(1, disposals);
        }

        [TestMethod]
        public void SourceDeclarationsAreFrozenBeforeTheEngineFactoryUsesAny()
        {
            int enumerations = 0;
            IEnumerable<string> References()
            {
                Assert.AreEqual(1, ++enumerations);
                yield return CHILD_FILE;
            }

            RuntimeConfig ordinary = Merge(NewConfig(), (CHILD_FILE, NewConfig()));
            RuntimeConfig custom = ordinary with { DataSourceFiles = new(References()) };
            AssertEngineEquivalent(custom, expected: ordinary);
            Assert.AreEqual(1, enumerations);
        }

        [DataTestMethod]
        [DataRow("get_enumerator")]
        [DataRow("move_next")]
        [DataRow("current")]
        [DataRow("dispose")]
        public void AllEnumerationFailurePhasesReturnTheSameSafeFallback(string failure)
        {
            RuntimeConfig references = Merge(NewConfig(), (CHILD_FILE, NewConfig())) with
            { DataSourceFiles = new(new FaultingSequence<string>(CHILD_FILE, failure)) };
            AssertUnknown(references);
            RuntimeConfig entities = NewConfig() with
            {
                Entities = RawEntities(new()) with
                {
                    Entities = new SequenceDictionary<Entity>(new FaultingSequence<KeyValuePair<string, Entity>>(new(PRIVATE_VALUE, NewEntity()), failure))
                }
            };
            AssertUnknown(entities);
            RuntimeConfig autoentities = NewConfig() with
            {
                Autoentities = new(new SequenceDictionary<Autoentity>(new FaultingSequence<KeyValuePair<string, Autoentity>>(new(PRIVATE_VALUE, new(null, null, [])), failure)))
            };
            AssertUnknown(autoentities);
        }

        [TestMethod]
        public void SnapshotRetainsOnlyItsImmutableStringAndLaterModelMutationsCannotChangeIt()
        {
            Dictionary<string, Entity> entities = new()
            {
                [PRIVATE_VALUE] = NewEntity() with { Permissions = [new(PRIVATE_VALUE, [])] }
            };
            RuntimeConfig config = NewConfig(EmptyRuntime() with { Host = new(null, null, HostMode.Development), Cache = new(true) }, entities,
                new(DatabaseType.MSSQL, PRIVATE_VALUE, new() { ["set-session-context"] = false }));
            CliTelemetryConfigurationSnapshot snapshot = CliTelemetryConfigurationSnapshot.Create(config);
            string before = snapshot.Json;
            config.Runtime!.Host!.Mode = HostMode.Production;
            config.DataSource!.Options!["set-session-context"] = true;
            entities[PRIVATE_VALUE].Permissions[0] = new("anonymous", []);
            entities.Clear();
            Assert.AreSame(before, snapshot.Json);
            Assert.AreEqual("development", Properties(snapshot)["host.mode.effective"]);
            Assert.AreEqual("enabled", Properties(snapshot)["entities.any.custom_roles"]);
            Dictionary<string, string> next = AssertEngineEquivalent(config);
            Assert.AreEqual("production", next["host.mode.effective"]);
            Assert.AreEqual("disabled", next["runtime.cache.effective"]);
            Assert.AreEqual("0", next["scale.entity_count"]);
            FieldInfo[] fields = typeof(CliTelemetryConfigurationSnapshot).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(typeof(string), fields[0].FieldType);
            Assert.IsTrue(fields[0].IsInitOnly);
            Assert.IsTrue(typeof(CliTelemetryConfigurationSnapshot).IsSealed);
        }

        [TestMethod]
        public void SerializationIsOrdinalStableAndBoundedRegardlessOfInputDictionaryOrder()
        {
            Dictionary<string, Entity> entities = EntitySet(501);
            RuntimeConfig forward = NewConfig(entities: entities);
            RuntimeConfig reverse = forward with { Entities = RawEntities(entities.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)) };
            CliTelemetryConfigurationSnapshot first = CliTelemetryConfigurationSnapshot.Create(forward);
            CliTelemetryConfigurationSnapshot second = CliTelemetryConfigurationSnapshot.Create(reverse);
            Assert.AreEqual(first.Json, second.Json);
            AssertWireShape(first);
            AssertWireShape(CliTelemetryConfigurationSnapshot.Unknown);
        }

        private static RuntimeOptions EmptyRuntime() => new(Rest: null, GraphQL: null, Mcp: null, Host: null);

        private static Entity NewEntity(EntitySourceType type = EntitySourceType.Table)
            => new(new(PRIVATE_VALUE, type, null, null), new(PRIVATE_VALUE, PRIVATE_VALUE), null, new(), [], null, null);

        private static EntityRelationship Relationship() => new(Cardinality.One, PRIVATE_VALUE, [PRIVATE_VALUE], [PRIVATE_VALUE], PRIVATE_VALUE, [PRIVATE_VALUE], [PRIVATE_VALUE]);

        private static RuntimeEntities RawEntities(Dictionary<string, Entity> entities)
            => new(new Dictionary<string, Entity>()) { Entities = entities };

        private static Dictionary<string, Entity> EntitySet(int count) => Enumerable.Range(0, count)
            .ToDictionary(index => PRIVATE_VALUE + index.ToString(CultureInfo.InvariantCulture), _ => NewEntity());

        private static RuntimeConfig NewConfig(RuntimeOptions? runtime = null, Dictionary<string, Entity>? entities = null, DataSource? source = null)
        {
            string name = Guid.NewGuid().ToString("N");
            source ??= new(DatabaseType.MSSQL, PRIVATE_VALUE);
            return new(string.Empty, source, runtime!, RawEntities(entities ?? new()), name, new() { [name] = source }, new());
        }

        private static RuntimeConfig WithPresence(string json)
        {
            RuntimeConfig config = JsonSerializer.Deserialize<RuntimeConfig>(json,
                RuntimeConfigLoader.GetSerializationOptions(new DeserializationVariableReplacementSettings(doReplaceEnvVar: false, doReplaceAkvVar: false)))!;
            return AttachPresence(config, json);
        }

        private static RuntimeConfig AttachPresence(RuntimeConfig config, string json)
        {
            TelemetryConfigurationPresence? presence = TelemetryConfigurationPresence.TryCapture(json, config, enabled: true);
            Assert.IsNotNull(presence);
            return config with { TelemetryPresence = presence };
        }

        private static RuntimeConfig Merge(RuntimeConfig root, params (string FileName, RuntimeConfig Config)[] children)
        {
            Dictionary<string, DataSource> sources = root.GetDataSourceNamesToDataSourcesIterator().ToDictionary(pair => pair.Key, pair => pair.Value);
            Dictionary<string, Entity> entities = root.Entities.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach ((_, RuntimeConfig child) in children)
            {
                foreach ((string name, DataSource source) in child.GetDataSourceNamesToDataSourcesIterator())
                {
                    sources.TryAdd(name, source);
                }

                foreach ((string name, Entity entity) in child.Entities)
                {
                    entities.TryAdd(name, entity);
                }
            }

            RuntimeConfig merged = new(string.Empty, root.DataSource!, root.Runtime!, RawEntities(entities), root.DefaultDataSourceName, sources, new(),
                AzureKeyVault: root.AzureKeyVault, Autoentities: root.Autoentities)
            { TelemetryPresence = root.TelemetryPresence };
            merged = merged with { DataSourceFiles = new(children.Select(child => child.FileName).ToArray()) };
            merged.ChildConfigs.AddRange(children);
            return merged;
        }

        private static RuntimeConfig NormalizeRootMap(RuntimeConfig root)
        {
            Dictionary<string, DataSource> sources = root.GetDataSourceNamesToDataSourcesIterator().ToDictionary(pair => pair.Key, pair => pair.Value);
            sources.Remove(root.DefaultDataSourceName);
            if (root.DataSource is not null)
            {
                sources.Add(root.DefaultDataSourceName, root.DataSource);
            }

            RuntimeConfig normalized = new(string.Empty, root.DataSource!, root.Runtime!, root.Entities, root.DefaultDataSourceName, sources, new(),
                root.DataSourceFiles, root.AzureKeyVault, root.Autoentities)
            { TelemetryPresence = root.TelemetryPresence };
            normalized.ChildConfigs.AddRange(root.ChildConfigs);
            return normalized;
        }

        private static Dictionary<string, string> Properties(CliTelemetryConfigurationSnapshot snapshot)
            => JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.Json)!;

        private static Dictionary<string, string> AssertEngineEquivalent(RuntimeConfig config, bool saved = false, RuntimeConfig? expected = null)
        {
            ImmutableDictionary<string, string> engine = EngineTelemetrySnapshotFactory.Create(expected ?? config);
            CliTelemetryConfigurationSnapshot snapshot = CliTelemetryConfigurationSnapshot.Create(config, saved);
            Dictionary<string, string> properties = Properties(snapshot);
            Assert.AreEqual(saved ? "saved" : "loaded", properties["observation"]);
            Assert.AreEqual("cli-configuration-v1", properties["snapshot_schema"]);
            Assert.AreEqual(engine.Count + 1, properties.Count);
            foreach ((string key, string value) in engine)
            {
                Assert.AreEqual(key == "snapshot_schema" ? "cli-configuration-v1" : value, properties[key], key);
            }

            AssertWireShape(snapshot);
            return properties;
        }

        private static void AssertWireShape(CliTelemetryConfigurationSnapshot snapshot)
        {
            Assert.IsTrue(snapshot.Json.Length <= CliTelemetryConfigurationSnapshot.MAX_JSON_CHARACTERS);
            Assert.IsFalse(snapshot.Json.Contains(PRIVATE_VALUE, StringComparison.Ordinal));
            using JsonDocument document = JsonDocument.Parse(snapshot.Json);
            JsonProperty[] properties = document.RootElement.EnumerateObject().ToArray();
            Assert.AreEqual(83, properties.Length);
            Assert.IsTrue(properties.Length <= CliTelemetryConfigurationSnapshot.MAX_PROPERTY_COUNT);
            Assert.IsTrue(properties.All(property => property.Value.ValueKind == JsonValueKind.String));
            string[] keys = properties.Select(property => property.Name).ToArray();
            Assert.AreEqual(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            CollectionAssert.AreEqual(keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(), keys);
        }

        private static void AssertPair(Dictionary<string, string> properties, string key, string configured, string effective)
        {
            Assert.AreEqual(configured, properties[key + ".configured"], key + ".configured");
            Assert.AreEqual(effective, properties[key + ".effective"], key + ".effective");
        }

        private static void AssertUnknown(RuntimeConfig config)
            => Assert.AreSame(CliTelemetryConfigurationSnapshot.Unknown, CliTelemetryConfigurationSnapshot.Create(config));

        private sealed class NeverStringify
        {
            public override string ToString() => throw new InvalidOperationException("Arbitrary values must not be serialized.");
        }

        private sealed record HostileRuntimeConfig(RuntimeConfig Original) : RuntimeConfig(Original)
        {
            public override bool CanUseCache() => throw new InvalidOperationException();
            public override int GlobalCacheEntryTtl() => throw new InvalidOperationException();
            public override bool IsEntityCachingEnabled(string entityName) => throw new InvalidOperationException();
        }

        private sealed class SequenceDictionary<T>(IEnumerable<KeyValuePair<string, T>> entries) : IReadOnlyDictionary<string, T>
        {
            public int Count => throw new InvalidOperationException("Do not trust or inspect Count.");
            public IEnumerable<string> Keys => throw new InvalidOperationException();
            public IEnumerable<T> Values => throw new InvalidOperationException();
            public T this[string key] => throw new InvalidOperationException();
            public bool ContainsKey(string key) => throw new InvalidOperationException();
            public bool TryGetValue(string key, out T value) => throw new InvalidOperationException();
            public IEnumerator<KeyValuePair<string, T>> GetEnumerator() => entries.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class FaultingSequence<T>(T item, string failure) : IEnumerable<T>
        {
            public IEnumerator<T> GetEnumerator() => failure == "get_enumerator"
                ? throw new InvalidOperationException() : new Enumerator(item, failure);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(T item, string failure) : IEnumerator<T>
            {
                private bool _visited;
                public T Current => failure == "current" ? throw new InvalidOperationException() : item;
                object IEnumerator.Current => Current!;
                public bool MoveNext()
                {
                    if (failure == "move_next")
                    {
                        throw new InvalidOperationException();
                    }

                    bool next = !_visited;
                    _visited = true;
                    return next;
                }

                public void Reset() => throw new NotSupportedException();
                public void Dispose()
                {
                    if (failure == "dispose")
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
        }
    }
}
