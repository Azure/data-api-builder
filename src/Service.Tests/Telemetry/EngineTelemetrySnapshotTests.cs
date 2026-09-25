// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Synthetic, in-memory projection tests. Deserialization uses the product converters with
    /// variable replacement disabled. No host, database, exporter, environment, or file is used.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetrySnapshotTests
    {
        private const string SECRET = "SENSITIVE_SENTINEL_9a53b07";

        private static readonly string[] _pairedKeys =
        [
            "runtime.rest", "runtime.graphql", "runtime.mcp", "runtime.health", "runtime.cache",
            "runtime.cache.l2", "runtime.rest.strict_body", "runtime.graphql.multiple_create",
            "integrations.key_vault", "integrations.autoentities", "integrations.multiple_source_files",
            "runtime.embeddings", "runtime.embeddings.endpoint", "customer_telemetry.open_telemetry",
            "customer_telemetry.application_insights", "customer_telemetry.log_analytics", "customer_telemetry.file",
            "authentication.provider", "host.mode", "data_sources.obo", "data_sources.session_context",
            "entities.any.cache", "entities.any.rest", "entities.any.graphql", "entities.any.mcp_dml",
            "entities.any.mcp_custom_tool", "limits.default_page_size", "limits.max_page_size",
            "limits.max_response_bytes", "limits.cache_ttl_seconds", "limits.query_timeout_seconds",
            "limits.mcp_aggregate_query_timeout_seconds"
        ];

        private static readonly string[] _singleKeys =
        [
            "snapshot_schema", "runtime.embeddings.endpoint_present", "scale.data_source_count",
            "data_sources.types", "data_sources.distinct_type_count", "scale.entity_count",
            "entities.any.table", "entities.any.view", "entities.any.stored_procedure",
            "entities.any.persisted_document", "entities.any.parameter_embeddings", "entities.any.custom_roles",
            "entities.any.request_policy", "entities.any.database_policy", "entities.any.policies",
            "entities.any.descriptions", "entities.any.relationships", "limits.max_response_enforced"
        ];

        [TestMethod]
        public void OneArgumentApiReturnsImmutableProjectionAndRejectsNull()
        {
            Func<RuntimeConfig, ImmutableDictionary<string, string>> create = EngineTelemetrySnapshotFactory.Create;
            ImmutableDictionary<string, string> snapshot = create(Parse("{}"));
            Assert.AreEqual(StringComparer.Ordinal, snapshot.KeyComparer);
            Assert.ThrowsException<ArgumentNullException>(() => create(null!));
            AssertSchema(snapshot);
        }

        [TestMethod]
        public void OriginalEmptyInputDistinguishesOmittedSettingsFromEffectiveDefaults()
        {
            ImmutableDictionary<string, string> snapshot = Snapshot("{}");
            AssertPair(snapshot, "runtime.rest", "missing", "enabled");
            AssertPair(snapshot, "runtime.graphql", "missing", "enabled");
            AssertPair(snapshot, "runtime.mcp", "missing", "enabled");
            AssertPair(snapshot, "runtime.health", "missing", "enabled");
            AssertPair(snapshot, "runtime.rest.strict_body", "missing", "enabled");
            AssertPair(snapshot, "runtime.cache", "missing", "disabled");
            AssertPair(snapshot, "runtime.cache.l2", "missing", "disabled");
            AssertPair(snapshot, "runtime.graphql.multiple_create", "missing", "disabled");
            AssertPair(snapshot, "integrations.autoentities", "missing", "disabled");
            AssertPair(snapshot, "integrations.key_vault", "missing", "disabled");
            AssertPair(snapshot, "integrations.multiple_source_files", "missing", "disabled");
            AssertPair(snapshot, "runtime.embeddings", "missing", "disabled");
            AssertPair(snapshot, "runtime.embeddings.endpoint", "missing", "disabled");
            foreach (string key in _pairedKeys.Where(key => key.StartsWith("customer_telemetry.", StringComparison.Ordinal)))
            {
                AssertPair(snapshot, key, "missing", "disabled");
            }

            AssertPair(snapshot, "authentication.provider", "missing", "unauthenticated");
            AssertPair(snapshot, "host.mode", "missing", "production");
            AssertPair(snapshot, "limits.default_page_size", "missing", "11-100");
            AssertPair(snapshot, "limits.max_page_size", "missing", "10001-100000");
            AssertPair(snapshot, "limits.max_response_bytes", "missing", "67108865-268435456");
            AssertPair(snapshot, "limits.cache_ttl_seconds", "missing", "1-5");
            AssertPair(snapshot, "limits.query_timeout_seconds", "unsupported", "unsupported");
            AssertPair(snapshot, "limits.mcp_aggregate_query_timeout_seconds", "missing", "not_applicable");
            Assert.AreEqual("disabled", snapshot["limits.max_response_enforced"]);
            Assert.AreEqual("0", snapshot["scale.data_source_count"]);
            Assert.AreEqual("0", snapshot["data_sources.distinct_type_count"]);
            Assert.AreEqual("none", snapshot["data_sources.types"]);
            AssertSchema(snapshot);
        }

        [DataTestMethod]
        [DataRow("{}")]
        [DataRow("{\"entities\":{}}")]
        public void ZeroEntitiesAreNotDisabledFunctionalityAndUnsupportedCapabilitiesStayUnsupported(string json)
        {
            ImmutableDictionary<string, string> snapshot = Snapshot(json);
            Assert.AreEqual("0", snapshot["scale.entity_count"]);
            foreach ((string key, string value) in snapshot.Where(item => item.Key.StartsWith("entities.any.", StringComparison.Ordinal)))
            {
                Assert.AreEqual(key is "entities.any.persisted_document" or "entities.any.parameter_embeddings"
                    ? "unsupported" : "not_applicable", value, key);
            }
        }

        [TestMethod]
        public void ObjectModelOnlyDoesNotReconstructLostExplicitnessOrMistakeNullForKnownOmission()
        {
            RuntimeConfig config = Parse("""
                {
                  "runtime": {
                    "rest": {}, "graphql": {}, "mcp": {}, "health": {}, "cache": {},
                    "host": { "authentication": {} },
                    "telemetry": { "open-telemetry": {}, "application-insights": {}, "file": {}, "azure-log-analytics": {} }
                  },
                  "entities": { "Synthetic": { "source": "synthetic" } }
                }
                """);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            foreach (string key in new[]
            {
                "runtime.rest", "runtime.graphql", "runtime.mcp", "runtime.health", "runtime.cache",
                "runtime.rest.strict_body", "host.mode", "authentication.provider", "integrations.autoentities",
                "entities.any.rest", "entities.any.graphql", "entities.any.cache",
                "customer_telemetry.open_telemetry", "customer_telemetry.application_insights",
                "customer_telemetry.file", "customer_telemetry.log_analytics"
            })
            {
                Assert.AreEqual("unknown", snapshot[key + ".configured"], key);
            }

            Assert.AreEqual("enabled", snapshot["entities.any.rest.effective"]);
            Assert.AreEqual("enabled", snapshot["entities.any.graphql.effective"]);
            ImmutableDictionary<string, string> absentModel = EngineTelemetrySnapshotFactory.Create(Parse("{}"));
            Assert.AreEqual("unknown", absentModel["runtime.rest.configured"]);
            Assert.AreEqual("unknown", absentModel["integrations.autoentities.configured"]);
        }

        [DataTestMethod]
        [DataRow("true", "enabled")]
        [DataRow("false", "disabled")]
        [DataRow("{}", "missing")]
        [DataRow("{\"enabled\":false}", "disabled")]
        [DataRow("{\"enabled\":true}", "enabled")]
        public void ActualApiConvertersPreserveBooleanShorthandAndObjectPresence(string option, string configured)
        {
            string json = $$"""
                { "runtime": { "rest": {{option}}, "graphql": {{option}}, "mcp": {{option}} } }
                """;
            ImmutableDictionary<string, string> snapshot = Snapshot(json);
            foreach (string key in new[] { "runtime.rest", "runtime.graphql", "runtime.mcp" })
            {
                AssertPair(snapshot, key, configured, configured == "disabled" ? "disabled" : "enabled");
            }

            Assert.AreEqual("missing", snapshot["runtime.rest.strict_body.configured"]);
        }

        [TestMethod]
        public void ExplicitNullIsUnknownRatherThanOmissionOrFalse()
        {
            ImmutableDictionary<string, string> snapshot = Snapshot("""
                {
                  "runtime": {
                    "rest": null, "health": { "enabled": null }, "cache": { "enabled": null },
                    "host": null, "pagination": { "default-page-size": null },
                    "mcp": { "dml-tools": { "aggregate-records": { "query-timeout": null } } }
                  },
                  "autoentities": null,
                  "entities": { "Synthetic": { "source": "synthetic", "cache": { "enabled": null } } }
                }
                """);
            foreach (string key in new[]
            {
                "runtime.rest", "runtime.health", "runtime.cache", "host.mode", "limits.default_page_size",
                "integrations.autoentities", "entities.any.cache", "limits.mcp_aggregate_query_timeout_seconds"
            })
            {
                Assert.AreEqual("unknown", snapshot[key + ".configured"], key);
            }

            Assert.AreEqual("enabled", snapshot["runtime.rest.effective"]);
            Assert.AreEqual("disabled", snapshot["runtime.cache.effective"]);
        }

        [DataTestMethod]
        [DataRow("", "missing", "enabled")]
        [DataRow(",\"cache\":{}", "missing", "enabled")]
        [DataRow(",\"cache\":{\"enabled\":false}", "disabled", "disabled")]
        [DataRow(",\"cache\":{\"enabled\":true}", "enabled", "enabled")]
        public void EntityCacheUsesLazyInheritanceAndRetainsExplicitFalse(string entityCache, string configured, string effective)
        {
            string json = $$"""
                {
                  "data-source": { "database-type": "mssql" },
                  "runtime": { "cache": { "enabled": true } },
                  "entities": { "Synthetic": { "source": "synthetic" {{entityCache}} } }
                }
                """;
            RuntimeConfig config = Parse(json);
            ImmutableDictionary<string, string> snapshot = Snapshot(json);
            AssertPair(snapshot, "entities.any.cache", configured, effective);
            Assert.AreEqual(config.CanUseCache() && config.IsEntityCachingEnabled("Synthetic"), effective == "enabled");
            Assert.AreEqual("missing", snapshot["entities.any.rest.configured"]);
            Assert.AreEqual("missing", snapshot["entities.any.graphql.configured"]);
            Assert.AreEqual("enabled", snapshot["entities.any.rest.effective"]);
        }

        [DataTestMethod]
        [DataRow("false", "disabled", "disabled")]
        [DataRow("true", "enabled", "enabled")]
        [DataRow("{}", "missing", "enabled")]
        [DataRow("{\"enabled\":false}", "disabled", "disabled")]
        public void EntityApiFalseAndDefaultedObjectsRemainDistinct(string option, string configured, string effective)
        {
            string json = $$"""
                { "entities": { "Synthetic": { "source": "synthetic", "rest": {{option}}, "graphql": {{option}} } } }
                """;
            ImmutableDictionary<string, string> snapshot = Snapshot(json);
            AssertPair(snapshot, "entities.any.rest", configured, effective);
            AssertPair(snapshot, "entities.any.graphql", configured, effective);
            ImmutableDictionary<string, string> modelOnly = EngineTelemetrySnapshotFactory.Create(Parse(json));
            Assert.AreEqual("unknown", modelOnly["entities.any.rest.configured"]);
            Assert.AreEqual("unknown", modelOnly["entities.any.graphql.configured"]);
        }

        [TestMethod]
        public void SessionContextAndRuntimeCacheGateEntityOverridesAndLevelTwo()
        {
            RuntimeConfig config = NewConfig(EmptyRuntime() with
            {
                Cache = new RuntimeCacheOptions(true, 60) { Level2 = new RuntimeCacheLevel2Options(true) }
            }, new() { [SECRET] = NewEntity() with { Cache = new EntityCacheOptions(true) } },
                new DataSource(DatabaseType.MSSQL, SECRET, new() { ["set-session-context"] = true }));
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            AssertPair(snapshot, "runtime.cache", "enabled", "disabled");
            AssertPair(snapshot, "runtime.cache.l2", "enabled", "disabled");
            AssertPair(snapshot, "entities.any.cache", "enabled", "disabled");
            AssertPair(snapshot, "data_sources.session_context", "enabled", "enabled");
            Assert.AreEqual(config.CanUseCache(), snapshot["runtime.cache.effective"] == "enabled");

            RuntimeConfig cacheOff = NewConfig(EmptyRuntime() with
            {
                Cache = new RuntimeCacheOptions(false) { Level2 = new RuntimeCacheLevel2Options(true) }
            }, new() { [SECRET] = NewEntity() with { Cache = new EntityCacheOptions(true) } });
            ImmutableDictionary<string, string> off = EngineTelemetrySnapshotFactory.Create(cacheOff);
            AssertPair(off, "entities.any.cache", "enabled", "disabled");
            AssertPair(off, "runtime.cache.l2", "enabled", "disabled");
        }

        [DataTestMethod]
        [DataRow("true", "enabled")]
        [DataRow("false", "disabled")]
        [DataRow("\"true\"", "enabled")]
        [DataRow("\"false\"", "disabled")]
        public void SessionContextReadsActualTypedOptionsIncludingConverterStringBooleans(string value, string state)
        {
            string json = $$"""
                { "data-source": { "database-type": "mssql", "options": { "set-session-context": {{value}} } } }
                """;
            AssertPair(Snapshot(json), "data_sources.session_context", state, state);
            ImmutableDictionary<string, string> omitted = Snapshot("""{ "data-source": { "database-type": "mssql" } }""");
            AssertPair(omitted, "data_sources.session_context", "missing", "disabled");
        }

        [TestMethod]
        public void RuntimeAndProviderGatesDoNotChangeConfiguredIntent()
        {
            ImmutableDictionary<string, string> cosmos = Snapshot("""
                {
                  "data-source": { "database-type": "cosmosdb_nosql" },
                  "runtime": { "rest": true },
                  "entities": { "Synthetic": { "source": { "object": "synthetic" }, "rest": true } }
                }
                """);
            AssertPair(cosmos, "runtime.rest", "enabled", "disabled");
            AssertPair(cosmos, "entities.any.rest", "enabled", "disabled");
            Assert.AreEqual("not_applicable", cosmos["runtime.rest.strict_body.effective"]);
            Assert.AreEqual("unsupported", cosmos["entities.any.persisted_document"]);
            AssertPair(cosmos, "data_sources.obo", "unsupported", "unsupported");
            AssertPair(cosmos, "data_sources.session_context", "unsupported", "unsupported");

            ImmutableDictionary<string, string> strict = Snapshot("""
                { "runtime": { "rest": { "request-body-strict": false } } }
                """);
            AssertPair(strict, "runtime.rest.strict_body", "disabled", "disabled");
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, true, "enabled")]
        [DataRow(DatabaseType.MSSQL, false, "disabled")]
        [DataRow(DatabaseType.MySQL, true, "disabled")]
        [DataRow(DatabaseType.PostgreSQL, true, "disabled")]
        public void MultipleCreateUsesRuntimeSupportMethodAndGraphQlParent(DatabaseType databaseType, bool graphQlEnabled, string effective)
        {
            RuntimeConfig config = NewConfig(EmptyRuntime() with
            {
                GraphQL = new GraphQLRuntimeOptions(Enabled: graphQlEnabled, MultipleMutationOptions: new(new(true)))
            }, source: new DataSource(databaseType, SECRET));
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            AssertPair(snapshot, "runtime.graphql.multiple_create", "enabled", effective);
            Assert.AreEqual(config.IsGraphQLEnabled && config.IsMultipleCreateOperationEnabled(), effective == "enabled");
        }

        [TestMethod]
        public void ExplicitMultipleCreateFalseAndCollapsedEmptyObjectsRemainDistinct()
        {
            ImmutableDictionary<string, string> empty = Snapshot("""
                { "runtime": { "graphql": { "multiple-mutations": { "create": {} } } } }
                """);
            AssertPair(empty, "runtime.graphql.multiple_create", "missing", "disabled");
            ImmutableDictionary<string, string> disabled = Snapshot("""
                { "runtime": { "graphql": { "multiple-mutations": { "create": { "enabled": false } } } } }
                """);
            AssertPair(disabled, "runtime.graphql.multiple_create", "disabled", "disabled");
        }

        [TestMethod]
        public void EmbeddingsDefaultsEndpointPresenceAndParentGateAreIndependent()
        {
            string json = """
                {
                  "runtime": {
                    "embeddings": { "provider": "openai", "base-url": "SENSITIVE_SENTINEL_9a53b07", "api-key": "SENSITIVE_SENTINEL_9a53b07", "endpoint": {} }
                  }
                }
                """;
            ImmutableDictionary<string, string> defaults = Snapshot(json);
            AssertPair(defaults, "runtime.embeddings", "missing", "enabled");
            AssertPair(defaults, "runtime.embeddings.endpoint", "missing", "disabled");
            Assert.AreEqual("enabled", defaults["runtime.embeddings.endpoint_present"]);
            RuntimeConfig config = Parse(json);
            EmbeddingsOptions embeddings = config.Runtime!.Embeddings!;
            config = config with
            {
                Runtime = config.Runtime with { Embeddings = embeddings with { Enabled = false, UserProvidedEnabled = true, Endpoint = new(true) } }
            };
            ImmutableDictionary<string, string> disabledParent = EngineTelemetrySnapshotFactory.Create(config);
            AssertPair(disabledParent, "runtime.embeddings", "disabled", "disabled");
            AssertPair(disabledParent, "runtime.embeddings.endpoint", "enabled", "disabled");
            Assert.AreEqual("disabled", Snapshot("{}")["runtime.embeddings.endpoint_present"]);
        }

        [TestMethod]
        public void EmbeddingConverterCaseInsensitiveFieldsAreAlsoRecognizedForProvenance()
        {
            ImmutableDictionary<string, string> snapshot = Snapshot("""
                {
                  "runtime": {
                    "embeddings": {
                      "provider": "openai", "base-url": "synthetic", "api-key": "synthetic",
                      "ENABLED": false, "ENDPOINT": { "ENABLED": true }
                    }
                  }
                }
                """);
            AssertPair(snapshot, "runtime.embeddings", "disabled", "disabled");
            AssertPair(snapshot, "runtime.embeddings.endpoint", "enabled", "disabled");
        }

        [TestMethod]
        public void CustomerSinkConfigurationDoesNotImplyRegistrationWhenLocalRequirementsAreMissing()
        {
            ImmutableDictionary<string, string> snapshot = Snapshot("""
                {
                  "runtime": {
                    "telemetry": {
                      "application-insights": { "enabled": true },
                      "open-telemetry": { "enabled": true, "endpoint": "not-an-absolute-uri" },
                      "azure-log-analytics": { "enabled": true },
                      "file": { "enabled": true, "path": " " }
                    }
                  }
                }
                """);
            AssertPair(snapshot, "customer_telemetry.application_insights", "enabled", "enabled");
            AssertPair(snapshot, "customer_telemetry.open_telemetry", "enabled", "disabled");
            AssertPair(snapshot, "customer_telemetry.log_analytics", "enabled", "disabled");
            AssertPair(snapshot, "customer_telemetry.file", "enabled", "disabled");
            ImmutableDictionary<string, string> disabled = Snapshot("""
                {
                  "runtime": {
                    "telemetry": {
                      "application-insights": { "enabled": false }, "open-telemetry": { "enabled": false },
                      "azure-log-analytics": { "enabled": false }, "file": { "enabled": false }
                    }
                  }
                }
                """);
            foreach (string key in _pairedKeys.Where(key => key.StartsWith("customer_telemetry.", StringComparison.Ordinal)))
            {
                AssertPair(disabled, key, "disabled", "disabled");
            }
        }

        [DataTestMethod]
        [DataRow("Unauthenticated", "unauthenticated")]
        [DataRow("Simulator", "simulator")]
        [DataRow("AppService", "app_service")]
        [DataRow("StaticWebApps", "static_web_apps")]
        [DataRow("AzureAD", "entra_id")]
        [DataRow("eNtRaId", "entra_id")]
        [DataRow(SECRET, "jwt")]
        public void AuthenticationAndHostModeUseClosedCategories(string provider, string expected)
        {
            string json = $$"""
                { "runtime": { "host": { "mode": "development", "authentication": { "provider": "{{provider}}" } } } }
                """;
            ImmutableDictionary<string, string> snapshot = Snapshot(json);
            AssertPair(snapshot, "authentication.provider", expected, expected);
            AssertPair(snapshot, "host.mode", "development", "development");
            AssertSchema(snapshot);
        }

        [TestMethod]
        public void SourceTypesAuthorizationAndMetadataAreAggregatedWithoutTheirContent()
        {
            Entity withMetadata = NewEntity() with
            {
                Description = SECRET,
                Permissions =
                [
                    new("Anonymous", [new(EntityActionOperation.Read, null, null)]),
                    new("AUTHENTICATED", [new(EntityActionOperation.Read, null, null)]),
                    new(SECRET, [new(EntityActionOperation.Read, null, new(SECRET, SECRET))])
                ],
                Relationships = new() { [SECRET] = new(Cardinality.One, SECRET, [SECRET], [SECRET], SECRET, [SECRET], [SECRET]) }
            };
            RuntimeConfig config = NewConfig(entities: new()
            {
                ["Table"] = withMetadata,
                ["View"] = NewEntity(EntitySourceType.View),
                ["Procedure"] = NewEntity(EntitySourceType.StoredProcedure)
            });
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            foreach (string key in new[]
            {
                "entities.any.table", "entities.any.view", "entities.any.stored_procedure", "entities.any.custom_roles",
                "entities.any.request_policy", "entities.any.database_policy", "entities.any.policies",
                "entities.any.descriptions", "entities.any.relationships"
            })
            {
                Assert.AreEqual("enabled", snapshot[key], key);
            }

            RuntimeConfig reservedRolesOnly = NewConfig(entities: new()
            {
                ["Synthetic"] = NewEntity() with { Permissions = [new("Anonymous", []), new("AUTHENTICATED", [])] }
            });
            ImmutableDictionary<string, string> noFeatures = EngineTelemetrySnapshotFactory.Create(reservedRolesOnly);
            foreach (string key in new[] { "custom_roles", "request_policy", "database_policy", "policies", "descriptions", "relationships" })
            {
                Assert.AreEqual("disabled", noFeatures["entities.any." + key], key);
            }

            AssertSchema(snapshot);
        }

        [TestMethod]
        public void McpDmlHonorsToolAndEntityGatesAndCustomToolsRequireProcedures()
        {
            ImmutableDictionary<string, string> snapshot = Snapshot("""
                {
                  "runtime": { "mcp": { "dml-tools": false } },
                  "entities": {
                    "Synthetic": { "source": "synthetic", "mcp": true },
                    "Procedure": { "source": { "object": "synthetic", "type": "stored-procedure" }, "mcp": { "custom-tool": true } }
                  }
                }
                """);
            AssertPair(snapshot, "entities.any.mcp_dml", "enabled", "disabled");
            AssertPair(snapshot, "entities.any.mcp_custom_tool", "enabled", "enabled");

            ImmutableDictionary<string, string> defaults = Snapshot("""
                { "runtime": { "mcp": {} }, "entities": { "Synthetic": { "source": "synthetic" } } }
                """);
            AssertPair(defaults, "entities.any.mcp_dml", "missing", "enabled");
            AssertPair(defaults, "entities.any.mcp_custom_tool", "not_applicable", "not_applicable");

            ImmutableDictionary<string, string> noMcpOptions = Snapshot("""
                { "entities": { "Synthetic": { "source": "synthetic" } } }
                """);
            AssertPair(noMcpOptions, "runtime.mcp", "missing", "enabled");
            AssertPair(noMcpOptions, "entities.any.mcp_dml", "missing", "unknown");

            ImmutableDictionary<string, string> entityOff = Snapshot("""
                { "runtime": { "mcp": {} }, "entities": { "Synthetic": { "source": "synthetic", "mcp": false } } }
                """);
            AssertPair(entityOff, "entities.any.mcp_dml", "disabled", "disabled");
        }

        [TestMethod]
        public void EntityShorthandNamesArePresenceOnlyAndParentDisablesExposure()
        {
            ImmutableDictionary<string, string> snapshot = Snapshot("""
                {
                  "runtime": { "rest": false, "graphql": false, "mcp": false },
                  "entities": {
                    "Synthetic": { "source": "synthetic", "rest": "SENSITIVE_SENTINEL_9a53b07", "graphql": "SENSITIVE_SENTINEL_9a53b07", "mcp": true }
                  }
                }
                """);
            AssertPair(snapshot, "entities.any.rest", "enabled", "disabled");
            AssertPair(snapshot, "entities.any.graphql", "enabled", "disabled");
            AssertPair(snapshot, "entities.any.mcp_dml", "enabled", "disabled");
            AssertSchema(snapshot);
        }

        [TestMethod]
        public void SourceSetIsDistinctOrderedAndFiniteEvenWithUnknownEnumValues()
        {
            Dictionary<string, DataSource> sources = new()
            {
                ["z"] = new(DatabaseType.CosmosDB_PostgreSQL, SECRET),
                ["a"] = new(DatabaseType.MySQL, SECRET),
                ["b"] = new(DatabaseType.MSSQL, SECRET),
                ["c"] = new(DatabaseType.DWSQL, SECRET),
                ["d"] = new(DatabaseType.CosmosDB_NoSQL, SECRET),
                ["e"] = new(DatabaseType.PostgreSQL, SECRET),
                ["f"] = new(DatabaseType.MSSQL, SECRET)
            };
            RuntimeConfig config = MultipleSources(sources);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            Assert.AreEqual("mssql,dwsql,postgresql,mysql,cosmosdb_nosql,cosmosdb_postgresql", snapshot["data_sources.types"]);
            Assert.AreEqual("6", snapshot["data_sources.distinct_type_count"]);
            Assert.AreEqual("2-10", snapshot["scale.data_source_count"]);

            sources.Add(SECRET, new DataSource((DatabaseType)12345, SECRET));
            sources.Add(SECRET + "2", new DataSource((DatabaseType)54321, SECRET));
            ImmutableDictionary<string, string> unknown = EngineTelemetrySnapshotFactory.Create(config);
            Assert.AreEqual(snapshot["data_sources.types"] + ",unknown", unknown["data_sources.types"]);
            Assert.AreEqual("unknown", unknown["data_sources.distinct_type_count"]);
            AssertSchema(unknown);
        }

        [TestMethod]
        public void OboIsAnyAcrossSourcesAndRootJsonDoesNotInventChildProvenance()
        {
            Dictionary<string, DataSource> sources = new()
            {
                ["root"] = new(DatabaseType.MSSQL, SECRET),
                ["child"] = new DataSource(DatabaseType.MSSQL, SECRET) { UserDelegatedAuth = new(true, SECRET, SECRET) }
            };
            RuntimeConfig config = MultipleSources(sources, new() { [SECRET] = NewEntity() });
            using JsonDocument rootInput = JsonDocument.Parse("""{ "data-source": { "database-type": "mssql" }, "entities": {} }""");
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config, rootInput.RootElement);
            AssertPair(snapshot, "data_sources.obo", "unknown", "enabled");
            Assert.AreEqual("unknown", snapshot["entities.any.rest.configured"]);
            Assert.AreEqual("unknown", snapshot["entities.any.graphql.configured"]);

            ImmutableDictionary<string, string> explicitRoot = Snapshot("""
                { "data-source": { "database-type": "mssql", "user-delegated-auth": { "enabled": false } } }
                """);
            AssertPair(explicitRoot, "data_sources.obo", "disabled", "disabled");
            ImmutableDictionary<string, string> omittedRoot = Snapshot("""
                { "data-source": { "database-type": "mssql", "user-delegated-auth": {} } }
                """);
            AssertPair(omittedRoot, "data_sources.obo", "missing", "disabled");
        }

        [TestMethod]
        public void EmptyAndNonemptyIntegrationCollectionsDoNotReadFilesOrSecrets()
        {
            // Even an empty data-source-files array enters the loading constructor. Build that
            // already-loaded shape in memory instead, including original presence information.
            RuntimeConfig emptyConfig = Parse("""{ "autoentities": {}, "azure-key-vault": {} }""") with
            {
                DataSourceFiles = new([])
            };
            using JsonDocument emptyInput = JsonDocument.Parse("""
                { "autoentities": {}, "data-source-files": [], "azure-key-vault": {} }
                """);
            ImmutableDictionary<string, string> empty = EngineTelemetrySnapshotFactory.Create(emptyConfig, emptyInput.RootElement);
            AssertPair(empty, "integrations.autoentities", "disabled", "disabled");
            AssertPair(empty, "integrations.multiple_source_files", "disabled", "disabled");
            AssertPair(empty, "integrations.key_vault", "disabled", "disabled");

            // Assign after construction: RuntimeConfig's file-loading constructor is deliberately
            // not called with nonempty data-source-files in these synthetic tests.
            RuntimeConfig config = NewConfig() with
            {
                DataSourceFiles = new([SECRET]),
                Autoentities = new(new Dictionary<string, Autoentity> { [SECRET] = new(null, null, []) }),
                AzureKeyVault = new(SECRET)
            };
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            AssertPair(snapshot, "integrations.autoentities", "enabled", "enabled");
            AssertPair(snapshot, "integrations.multiple_source_files", "enabled", "enabled");
            AssertPair(snapshot, "integrations.key_vault", "enabled", "unknown");
            AssertSchema(snapshot);
        }

        [TestMethod]
        public void KeyVaultPresenceIncludesLoadedChildrenAndDoesNotLoopOnRepeatedReferences()
        {
            RuntimeConfig root = NewConfig();
            RuntimeConfig child = NewConfig() with { AzureKeyVault = new(SECRET) };
            root.ChildConfigs.Add((SECRET, child));
            root.ChildConfigs.Add((SECRET, child));
            child.ChildConfigs.Add((SECRET, root));
            AssertPair(EngineTelemetrySnapshotFactory.Create(root), "integrations.key_vault", "enabled", "unknown");
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
        public void EntityCountHasAnExplicitZeroAndInclusiveFiniteBuckets(int count, string expected)
        {
            Dictionary<string, Entity> entities = Enumerable.Range(0, count).ToDictionary(index => "Synthetic" + index, _ => NewEntity());
            Assert.AreEqual(expected, EngineTelemetrySnapshotFactory.Create(NewConfig(entities: entities))["scale.entity_count"]);
        }

        [DataTestMethod]
        [DataRow(1, "1-10")]
        [DataRow(10, "1-10")]
        [DataRow(11, "11-100")]
        [DataRow(100, "11-100")]
        [DataRow(101, "101-1000")]
        [DataRow(1000, "101-1000")]
        [DataRow(1001, "1001-10000")]
        [DataRow(10000, "1001-10000")]
        [DataRow(10001, "10001-100000")]
        [DataRow(100000, "10001-100000")]
        [DataRow(100001, "100001+")]
        [DataRow(-1, "100001+")]
        public void PageBucketsUseResolvedPaginationValuesIncludingMinusOne(int value, string expected)
        {
            RuntimeConfig config = NewConfig(EmptyRuntime() with { Pagination = new PaginationOptions(value, value) });
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            AssertPair(snapshot, "limits.default_page_size", expected, expected);
            AssertPair(snapshot, "limits.max_page_size", expected, expected);
        }

        [DataTestMethod]
        [DataRow(1, "1-1048576")]
        [DataRow(2, "1048577-16777216")]
        [DataRow(16, "1048577-16777216")]
        [DataRow(17, "16777217-67108864")]
        [DataRow(64, "16777217-67108864")]
        [DataRow(65, "67108865-268435456")]
        [DataRow(-1, "67108865-268435456")]
        public void ResponseLimitBucketsConvertMebibytesWithoutEmittingExactLimits(int megabytes, string expected)
        {
            RuntimeConfig config = NewConfig(EmptyRuntime() with { Host = new(null, null, MaxResponseSizeMB: megabytes) });
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            AssertPair(snapshot, "limits.max_response_bytes", expected, expected);
            Assert.AreEqual("enabled", snapshot["limits.max_response_enforced"]);
        }

        [DataTestMethod]
        [DataRow(1, "1-5")]
        [DataRow(5, "1-5")]
        [DataRow(6, "6-30")]
        [DataRow(30, "6-30")]
        [DataRow(31, "31-60")]
        [DataRow(60, "31-60")]
        [DataRow(61, "61-300")]
        [DataRow(300, "61-300")]
        [DataRow(301, "301-3600")]
        [DataRow(3600, "301-3600")]
        [DataRow(3601, "3601+")]
        public void CacheTtlUsesConfiguredAndRuntimeResolvedBuckets(int seconds, string expected)
        {
            RuntimeOptions runtime = EmptyRuntime() with { Cache = new(true, seconds) };
            AssertPair(EngineTelemetrySnapshotFactory.Create(NewConfig(runtime)), "limits.cache_ttl_seconds", expected, expected);
            RuntimeConfig disabled = NewConfig(runtime with { Cache = new(false, seconds) });
            AssertPair(EngineTelemetrySnapshotFactory.Create(disabled), "limits.cache_ttl_seconds", expected, "1-5");
        }

        [DataTestMethod]
        [DataRow(1, "1-5", "1-5")]
        [DataRow(30, "6-30", "6-30")]
        [DataRow(60, "31-60", "31-60")]
        [DataRow(300, "61-300", "61-300")]
        [DataRow(600, "301-3600", "301-3600")]
        [DataRow(0, "unknown", "6-30")]
        [DataRow(601, "unknown", "6-30")]
        public void QueryTimeoutIsMcpAggregateScopedAndUsesActualDefensiveFallback(int seconds, string configured, string effective)
        {
            RuntimeOptions runtime = EmptyRuntime() with { Mcp = new(DmlTools: new(aggregateRecordsQueryTimeout: seconds)) };
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(NewConfig(runtime));
            AssertPair(snapshot, "limits.mcp_aggregate_query_timeout_seconds", configured, effective);
            AssertPair(snapshot, "limits.query_timeout_seconds", "unsupported", "unsupported");
            RuntimeConfig disabled = NewConfig(runtime with { Mcp = new(Enabled: false, DmlTools: runtime.Mcp!.DmlTools) });
            AssertPair(EngineTelemetrySnapshotFactory.Create(disabled), "limits.mcp_aggregate_query_timeout_seconds", configured, "not_applicable");
        }

        [TestMethod]
        public void UnknownSourceOrHostEnumsNeverBecomeKnownDefaultsOrRawNumbers()
        {
            RuntimeConfig config = NewConfig(EmptyRuntime() with { Host = new(null, null, (HostMode)98765) },
                new() { [SECRET] = NewEntity((EntitySourceType)12345) }, new DataSource((DatabaseType)54321, SECRET));
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            foreach (string key in new[] { "entities.any.table", "entities.any.view", "entities.any.stored_procedure", "host.mode.effective" })
            {
                Assert.AreEqual("unknown", snapshot[key], key);
            }

            Assert.AreEqual("unknown", snapshot["data_sources.types"]);
            AssertSchema(snapshot);
        }

        [TestMethod]
        public void AnyPositiveEvidenceWinsButUnknownIsNotErasedByKnownNegatives()
        {
            RuntimeConfig unknown = NewConfig(entities: new()
            {
                ["Unknown"] = NewEntity((EntitySourceType)999),
                ["View"] = NewEntity(EntitySourceType.View)
            });
            Assert.AreEqual("unknown", EngineTelemetrySnapshotFactory.Create(unknown)["entities.any.table"]);
            RuntimeConfig positive = NewConfig(entities: new()
            {
                ["Unknown"] = NewEntity((EntitySourceType)999),
                ["Table"] = NewEntity()
            });
            Assert.AreEqual("enabled", EngineTelemetrySnapshotFactory.Create(positive)["entities.any.table"]);
        }

        [TestMethod]
        public void SnapshotSurvivesDocumentDisposalAndLaterMutationOfConfigGraph()
        {
            const string json = """
                {
                  "data-source": { "database-type": "mssql", "options": { "set-session-context": false } },
                  "runtime": {
                    "host": { "mode": "development" }, "cache": { "enabled": true },
                    "graphql": { "multiple-mutations": { "create": { "enabled": true } } }
                  },
                  "entities": { "Synthetic": { "source": "synthetic", "permissions": [ { "role": "SENSITIVE_SENTINEL_9a53b07", "actions": ["read"] } ] } }
                }
                """;
            RuntimeConfig config = Parse(json);
            ImmutableDictionary<string, string> snapshot;
            using (JsonDocument input = JsonDocument.Parse(json))
            {
                snapshot = EngineTelemetrySnapshotFactory.Create(config, input.RootElement);
            }

            string before = JsonSerializer.Serialize(snapshot);
            config.Runtime!.Host!.Mode = HostMode.Production;
            config.Runtime.GraphQL!.MultipleMutationOptions!.MultipleCreateOptions!.Enabled = false;
            config.DataSource!.Options!["set-session-context"] = true;
            config.Entities["Synthetic"].Permissions[0] = new EntityPermission("anonymous", []);
            Assert.AreEqual(before, JsonSerializer.Serialize(snapshot));
            Assert.AreEqual("development", snapshot["host.mode.effective"]);
            Assert.AreEqual("enabled", snapshot["entities.any.custom_roles"]);
            ImmutableDictionary<string, string> next = EngineTelemetrySnapshotFactory.Create(config);
            Assert.AreEqual("production", next["host.mode.effective"]);
            Assert.AreEqual("disabled", next["entities.any.custom_roles"]);
            Assert.AreEqual("disabled", next["runtime.cache.effective"]);
        }

        [TestMethod]
        public void SecretsNamesPathsUrlsPoliciesParametersAndDescriptionsNeverEnterSnapshot()
        {
            RuntimeOptions runtime = EmptyRuntime() with
            {
                Rest = new(Path: SECRET),
                GraphQL = new(Path: SECRET),
                Mcp = new(Path: SECRET, Description: SECRET, AllowedHosts: [SECRET]),
                Host = new(null, new(SECRET), HostMode.Development),
                BaseRoute = SECRET,
                Cache = new RuntimeCacheOptions(true, 72) { Level2 = new(true, SECRET, SECRET, SECRET) },
                Embeddings = new(EmbeddingProviderType.OpenAI, SECRET, SECRET, Model: SECRET, Endpoint: new(true, [SECRET], SECRET)),
                Telemetry = new(
                    ApplicationInsights: new(true, SECRET),
                    OpenTelemetry: new(true, "https://" + SECRET + ".invalid", SECRET, ServiceName: SECRET),
                    AzureLogAnalytics: new(true, new(SECRET, SECRET, SECRET), SECRET),
                    File: new(true, SECRET))
            };
            Entity entity = NewEntity(EntitySourceType.StoredProcedure) with
            {
                Source = new(SECRET, EntitySourceType.StoredProcedure, [new ParameterMetadata { Name = SECRET, Description = SECRET, Default = SECRET }], [SECRET]),
                Rest = new(Path: SECRET),
                GraphQL = new(SECRET, SECRET),
                Description = SECRET,
                Permissions = [new(SECRET, [new(EntityActionOperation.Execute, null, new(SECRET, SECRET))])],
                Mappings = new() { [SECRET] = SECRET },
                Relationships = new() { [SECRET] = new(Cardinality.One, SECRET, [SECRET], [SECRET], SECRET, [SECRET], [SECRET]) },
                Mcp = new(true, true)
            };
            RuntimeConfig config = NewConfig(runtime, new() { [SECRET] = entity }, new DataSource(DatabaseType.MSSQL, SECRET, new() { [SECRET] = SECRET })
            {
                UserDelegatedAuth = new(true, SECRET, SECRET)
            }) with
            { Schema = SECRET, AzureKeyVault = new(SECRET), DataSourceFiles = new([SECRET]) };
            using JsonDocument input = JsonDocument.Parse("""
                { "$schema": "SENSITIVE_SENTINEL_9a53b07", "unrecognized-secret": "SENSITIVE_SENTINEL_9a53b07" }
                """);
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            AssertSchema(snapshot);
            AssertSchema(EngineTelemetrySnapshotFactory.Create(config, input.RootElement));
            AssertPair(snapshot, "customer_telemetry.log_analytics", "enabled", "enabled");
            AssertPair(snapshot, "customer_telemetry.file", "enabled", "enabled");
            Assert.IsFalse(JsonSerializer.Serialize(snapshot).Contains(SECRET, StringComparison.Ordinal));
        }

        private static RuntimeConfig Parse(string json) => JsonSerializer.Deserialize<RuntimeConfig>(json, RuntimeConfigLoader.GetSerializationOptions())!;

        private static ImmutableDictionary<string, string> Snapshot(string json)
        {
            using JsonDocument input = JsonDocument.Parse(json);
            return EngineTelemetrySnapshotFactory.Create(Parse(json), input.RootElement);
        }

        private static RuntimeOptions EmptyRuntime() => new(Rest: null, GraphQL: null, Mcp: null, Host: null);

        private static RuntimeConfig NewConfig(RuntimeOptions? runtime = null, Dictionary<string, Entity>? entities = null, DataSource? source = null)
            => new(Schema: null, DataSource: source ?? new(DatabaseType.MSSQL, SECRET), Entities: new(entities ?? new()), Runtime: runtime);

        private static Entity NewEntity(EntitySourceType? type = EntitySourceType.Table)
            => new(new EntitySource(SECRET, type, null, null), GraphQL: null!, Fields: null, Rest: null!, Permissions: [], Mappings: null, Relationships: null);

        private static RuntimeConfig MultipleSources(Dictionary<string, DataSource> sources, Dictionary<string, Entity>? entities = null)
        {
            string first = sources.Keys.First();
            Dictionary<string, string> mappings = (entities ?? new()).Keys.ToDictionary(name => name, _ => first);
            return new RuntimeConfig(SECRET, sources[first], EmptyRuntime(), new(entities ?? new()), first, sources, mappings);
        }

        private static void AssertPair(ImmutableDictionary<string, string> snapshot, string key, string configured, string effective)
        {
            Assert.AreEqual(configured, snapshot[key + ".configured"], key + ".configured");
            Assert.AreEqual(effective, snapshot[key + ".effective"], key + ".effective");
        }

        private static void AssertSchema(ImmutableDictionary<string, string> snapshot)
        {
            string[] expectedKeys = _pairedKeys.SelectMany(key => new[] { key + ".configured", key + ".effective" }).Concat(_singleKeys).ToArray();
            CollectionAssert.AreEquivalent(expectedKeys, snapshot.Keys.ToArray());
            HashSet<string> values = new(StringComparer.Ordinal)
            {
                "enabled", "disabled", "missing", "unknown", "unsupported", "not_applicable", "configuration-v1",
                "production", "development", "unauthenticated", "simulator", "app_service", "static_web_apps", "entra_id", "jwt",
                "0", "1", "2", "3", "4", "5", "6", "2-10", "11-50", "51-100", "101-500", "501+",
                "1-10", "11-100", "101-1000", "1001-10000", "10001-100000", "100001+",
                "1-1048576", "1048577-16777216", "16777217-67108864", "67108865-268435456", "268435457+",
                "1-5", "6-30", "31-60", "61-300", "301-3600", "3601+"
            };
            foreach ((string key, string value) in snapshot)
            {
                Assert.IsFalse(key.Contains(SECRET, StringComparison.Ordinal));
                Assert.IsFalse(value.Contains(SECRET, StringComparison.Ordinal));
                Assert.IsTrue(value.Length <= 128, key);
                if (key != "data_sources.types")
                {
                    Assert.IsTrue(values.Contains(value), key);
                }
            }

            string types = snapshot["data_sources.types"];
            if (types != "none")
            {
                string[] ordered = ["mssql", "dwsql", "postgresql", "mysql", "cosmosdb_nosql", "cosmosdb_postgresql", "unknown"];
                string[] actual = types.Split(',');
                CollectionAssert.AreEqual(ordered.Where(type => actual.Contains(type, StringComparer.Ordinal)).ToArray(), actual);
            }
        }
    }
}
