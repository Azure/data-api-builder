// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// One immutable configuration_context command property, not serialized configuration.
    /// Projects only an already loaded model; never loads, validates, resolves variables, or
    /// contacts a database. The caller supplies authorized load/successful-save provenance.
    /// </summary>
    internal sealed class CliTelemetryConfigurationSnapshot
    {
        // Shared across the entire preparation, including copies of merged maps in ancestors.
        // A replaced default source consumes one node, not both the old and new source.
        internal const int MAX_CONFIGURATION_SOURCE_NODES = 256;
        internal const int MAX_CHILD_REFERENCES = 256;
        internal const int MAX_DEPTH = 32;

        // Entities, autoentities, permissions, actions and relationships share this budget.
        // Arrays are checked before allocation; custom dictionaries are enumerated with a cap,
        // never trusted via Count. Unused fields/parameters/policy text are not traversed.
        // Caps bound callback counts, not execution time inside a caller's enumerator callback.
        internal const int MAX_MODEL_NODES = 4096;
        internal const int MAX_PROPERTY_COUNT = 128;
        internal const int MAX_JSON_CHARACTERS = 8192;

        // This fixed fallback also pins the engine projection's key set. Schema drift must be
        // reviewed explicitly. It requires neither the engine factory nor a serializer to work
        // on the failure path, and contains no inferred defaults, zero counts or disabled gates.
        private const string UNKNOWN_JSON = """
            {
            "authentication.provider.configured":"unknown",
            "authentication.provider.effective":"unknown",
            "customer_telemetry.application_insights.configured":"unknown",
            "customer_telemetry.application_insights.effective":"unknown",
            "customer_telemetry.file.configured":"unknown",
            "customer_telemetry.file.effective":"unknown",
            "customer_telemetry.log_analytics.configured":"unknown",
            "customer_telemetry.log_analytics.effective":"unknown",
            "customer_telemetry.open_telemetry.configured":"unknown",
            "customer_telemetry.open_telemetry.effective":"unknown",
            "data_sources.distinct_type_count":"unknown",
            "data_sources.obo.configured":"unknown",
            "data_sources.obo.effective":"unknown",
            "data_sources.session_context.configured":"unknown",
            "data_sources.session_context.effective":"unknown",
            "data_sources.types":"unknown",
            "entities.any.cache.configured":"unknown",
            "entities.any.cache.effective":"unknown",
            "entities.any.custom_roles":"unknown",
            "entities.any.database_policy":"unknown",
            "entities.any.descriptions":"unknown",
            "entities.any.graphql.configured":"unknown",
            "entities.any.graphql.effective":"unknown",
            "entities.any.mcp_custom_tool.configured":"unknown",
            "entities.any.mcp_custom_tool.effective":"unknown",
            "entities.any.mcp_dml.configured":"unknown",
            "entities.any.mcp_dml.effective":"unknown",
            "entities.any.parameter_embeddings":"unknown",
            "entities.any.persisted_document":"unknown",
            "entities.any.policies":"unknown",
            "entities.any.relationships":"unknown",
            "entities.any.request_policy":"unknown",
            "entities.any.rest.configured":"unknown",
            "entities.any.rest.effective":"unknown",
            "entities.any.stored_procedure":"unknown",
            "entities.any.table":"unknown",
            "entities.any.view":"unknown",
            "host.mode.configured":"unknown",
            "host.mode.effective":"unknown",
            "integrations.autoentities.configured":"unknown",
            "integrations.autoentities.effective":"unknown",
            "integrations.key_vault.configured":"unknown",
            "integrations.key_vault.effective":"unknown",
            "integrations.multiple_source_files.configured":"unknown",
            "integrations.multiple_source_files.effective":"unknown",
            "limits.cache_ttl_seconds.configured":"unknown",
            "limits.cache_ttl_seconds.effective":"unknown",
            "limits.default_page_size.configured":"unknown",
            "limits.default_page_size.effective":"unknown",
            "limits.max_page_size.configured":"unknown",
            "limits.max_page_size.effective":"unknown",
            "limits.max_response_bytes.configured":"unknown",
            "limits.max_response_bytes.effective":"unknown",
            "limits.max_response_enforced":"unknown",
            "limits.mcp_aggregate_query_timeout_seconds.configured":"unknown",
            "limits.mcp_aggregate_query_timeout_seconds.effective":"unknown",
            "limits.query_timeout_seconds.configured":"unknown",
            "limits.query_timeout_seconds.effective":"unknown",
            "observation":"unknown",
            "runtime.cache.configured":"unknown",
            "runtime.cache.effective":"unknown",
            "runtime.cache.l2.configured":"unknown",
            "runtime.cache.l2.effective":"unknown",
            "runtime.embeddings.configured":"unknown",
            "runtime.embeddings.effective":"unknown",
            "runtime.embeddings.endpoint.configured":"unknown",
            "runtime.embeddings.endpoint.effective":"unknown",
            "runtime.embeddings.endpoint_present":"unknown",
            "runtime.graphql.configured":"unknown",
            "runtime.graphql.effective":"unknown",
            "runtime.graphql.multiple_create.configured":"unknown",
            "runtime.graphql.multiple_create.effective":"unknown",
            "runtime.health.configured":"unknown",
            "runtime.health.effective":"unknown",
            "runtime.mcp.configured":"unknown",
            "runtime.mcp.effective":"unknown",
            "runtime.rest.configured":"unknown",
            "runtime.rest.effective":"unknown",
            "runtime.rest.strict_body.configured":"unknown",
            "runtime.rest.strict_body.effective":"unknown",
            "scale.data_source_count":"unknown",
            "scale.entity_count":"unknown",
            "snapshot_schema":"cli-configuration-v1"
            }
            """;

        private static class UnknownHolder
        {
            // Explicit type initializer prevents beforefieldinit/eager fallback allocation.
            static UnknownHolder() { }
            internal static readonly CliTelemetryConfigurationSnapshot _value = new(UNKNOWN_JSON);
        }

        internal static CliTelemetryConfigurationSnapshot Unknown => UnknownHolder._value;
        internal string Json { get; }

        private CliTelemetryConfigurationSnapshot(string json) => Json = json;

        internal static CliTelemetryConfigurationSnapshot Create(RuntimeConfig? config, bool saved = false)
        {
            if (config is null)
            {
                return Unknown;
            }

            try
            {
                Preparation preparation = new();
                RuntimeConfig bounded = preparation.Prepare(config, depth: 1).Config;
                ImmutableDictionary<string, string> projection = EngineTelemetrySnapshotFactory.Create(bounded);
                using JsonDocument schema = JsonDocument.Parse(UNKNOWN_JSON);
                if (projection.Count + 1 > MAX_PROPERTY_COUNT
                    || projection.Count + 1 != schema.RootElement.EnumerateObject().Count())
                {
                    return Unknown;
                }

                SortedDictionary<string, string> properties = new(StringComparer.Ordinal);
                foreach ((string key, string value) in projection)
                {
                    if (!schema.RootElement.TryGetProperty(key, out _) || value is null || value.Length > 128)
                    {
                        return Unknown;
                    }

                    // Retained child aggregates cannot prove omission for replaced/removed
                    // entities. Keep all other engine categories, including effective values.
                    properties.Add(key, preparation.EntityPresenceConflict
                        && key.StartsWith("entities.any.", StringComparison.Ordinal)
                        && key.EndsWith(".configured", StringComparison.Ordinal) ? "unknown" : value);
                }

                properties["snapshot_schema"] = "cli-configuration-v1";
                properties.Add("observation", saved ? "saved" : "loaded");
                string json = JsonSerializer.Serialize(properties);
                return json.Length <= MAX_JSON_CHARACTERS ? new(json) : Unknown;
            }
            catch (Exception)
            {
                // Projection/enumeration/disposal failures must never become command failures.
                // The fallback has no dependency on any failed callback or schema construction.
                return Unknown;
            }
        }

        private sealed record Prepared(RuntimeConfig Config, Dictionary<string, Entity> OriginalEntities, int Height);

        /// <summary>Call-local graph and budgets; no reference escapes into a snapshot.</summary>
        private sealed class Preparation
        {
            private readonly Dictionary<RuntimeConfig, Prepared> _prepared = new(ReferenceEqualityComparer.Instance);
            private readonly HashSet<RuntimeConfig> _active = new(ReferenceEqualityComparer.Instance);
            private int _configurationSourceNodes = MAX_CONFIGURATION_SOURCE_NODES;
            private int _childReferences = MAX_CHILD_REFERENCES;
            private int _modelNodes = MAX_MODEL_NODES;
            internal bool EntityPresenceConflict { get; private set; }

            internal Prepared Prepare(RuntimeConfig original, int depth)
            {
                Require(depth <= MAX_DEPTH);
                if (_prepared.TryGetValue(original, out Prepared? existing))
                {
                    // A shared subtree reached through a longer path still obeys the depth cap.
                    Require(depth + existing.Height - 1 <= MAX_DEPTH);
                    return existing;
                }

                Require(_active.Add(original)); // An active ancestor is a cycle, not shared data.
                Consume(ref _configurationSourceNodes);
                (string FileName, RuntimeConfig Config)[] children = ReadChildren(original);
                string defaultName = original.DefaultDataSourceName;
                Require(!string.IsNullOrWhiteSpace(defaultName));
                Dictionary<string, DataSource> sources = new(StringComparer.Ordinal);
                DataSource? actualSource = original.DataSource;
                DataSource? source = null;
                if (actualSource is not null)
                {
                    Consume(ref _configurationSourceNodes);
                    source = CopySource(actualSource);
                    sources.Add(defaultName, source);
                }

                foreach ((string name, DataSource mapped) in original.GetDataSourceNamesToDataSourcesIterator())
                {
                    // Record clones replace DataSource without updating the internal map.
                    // Its actual value wins, including removal; other merged sources survive.
                    if (!string.Equals(name, defaultName, StringComparison.Ordinal))
                    {
                        Consume(ref _configurationSourceNodes);
                        sources.Add(name, CopySource(mapped));
                    }
                }

                Dictionary<string, Entity> originalEntities = new(StringComparer.Ordinal);
                Dictionary<string, Entity> entities = new(StringComparer.Ordinal);
                foreach ((string name, Entity entity) in original.Entities)
                {
                    Consume(ref _modelNodes);
                    Require(entity is not null && entity.Source is not null);
                    originalEntities.Add(name, entity);
                    entities.Add(name, new Entity(
                        new EntitySource(string.Empty, entity.Source.Type, null, null),
                        new EntityGraphQLOptions(string.Empty, string.Empty, entity.IsGraphQLEnabled), null,
                        new EntityRestOptions(Enabled: entity.IsRestEnabled), CopyPermissions(entity.Permissions), null,
                        CopyRelationships(entity.Relationships), Cache: entity.Cache,
                        Description: string.IsNullOrEmpty(entity.Description) ? null : "present", Mcp: entity.Mcp));
                }

                Dictionary<string, Autoentity> autoentities = new(StringComparer.Ordinal);
                foreach ((string name, Autoentity definition) in original.Autoentities)
                {
                    Consume(ref _modelNodes);
                    Require(definition is not null);
                    autoentities.Add(name, new(null, null, CopyPermissions(definition.Permissions)));
                }

                // Bypass RuntimeEntities' name/default processing and the file-loading
                // RuntimeConfig constructor. All enumerables reaching the factory are owned.
                RuntimeEntities boundedEntities = new(new Dictionary<string, Entity>()) { Entities = entities };
                DataSourceFiles? files = original.DataSourceFiles is null ? null
                    : new(original.DataSourceFiles.SourceFiles is null ? null : children.Select(child => child.FileName).ToArray());
                RuntimeConfig bounded = new(string.Empty, source!, original.Runtime!, boundedEntities, defaultName,
                    sources, new Dictionary<string, string>(), files, original.AzureKeyVault, new RuntimeAutoentities(autoentities))
                {
                    TelemetryPresence = original.TelemetryPresence,
                    IsChildConfig = original.IsChildConfig
                };

                int height = 1;
                foreach ((string fileName, RuntimeConfig child) in children)
                {
                    Prepared preparedChild = Prepare(child, depth + 1);
                    foreach ((string name, DataSource childSource) in preparedChild.Config.GetDataSourceNamesToDataSourcesIterator())
                    {
                        Require(sources.TryGetValue(name, out DataSource? merged) && SameSource(merged, childSource));
                    }

                    foreach ((string name, Entity childEntity) in preparedChild.OriginalEntities)
                    {
                        EntityPresenceConflict |= !originalEntities.TryGetValue(name, out Entity? current)
                            || !ReferenceEquals(current, childEntity);
                    }

                    bounded.ChildConfigs.Add((fileName, preparedChild.Config));
                    height = Math.Max(height, preparedChild.Height + 1);
                }

                Prepared result = new(bounded, originalEntities, height);
                _active.Remove(original);
                _prepared.Add(original, result);
                return result;
            }

            private (string FileName, RuntimeConfig Config)[] ReadChildren(RuntimeConfig original)
            {
                int count = original.ChildConfigs.Count;
                Consume(ref _childReferences, count);
                IEnumerable<string>? references = original.DataSourceFiles?.SourceFiles;
                if (references is null)
                {
                    Require(count == 0);
                    return [];
                }

                (string FileName, RuntimeConfig Config)[] children = new (string, RuntimeConfig)[count];
                using (IEnumerator<string> declarations = references.GetEnumerator())
                {
                    for (int index = 0; index < count; index++)
                    {
                        (string fileName, RuntimeConfig child) = original.ChildConfigs[index];
                        Require(child is not null && !string.IsNullOrWhiteSpace(fileName)
                            && declarations.MoveNext() && string.Equals(fileName, declarations.Current, StringComparison.Ordinal));
                        children[index] = (fileName, child);
                    }

                    // Exactly one lookahead, including when there are zero loaded children.
                    // Never Count/Any/materialize an arbitrary declaration sequence.
                    Require(!declarations.MoveNext());
                }

                Require(original.ChildConfigs.Count == count);
                for (int index = 0; index < count; index++)
                {
                    Require(ReferenceEquals(children[index].Config, original.ChildConfigs[index].Config)
                        && string.Equals(children[index].FileName, original.ChildConfigs[index].FileName, StringComparison.Ordinal));
                }

                return children;
            }

            private EntityPermission[] CopyPermissions(EntityPermission[] permissions)
            {
                Require(permissions is not null);
                Consume(ref _modelNodes, permissions.Length);
                EntityPermission[] copy = new EntityPermission[permissions.Length];
                for (int index = 0; index < permissions.Length; index++)
                {
                    EntityPermission permission = permissions[index];
                    Require(permission is not null && permission.Role is not null && permission.Actions is not null);
                    EntityAction[] originalActions = permission.Actions;
                    Consume(ref _modelNodes, originalActions.Length);
                    EntityAction[] actions = new EntityAction[originalActions.Length];
                    for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
                    {
                        EntityAction action = originalActions[actionIndex];
                        Require(action is not null);
                        // Presence only. Do not parse, evaluate or copy policy text.
                        EntityActionPolicy? policy = action.Policy is null ? null : new(
                            Request: action.Policy.Request is null ? null : string.Empty,
                            Database: action.Policy.Database is null ? null : string.Empty);
                        actions[actionIndex] = new(action.Action, null, policy);
                    }

                    // Only the shared factory classifies role names; they remain call-local.
                    copy[index] = new(permission.Role, actions);
                }

                return copy;
            }

            private Dictionary<string, EntityRelationship>? CopyRelationships(Dictionary<string, EntityRelationship>? relationships)
            {
                if (relationships is null)
                {
                    return null;
                }

                Dictionary<string, EntityRelationship> copy = new(StringComparer.Ordinal);
                foreach ((string name, EntityRelationship relationship) in relationships)
                {
                    Consume(ref _modelNodes);
                    Require(relationship is not null);
                    copy.Add(name, relationship);
                }

                return copy;
            }

            private static DataSource CopySource(DataSource source)
            {
                Require(source is not null);
                Dictionary<string, object?>? options = source.Options is null ? null : new(StringComparer.Ordinal);
                if (source.Options?.TryGetValue("set-session-context", out object? value) == true)
                {
                    // Preserve absent/null/invalid versus bool without copying arbitrary objects.
                    options!.Add("set-session-context", value is bool enabled ? enabled : null);
                }

                return new(source.DatabaseType, string.Empty, options)
                {
                    UserDelegatedAuth = source.UserDelegatedAuth is null ? null : new(source.IsUserDelegatedAuthEnabled)
                };
            }

            private static bool SameSource(DataSource left, DataSource right) => left.DatabaseType == right.DatabaseType
                && left.IsUserDelegatedAuthEnabled == right.IsUserDelegatedAuthEnabled
                && left.Options?.Count == right.Options?.Count
                && Equals(left.Options?.GetValueOrDefault("set-session-context"), right.Options?.GetValueOrDefault("set-session-context"));

            private static void Consume(ref int remaining, int count = 1)
            {
                Require(count >= 0 && count <= remaining);
                remaining -= count;
            }

            private static void Require([DoesNotReturnIf(false)] bool valid)
            {
                if (!valid)
                {
                    throw new InvalidOperationException("Configuration projection is unavailable.");
                }
            }
        }
    }
}
