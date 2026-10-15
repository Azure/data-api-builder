// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Telemetry;

/// <summary>
/// Value-free provenance for the original input of one configuration file. The retained shape
/// is fixed: 26 built-in setting paths and five entity presence summaries. No JSON, configuration
/// reference, customer property/entity name, string value, or per-entity collection is retained.
/// Child files keep their own provenance; their merged entities are not counted again here.
/// </summary>
public sealed class TelemetryConfigurationPresence
{
    private static readonly AsyncLocal<Func<bool>?> _capturePermission = new();
    public const string TEST_MODE_ENV_VAR = ProductTelemetryPolicy.TEST_MODE_ENV_VAR;
    public const int SETTING_COUNT = 26;
    public const int ENTITY_FEATURE_COUNT = 5;

    public enum Presence { Unavailable, Missing, ExplicitNull, Present, Indeterminate }

    public enum EntityFeature { Cache, Rest, GraphQL, McpDml, McpCustomTool }

    /// <summary>
    /// Counts structural presence only, never enabled/disabled values. Fixed-width counters keep
    /// retained space constant regardless of the number or length of customer entity names.
    /// Custom-tool counts cover stored procedures only, matching that feature's applicability.
    /// </summary>
    public readonly record struct EntityPresence(long Missing, long ExplicitNull, long Present, long Indeterminate)
    {
        public long Total => Missing + ExplicitNull + Present + Indeterminate;

        public EntityPresence Combine(EntityPresence other) => new(
            Missing + other.Missing, ExplicitNull + other.ExplicitNull,
            Present + other.Present, Indeterminate + other.Indeterminate);

        internal EntityPresence Include(Presence presence) => presence switch
        {
            Presence.Missing => this with { Missing = Missing + 1 },
            Presence.ExplicitNull => this with { ExplicitNull = ExplicitNull + 1 },
            Presence.Present => this with { Present = Present + 1 },
            _ => this with { Indeterminate = Indeterminate + 1 }
        };
    }

    public ImmutableDictionary<string, Presence> Settings { get; }

    public ImmutableDictionary<EntityFeature, EntityPresence> Entities { get; }

    private TelemetryConfigurationPresence(
        ImmutableDictionary<string, Presence> settings,
        ImmutableDictionary<EntityFeature, EntityPresence> entities)
    {
        Settings = settings;
        Entities = entities;
    }

    /// <summary>Pure policy evaluation; an explicit synthetic opt-in is required and the veto wins.</summary>
    public static bool IsCaptureEnabled(string? testMode, string? productOptOut)
    {
        return ProductTelemetryPolicy.IsSyntheticCollectionEnabled(testMode, productOptOut);
    }

    // Parsing is shared with CLI and embedded hosts. Ambient test mode alone is not a host's
    // permission to collect; its fully evaluated session policy must authorize this load.
    public static bool IsCaptureEnabled() => _capturePermission.Value?.Invoke() == true && !ProductTelemetryPolicy.IsOptedOut();

    internal static IDisposable? BeginCapture(Func<bool>? permission)
    {
        if (permission is null)
        {
            return null; // Nested child loaders inherit the root's evaluated permission.
        }

        CaptureScope scope = new(_capturePermission.Value);
        _capturePermission.Value = permission;
        return scope;
    }

    private sealed class CaptureScope(Func<bool>? previous) : IDisposable
    {
        public void Dispose() => _capturePermission.Value = previous;
    }

    /// <summary>
    /// Pure, default-off capture after successful product deserialization. The caller supplies
    /// the matching accepted model only to identify original entities and custom-tool applicability;
    /// none of its values or references are retained. Never pass a reserialized/defaulted config.
    /// The enabled argument is the caller's evaluated policy; capture never reads the environment.
    /// Disabled capture does not parse JSON or allocate metadata. Bad JSON returns no provenance
    /// and does not replace the loader's validation or error handling.
    /// </summary>
    public static TelemetryConfigurationPresence? TryCapture(string json, RuntimeConfig config, bool enabled = false)
    {
        if (!enabled)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(config);
        try
        {
            // Match the loader's comment/depth/trailing-comma policy. Dispose the only raw document
            // before returning; even unrecognized properties never become retained metadata keys.
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            Cursor root = new(Presence.Present, document.RootElement);
            Cursor runtime = root.Child("runtime");
            Cursor telemetry = runtime.Child("telemetry");
            Cursor source = root.Child("data-source");
            ImmutableDictionary<string, Presence>.Builder settings = ImmutableDictionary.CreateBuilder<string, Presence>(StringComparer.Ordinal);
            settings.Add("runtime.rest.enabled", runtime.Child("rest").Enablement().State);
            settings.Add("runtime.graphql.enabled", runtime.Child("graphql").Enablement().State);
            settings.Add("runtime.mcp.enabled", runtime.Child("mcp").Enablement().State);
            settings.Add("runtime.health.enabled", runtime.Child("health").Child("enabled").State);
            settings.Add("runtime.cache.enabled", runtime.Child("cache").Child("enabled").State);
            settings.Add("runtime.cache.level-2.enabled", runtime.Child("cache").Child("level-2").Child("enabled").State);
            settings.Add("runtime.rest.request-body-strict", runtime.Child("rest").Child("request-body-strict").State);
            settings.Add("runtime.graphql.multiple-mutations.create.enabled", runtime.Child("graphql").Child("multiple-mutations").Child("create").Child("enabled").State);
            settings.Add("azure-key-vault", root.Child("azure-key-vault").State);
            settings.Add("autoentities", root.Child("autoentities").State);
            settings.Add("data-source-files", root.Child("data-source-files").State);
            settings.Add("runtime.embeddings.enabled", runtime.Child("embeddings").Child("enabled", ignoreCase: true).State);
            settings.Add("runtime.embeddings.endpoint.enabled", runtime.Child("embeddings").Child("endpoint", ignoreCase: true).Child("enabled", ignoreCase: true).State);
            settings.Add("runtime.telemetry.open-telemetry.enabled", telemetry.Child("open-telemetry").Child("enabled").State);
            settings.Add("runtime.telemetry.application-insights.enabled", telemetry.Child("application-insights").Child("enabled").State);
            settings.Add("runtime.telemetry.azure-log-analytics.enabled", telemetry.Child("azure-log-analytics").Child("enabled").State);
            settings.Add("runtime.telemetry.file.enabled", telemetry.Child("file").Child("enabled").State);
            settings.Add("runtime.host.authentication.provider", runtime.Child("host").Child("authentication").Child("provider").State);
            settings.Add("runtime.host.mode", runtime.Child("host").Child("mode").State);
            settings.Add("data-source.user-delegated-auth.enabled", source.Child("user-delegated-auth").Child("enabled").State);
            settings.Add("data-source.options.set-session-context", source.Child("options").Child("set-session-context").State);
            settings.Add("runtime.pagination.default-page-size", runtime.Child("pagination").Child("default-page-size").State);
            settings.Add("runtime.pagination.max-page-size", runtime.Child("pagination").Child("max-page-size").State);
            settings.Add("runtime.host.max-response-size-mb", runtime.Child("host").Child("max-response-size-mb").State);
            settings.Add("runtime.cache.ttl-seconds", runtime.Child("cache").Child("ttl-seconds").State);
            settings.Add("runtime.mcp.dml-tools.aggregate-records.query-timeout", runtime.Child("mcp").Child("dml-tools").Child("aggregate-records", ignoreCase: true).Child("query-timeout", ignoreCase: true).State);

            ImmutableDictionary<EntityFeature, EntityPresence>.Builder entities = ImmutableDictionary.CreateBuilder<EntityFeature, EntityPresence>();
            foreach (EntityFeature feature in Enum.GetValues<EntityFeature>())
            {
                entities.Add(feature, default);
            }

            Cursor originalEntities = root.Child("entities");
            if (originalEntities.IsObject)
            {
                foreach ((string name, Entity entity) in config.Entities)
                {
                    // Look up the last JSON property, as the product dictionary deserializer does.
                    // Iterate the accepted dictionary so duplicate JSON names cannot inflate counts.
                    // An absent definition can belong to a child or be generated; it proves no omission.
                    Cursor original = originalEntities.Child(name);
                    if (!original.IsObject)
                    {
                        continue;
                    }

                    entities[EntityFeature.Cache] = entities[EntityFeature.Cache].Include(original.Child("cache").Child("enabled").State);
                    entities[EntityFeature.Rest] = entities[EntityFeature.Rest].Include(original.Child("rest").Enablement(allowString: true).State);
                    entities[EntityFeature.GraphQL] = entities[EntityFeature.GraphQL].Include(original.Child("graphql").Enablement(allowString: true).State);
                    entities[EntityFeature.McpDml] = entities[EntityFeature.McpDml].Include(original.Child("mcp").ShorthandOrChild("dml-tools").State);
                    if (entity.Source.Type == EntitySourceType.StoredProcedure)
                    {
                        entities[EntityFeature.McpCustomTool] = entities[EntityFeature.McpCustomTool].Include(original.Child("mcp").Child("custom-tool").State);
                    }
                }
            }

            return new(settings.ToImmutable(), entities.ToImmutable());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A stack-local reader; no cursor escapes capture or enters the retained object.</summary>
    private readonly struct Cursor
    {
        private readonly JsonElement _value;
        public Presence State { get; }
        public bool IsObject => State == Presence.Present && _value.ValueKind == JsonValueKind.Object;

        public Cursor(Presence state, JsonElement value = default)
        {
            State = state;
            _value = value;
        }

        public Cursor Child(string property, bool ignoreCase = false)
        {
            if (State != Presence.Present)
            {
                return this;
            }

            if (_value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.String)
            {
                return new(Presence.Missing);
            }

            if (!IsObject)
            {
                return new(Presence.Indeterminate);
            }

            if (!ignoreCase)
            {
                return _value.TryGetProperty(property, out JsonElement child) ? FromValue(child) : new(Presence.Missing);
            }

            Cursor found = new(Presence.Missing);
            foreach (JsonProperty candidate in _value.EnumerateObject())
            {
                if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    found = FromValue(candidate.Value);
                }
            }

            return found;
        }

        private static Cursor FromValue(JsonElement value) => new(
            value.ValueKind == JsonValueKind.Null ? Presence.ExplicitNull : Presence.Present, value);

        public Cursor Enablement(bool allowString = false) => State == Presence.Present
            && (_value.ValueKind is JsonValueKind.True or JsonValueKind.False || (allowString && _value.ValueKind == JsonValueKind.String))
            ? this : Child("enabled");

        public Cursor ShorthandOrChild(string property) => State == Presence.Present
            && _value.ValueKind is JsonValueKind.True or JsonValueKind.False ? this : Child(property);
    }
}
