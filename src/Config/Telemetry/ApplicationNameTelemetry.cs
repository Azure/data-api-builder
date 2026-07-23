// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;

namespace Azure.DataApiBuilder.Config.Telemetry;

/// <summary>
/// Encodes (and decodes) lightweight, anonymous DAB telemetry into the SQL Server
/// <c>Application Name</c> connection-string property.
///
/// Format:
/// <code>
/// dab_oss_&lt;version&gt;+&lt;context&gt;|&lt;runtime&gt;|&lt;entity&gt;+
/// </code>
/// Example: <c>dab_oss_1.2.3+XXSX|11111M10...|10111101M...|11111011...+</c>
///
/// The block is self-delimiting: it always starts with the <c>dab_oss_</c> marker and ends
/// with <c>+</c>, so it can be located and decoded even when it is appended after a user's
/// custom Application Name (e.g. <c>MyApp,dab_oss_...+...+</c>) or an OBO per-user pool hash
/// (e.g. <c>{hash}|MyApp,dab_oss_...+...+</c>). Because of this, the inner <c>|</c> separators
/// never need to change and the existing composition separators (<c>,</c> and OBO <c>|</c>) are
/// left untouched.
///
/// Encoding notes:
/// <list type="bullet">
/// <item>Sections are additive: new flags are appended to the end of a section (before the
/// <c>|</c>) so older decoders remain forward-compatible.</item>
/// <item>Boolean-style settings encode as <c>1</c> (enabled/true), <c>0</c> (disabled/false) or
/// <c>M</c> (the owning config section is missing).</item>
/// <item><c>?</c> marks a setting whose concept does not yet exist in the engine.</item>
/// <item>Context fields that are per-request (Protocol, Object, Role) are not knowable when a
/// pooled connection is opened, so they are encoded as <c>X</c>. Only <c>Source</c> (known per
/// data source) is populated at runtime; the CLI, which has no live connection, emits all
/// <c>X</c>.</item>
/// </list>
/// </summary>
public static class ApplicationNameTelemetry
{
    /// <summary>
    /// Environment variable used to opt out of embedding telemetry in the Application Name.
    /// When set to exactly <c>"1"</c> the payload is omitted and only <c>dab_oss_&lt;version&gt;</c>
    /// is emitted. Any other value (including <c>"0"</c>, missing or invalid) keeps telemetry on.
    /// </summary>
    public const string OPT_OUT_ENV_VAR = "DAB_TELEMETRY_APPNAME_OPT_OUT";

    /// <summary>Placeholder used for values that are unknown/not-applicable at the current scope.</summary>
    private const char NOT_APPLICABLE = 'X';

    /// <summary>Placeholder used for settings whose concept does not yet exist in the engine.</summary>
    private const char NOT_SUPPORTED = '?';

    /// <summary>Marks a config section that is missing entirely.</summary>
    private const char MISSING = 'M';

    private const char SECTION_SEPARATOR = '|';
    private const char PAYLOAD_DELIMITER = '+';

    /// <summary>Inputs available to a setting encoder.</summary>
    private readonly record struct EncodeInputs(RuntimeConfig Config, DataSource? LiveDataSource);

    /// <summary>A single telemetry setting: its name, how to encode it, and how to describe a value.</summary>
    private sealed record Setting(string Name, Func<EncodeInputs, char> Encode, Func<char, string> Describe);

    /// <summary>
    /// Produces the pure telemetry string (<c>dab_oss_&lt;version&gt;+&lt;context&gt;|&lt;runtime&gt;|&lt;entity&gt;+</c>),
    /// independent of the opt-out switch and of <c>DAB_APP_NAME_ENV</c>. Used by the CLI and as the
    /// telemetry-bearing portion of the connection-string segment.
    /// </summary>
    /// <param name="config">The runtime config to encode.</param>
    /// <param name="liveDataSource">
    /// The data source whose connection is being opened, or <c>null</c> when there is no live
    /// connection context (e.g. the <c>dab appname --config</c> CLI command). When <c>null</c>, the
    /// Source field is emitted as <c>X</c> and per–data-source flags (such as OBO) fall back to the
    /// config's default data source.
    /// </param>
    public static string EncodeTelemetryString(RuntimeConfig config, DataSource? liveDataSource = null)
    {
        EncodeInputs inputs = new(config, liveDataSource);

        string context = EncodeSection(_contextSettings, inputs);
        string runtime = EncodeSection(_runtimeSettings, inputs);
        string entity = EncodeSection(_entitySettings, inputs);

        return new StringBuilder()
            .Append(ProductInfo.DAB_USER_AGENT)
            .Append(PAYLOAD_DELIMITER)
            .Append(context).Append(SECTION_SEPARATOR)
            .Append(runtime).Append(SECTION_SEPARATOR)
            .Append(entity)
            .Append(PAYLOAD_DELIMITER)
            .ToString();
    }

    /// <summary>
    /// Builds the DAB-owned portion of the <c>Application Name</c> to embed in a connection string.
    /// <list type="bullet">
    /// <item>When opted out, only <c>dab_oss_&lt;version&gt;</c> is returned (no payload).</item>
    /// <item>Telemetry is always based on the product version (<see cref="ProductInfo.DAB_USER_AGENT"/>),
    /// so it is never suppressed by <c>DAB_APP_NAME_ENV</c>.</item>
    /// <item>When <c>DAB_APP_NAME_ENV</c> is set, its value is preserved as a comma prefix
    /// (e.g. <c>dab_hosted,dab_oss_...+...+</c>) so a host/custom label survives while the
    /// <c>dab_oss_</c> marker remains intact for decoding.</item>
    /// </list>
    /// </summary>
    /// <param name="config">The runtime config to encode.</param>
    /// <param name="liveDataSource">The data source whose connection is being opened.</param>
    public static string BuildApplicationNameSegment(RuntimeConfig config, DataSource? liveDataSource)
    {
        string telemetry = IsOptedOut()
            ? ProductInfo.DAB_USER_AGENT
            : EncodeTelemetryString(config, liveDataSource);

        string? customLabel = Environment.GetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV);

        return string.IsNullOrWhiteSpace(customLabel)
            ? telemetry
            : $"{customLabel},{telemetry}";
    }

    /// <summary>
    /// Decodes a telemetry-bearing Application Name into human-readable lines. The input may be a
    /// raw telemetry string or a full Application Name with a user prefix and/or OBO hash. Decoding
    /// is tolerant: a value truncated by SQL Server's 128-character limit, a missing trailing
    /// delimiter, or extra (newer) flags are all handled without throwing.
    /// </summary>
    /// <param name="applicationName">The Application Name (or telemetry string) to decode.</param>
    /// <returns>One human-readable line per recognized value.</returns>
    public static IReadOnlyList<string> Decode(string? applicationName)
    {
        List<string> lines = new();

        if (string.IsNullOrWhiteSpace(applicationName))
        {
            lines.Add("No DAB telemetry found (empty Application Name).");
            return lines;
        }

        int markerIndex = applicationName.IndexOf(ProductInfo.DAB_USER_AGENT_MARKER, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            lines.Add("No DAB telemetry found (missing 'dab_oss_' marker).");
            return lines;
        }

        // Everything from the marker onward, ignoring any user prefix / OBO hash before it.
        string block = applicationName[markerIndex..];

        int payloadStart = block.IndexOf(PAYLOAD_DELIMITER);
        string version = payloadStart < 0 ? block : block[..payloadStart];
        lines.Add($"Version: {version}");

        if (payloadStart < 0)
        {
            lines.Add("Telemetry payload: none (opted out or version-only Application Name).");
            return lines;
        }

        // Payload sits between the opening '+' and the (optional, possibly truncated) closing '+'.
        string payload = block[(payloadStart + 1)..];
        if (payload.EndsWith(PAYLOAD_DELIMITER))
        {
            payload = payload[..^1];
        }

        string[] sections = payload.Split(SECTION_SEPARATOR);
        DecodeSection(lines, "Context", _contextSettings, sections, index: 0);
        DecodeSection(lines, "Runtime", _runtimeSettings, sections, index: 1);
        DecodeSection(lines, "Entity", _entitySettings, sections, index: 2);

        return lines;
    }

    /// <summary>Returns true when telemetry has been explicitly opted out via the environment variable.</summary>
    private static bool IsOptedOut() =>
        string.Equals(
            Environment.GetEnvironmentVariable(OPT_OUT_ENV_VAR)?.Trim(),
            "1",
            StringComparison.Ordinal);

    private static string EncodeSection(IReadOnlyList<Setting> settings, EncodeInputs inputs)
    {
        char[] chars = new char[settings.Count];
        for (int i = 0; i < settings.Count; i++)
        {
            chars[i] = settings[i].Encode(inputs);
        }

        return new string(chars);
    }

    private static void DecodeSection(
        List<string> lines,
        string sectionName,
        IReadOnlyList<Setting> settings,
        string[] sections,
        int index)
    {
        if (index >= sections.Length)
        {
            // Section absent (truncated before this section was reached).
            return;
        }

        string section = sections[index];
        for (int i = 0; i < section.Length; i++)
        {
            char value = section[i];
            if (i < settings.Count)
            {
                lines.Add($"{sectionName} > {settings[i].Name}: {value} ({settings[i].Describe(value)})");
            }
            else
            {
                // A newer engine added a flag this decoder does not know about.
                lines.Add($"{sectionName} > [position {i + 1}]: {value} (unrecognized – added by a newer version)");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Value helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Encodes a tri-state flag: <c>1</c>=true, <c>0</c>=false, <c>M</c>=missing section.</summary>
    private static char Flag(bool? value) => value switch
    {
        true => '1',
        false => '0',
        null => MISSING,
    };

    /// <summary>Encodes a presence flag: <c>1</c>=present, <c>0</c>=absent.</summary>
    private static char Present(bool present) => present ? '1' : '0';

    /// <summary>Evaluates an "any entity matches" predicate, returning <c>M</c> when no entities exist.</summary>
    private static char AnyEntity(RuntimeConfig config, Func<Entity, bool> predicate)
    {
        IReadOnlyDictionary<string, Entity>? entities = config.Entities?.Entities;
        if (entities is null || entities.Count == 0)
        {
            return MISSING;
        }

        return Present(entities.Values.Any(predicate));
    }

    private static char EncodeSource(DatabaseType? source) => source switch
    {
        DatabaseType.MSSQL => 'S',
        DatabaseType.DWSQL => 'D',
        DatabaseType.PostgreSQL => 'P',
        DatabaseType.MySQL => 'M',
        DatabaseType.CosmosDB_NoSQL => 'C',
        DatabaseType.CosmosDB_PostgreSQL => 'C',
        _ => NOT_APPLICABLE,
    };

    /// <summary>
    /// Encodes whether on-behalf-of (user-delegated) auth is enabled for the data source. Uses the
    /// live data source when one is supplied, so each connection pool reflects its own setting;
    /// otherwise falls back to the config's default data source (e.g. the CLI, which has no live
    /// connection). Encoded as <c>M</c> when no data source is available.
    /// </summary>
    private static char EncodeObo(EncodeInputs inputs)
    {
        DataSource? dataSource = inputs.LiveDataSource ?? inputs.Config.DataSource;
        return dataSource is null ? MISSING : Present(dataSource.IsUserDelegatedAuthEnabled);
    }

    private static char EncodeHostMode(RuntimeConfig config)
    {
        HostOptions? host = config.Runtime?.Host;
        if (host is null)
        {
            return MISSING;
        }

        return host.Mode == HostMode.Production ? '1' : '0';
    }

    /// <summary>
    /// Encodes the authentication provider. The issue defines the alphabet <c>U E C S A W</c> without a
    /// legend; the mapping below is the chosen interpretation and is easy to adjust if needed.
    /// </summary>
    private static char EncodeAuthProvider(RuntimeConfig config)
    {
        AuthenticationOptions? auth = config.Runtime?.Host?.Authentication;
        if (auth is null)
        {
            return MISSING;
        }

        string provider = auth.Provider;
        if (provider.Equals(AuthenticationOptions.UNAUTHENTICATED_AUTHENTICATION, StringComparison.OrdinalIgnoreCase))
        {
            return 'U';
        }

        if (provider.Equals(AuthenticationOptions.SIMULATOR_AUTHENTICATION, StringComparison.OrdinalIgnoreCase))
        {
            return 'S';
        }

        if (provider.Equals(nameof(EasyAuthType.StaticWebApps), StringComparison.OrdinalIgnoreCase))
        {
            return 'W';
        }

        if (provider.Equals(nameof(EasyAuthType.AppService), StringComparison.OrdinalIgnoreCase))
        {
            return 'A';
        }

        if (provider.Equals("AzureAD", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("EntraID", StringComparison.OrdinalIgnoreCase))
        {
            return 'E';
        }

        // Any other (custom) JWT provider.
        return 'C';
    }

    private static bool UsesCustomRole(Entity entity) =>
        entity.Permissions is not null &&
        entity.Permissions.Any(p =>
            !p.Role.Equals("anonymous", StringComparison.OrdinalIgnoreCase) &&
            !p.Role.Equals("authenticated", StringComparison.OrdinalIgnoreCase));

    private static bool UsesPolicy(Entity entity) =>
        entity.Permissions is not null &&
        entity.Permissions.Any(p =>
            p.Actions is not null &&
            p.Actions.Any(a => a.Policy is not null &&
                (a.Policy.Database is not null || a.Policy.Request is not null)));

    // ---------------------------------------------------------------------------------------------
    // Describers (value char -> human-readable meaning) used for decoding.
    // ---------------------------------------------------------------------------------------------

    private static string DescribeFlag(char value) => value switch
    {
        '1' => "enabled/yes",
        '0' => "disabled/no",
        MISSING => "missing",
        NOT_SUPPORTED => "not yet supported",
        _ => "unrecognized",
    };

    private static string DescribeProtocol(char value) => value switch
    {
        'R' => "REST",
        'G' => "GraphQL",
        'M' => "MCP",
        NOT_APPLICABLE => "not applicable",
        _ => "unrecognized",
    };

    private static string DescribeObject(char value) => value switch
    {
        'T' => "Table",
        'V' => "View",
        'S' => "Stored Procedure",
        'P' => "Persisted Document",
        NOT_APPLICABLE => "not applicable",
        _ => "unrecognized",
    };

    private static string DescribeSource(char value) => value switch
    {
        'S' => "SQL",
        'D' => "DWSQL",
        'P' => "Postgres",
        'M' => "MySQL",
        'C' => "Cosmos",
        NOT_APPLICABLE => "not applicable",
        _ => "unrecognized",
    };

    private static string DescribeRole(char value) => value switch
    {
        'N' => "Anonymous",
        'A' => "Authenticated",
        'C' => "Custom",
        NOT_APPLICABLE => "not applicable",
        _ => "unrecognized",
    };

    private static string DescribeHostMode(char value) => value switch
    {
        '0' => "Development",
        '1' => "Production",
        MISSING => "missing",
        _ => "unrecognized",
    };

    private static string DescribeAuthProvider(char value) => value switch
    {
        'U' => "Unauthenticated",
        'E' => "EntraId",
        'C' => "Custom",
        'S' => "Simulator",
        'A' => "AppService",
        'W' => "StaticWebApps",
        MISSING => "missing",
        _ => "unrecognized",
    };

    // ---------------------------------------------------------------------------------------------
    // Schema – the ordered list of settings per section. Encoding and decoding share these lists so
    // they can never drift out of sync. Append new settings to the END of a section only.
    // ---------------------------------------------------------------------------------------------

    private static readonly IReadOnlyList<Setting> _contextSettings = new[]
    {
        // Protocol/Object/Role are per-request and unknown when a pooled connection is opened.
        new Setting("Protocol", _ => NOT_APPLICABLE, DescribeProtocol),
        new Setting("Object", _ => NOT_APPLICABLE, DescribeObject),
        new Setting("Source", i => EncodeSource(i.LiveDataSource?.DatabaseType), DescribeSource),
        new Setting("Role", _ => NOT_APPLICABLE, DescribeRole),
    };

    private static readonly IReadOnlyList<Setting> _runtimeSettings = new[]
    {
        new Setting("runtime.rest.enabled", i => Flag(i.Config.Runtime?.Rest?.Enabled), DescribeFlag),
        new Setting("runtime.graphql.enabled", i => Flag(i.Config.Runtime?.GraphQL?.Enabled), DescribeFlag),
        new Setting("runtime.mcp.enabled", i => Flag(i.Config.Runtime?.Mcp?.Enabled), DescribeFlag),
        new Setting("runtime.host.mode", i => EncodeHostMode(i.Config), DescribeHostMode),
        new Setting("data-source-files", i => Present(i.Config.DataSourceFiles?.SourceFiles?.Any() == true), DescribeFlag),
        new Setting("azure-key-vault", i => Present(!string.IsNullOrEmpty(i.Config.AzureKeyVault?.Endpoint)), DescribeFlag),
        new Setting("health.enabled", i => Flag(i.Config.Runtime?.Health?.Enabled), DescribeFlag),
        new Setting("cache.enabled", i => Flag(i.Config.Runtime?.Cache?.Enabled), DescribeFlag),
        new Setting("cache.l2", i => Flag(i.Config.Runtime?.Cache?.Level2?.Enabled), DescribeFlag),
        new Setting("data-source.obo", EncodeObo, DescribeFlag),
        new Setting("autoentities", i => Present(i.Config.Autoentities?.Any() == true), DescribeFlag),
        new Setting("rest.request-body-strict", i => Flag(i.Config.Runtime?.Rest?.RequestBodyStrict), DescribeFlag),
        new Setting("graphql.multiple-mutations.create.enabled", i => Flag(i.Config.Runtime?.GraphQL?.MultipleMutationOptions?.MultipleCreateOptions?.Enabled), DescribeFlag),
        new Setting("telemetry.open-telemetry.enabled", i => Flag(i.Config.Runtime?.Telemetry?.OpenTelemetry?.Enabled), DescribeFlag),
        new Setting("telemetry.application-insights.enabled", i => Flag(i.Config.Runtime?.Telemetry?.ApplicationInsights?.Enabled), DescribeFlag),
        new Setting("telemetry.azure-log-analytics.enabled", i => Flag(i.Config.Runtime?.Telemetry?.AzureLogAnalytics?.Enabled), DescribeFlag),
        new Setting("telemetry.file-sink.enabled", i => Flag(i.Config.Runtime?.Telemetry?.File?.Enabled), DescribeFlag),
        new Setting("auth.provider", i => EncodeAuthProvider(i.Config), DescribeAuthProvider),
        new Setting("embedding.enabled", i => Flag(i.Config.Runtime?.Embeddings?.Enabled), DescribeFlag),
        new Setting("embedding.endpoint.enabled", i => Flag(i.Config.Runtime?.Embeddings?.Endpoint?.Enabled), DescribeFlag),
    };

    private static readonly IReadOnlyList<Setting> _entitySettings = new[]
    {
        new Setting("entities.any.table", i => AnyEntity(i.Config, e => e.Source?.Type == EntitySourceType.Table), DescribeFlag),
        new Setting("entities.any.view", i => AnyEntity(i.Config, e => e.Source?.Type == EntitySourceType.View), DescribeFlag),
        new Setting("entities.any.stored-procedure", i => AnyEntity(i.Config, e => e.Source?.Type == EntitySourceType.StoredProcedure), DescribeFlag),
        // MCP persisted documents are not yet a modeled concept in the engine.
        new Setting("entities.any.mcp-persisted-document", _ => NOT_SUPPORTED, DescribeFlag),
        new Setting("entities.any.cache", i => AnyEntity(i.Config, e => e.Cache?.Enabled == true), DescribeFlag),
        new Setting("entities.any.rest.enabled", i => AnyEntity(i.Config, e => e.IsRestEnabled), DescribeFlag),
        new Setting("entities.any.graphql.enabled", i => AnyEntity(i.Config, e => e.IsGraphQLEnabled), DescribeFlag),
        new Setting("entities.any.mcp.dml-tools", i => AnyEntity(i.Config, e => e.Mcp?.DmlToolEnabled == true), DescribeFlag),
        new Setting("entities.any.mcp.custom-tool", i => AnyEntity(i.Config, e => e.Mcp?.CustomToolEnabled == true), DescribeFlag),
        new Setting("entities.any.custom-roles", i => AnyEntity(i.Config, UsesCustomRole), DescribeFlag),
        new Setting("entities.any.policies", i => AnyEntity(i.Config, UsesPolicy), DescribeFlag),
        new Setting("entities.any.descriptions", i => AnyEntity(i.Config, e => !string.IsNullOrEmpty(e.Description)), DescribeFlag),
        new Setting("entities.any.relationships", i => AnyEntity(i.Config, e => e.Relationships?.Any() == true), DescribeFlag),
        // Parameter-level embeddings are not yet a modeled concept in the engine.
        new Setting("entities.any.parameter.embed", _ => NOT_SUPPORTED, DescribeFlag),
    };
}
