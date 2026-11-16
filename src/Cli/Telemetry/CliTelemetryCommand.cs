// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Cli.Commands;
using CommandLine;

namespace Cli.Telemetry
{
    /// <summary>
    /// A value-free description of a CLI invocation. Presence means the parser recognizes an
    /// explicitly supplied option name, not that its value or the invocation is valid.
    /// </summary>
    internal sealed record CliTelemetryCommand(string Name, string Control, ImmutableDictionary<string, string> Options)
    {
        internal const int MAX_OPTION_COUNT = 96;

        // Deliberately fixed: reflection supplies grammar, never approval for a new verb or option.
        // Keep this list aligned with Program.Execute; the tests audit the current verb metadata.
        internal static ImmutableDictionary<string, Type> RegisteredCommands { get; } =
            new Dictionary<string, Type>
            {
                ["init"] = typeof(InitOptions),
                ["add"] = typeof(AddOptions),
                ["update"] = typeof(UpdateOptions),
                ["start"] = typeof(StartOptions),
                ["validate"] = typeof(ValidateOptions),
                ["export"] = typeof(ExportOptions),
                ["add-telemetry"] = typeof(AddTelemetryOptions),
                ["configure"] = typeof(ConfigureOptions),
                ["auto-config"] = typeof(AutoConfigOptions),
                ["auto-config-simulate"] = typeof(AutoConfigSimulateOptions),
                ["appname"] = typeof(AppNameOptions)
            }.ToImmutableDictionary(StringComparer.Ordinal);

        // Only the presence of these reviewed long names is approved. In particular, approving
        // a connection-string, credential, path, description, or header option NEVER approves its value.
        internal static ImmutableHashSet<string> ApprovedLongOptions { get; } = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            // Common options and init.
            "config",
            "database-type",
            "connection-string",
            "cosmosdb_nosql-database",
            "cosmosdb_nosql-container",
            "graphql-schema",
            "set-session-context",
            "host-mode",
            "cors-origin",
            "auth.provider",
            "auth.audience",
            "auth.issuer",
            "rest.path",
            "runtime.base-route",
            "rest.disabled",
            "graphql.path",
            "graphql.disabled",
            "mcp.path",
            "mcp.disabled",
            "rest.enabled",
            "graphql.enabled",
            "mcp.enabled",
            "rest.request-body-strict",
            "graphql.multiple-mutations.create.enabled",
            "mcp.aggregate-records.query-timeout",
            // Entity options, add, and update.
            "source",
            "permissions",
            "source.type",
            "source.params",
            "source.key-fields",
            "rest",
            "rest.methods",
            "graphql",
            "graphql.operation",
            "fields.include",
            "fields.exclude",
            "policy-request",
            "policy-database",
            "cache.enabled",
            "cache.ttl-seconds",
            "cache.level",
            "health.enabled",
            "description",
            "parameters.name",
            "parameters.description",
            "parameters.required",
            "parameters.default",
            "fields.name",
            "fields.alias",
            "fields.description",
            "fields.primary-key",
            "mcp.dml-tools",
            "mcp.custom-tool",
            "relationship",
            "cardinality",
            "target.entity",
            "linking.object",
            "linking.source.fields",
            "linking.target.fields",
            "relationship.fields",
            "map",
            // Start and export. LogLevel is the registered, case-sensitive legacy spelling.
            "verbose",
            "log-level",
            "LogLevel",
            "no-https-redirect",
            "mcp-stdio",
            "output",
            "graphql-schema-file",
            "generate",
            "sampling-mode",
            "sampling-count",
            "sampling-partition-key-path",
            "sampling-days",
            "sampling-group-count",
            // Add-telemetry (customer-configured sinks, not a product telemetry opt-in).
            "app-insights-conn-string",
            "app-insights-enabled",
            "otel-endpoint",
            "otel-enabled",
            "otel-headers",
            "otel-protocol",
            "otel-service-name",
            // Configure.
            "data-source.database-type",
            "data-source.connection-string",
            "data-source.options.database",
            "data-source.options.container",
            "data-source.options.schema",
            "data-source.options.set-session-context",
            "data-source.health.name",
            "data-source.user-delegated-auth.enabled",
            "data-source.user-delegated-auth.database-audience",
            "data-source.user-delegated-auth.provider",
            "data-source.health.enabled",
            "data-source.health.threshold-ms",
            "data-source-files",
            "runtime.graphql.depth-limit",
            "runtime.graphql.enabled",
            "runtime.graphql.path",
            "runtime.graphql.allow-introspection",
            "runtime.graphql.multiple-mutations.create.enabled",
            "runtime.rest.enabled",
            "runtime.rest.path",
            "runtime.rest.request-body-strict",
            "runtime.mcp.enabled",
            "runtime.mcp.path",
            "runtime.mcp.description",
            "runtime.mcp.dml-tools.enabled",
            "runtime.mcp.dml-tools.describe-entities",
            "runtime.mcp.dml-tools.create-record",
            "runtime.mcp.dml-tools.read-records",
            "runtime.mcp.dml-tools.update-record",
            "runtime.mcp.dml-tools.delete-record",
            "runtime.mcp.dml-tools.execute-entity",
            "runtime.mcp.dml-tools.aggregate-records",
            "runtime.mcp.dml-tools.aggregate-records.query-timeout",
            "runtime.cache.enabled",
            "runtime.cache.ttl-seconds",
            "runtime.pagination.max-page-size",
            "runtime.pagination.default-page-size",
            "runtime.pagination.next-link-relative",
            "runtime.compression.level",
            "runtime.health.enabled",
            "runtime.health.cache-ttl-seconds",
            "runtime.health.max-query-parallelism",
            "runtime.health.roles",
            "runtime.host.mode",
            "runtime.host.cors.origins",
            "runtime.host.cors.allow-credentials",
            "runtime.host.authentication.provider",
            "runtime.host.authentication.jwt.audience",
            "runtime.host.authentication.jwt.issuer",
            "runtime.host.max-response-size-mb",
            "azure-key-vault.endpoint",
            "azure-key-vault.retry-policy.mode",
            "azure-key-vault.retry-policy.max-count",
            "azure-key-vault.retry-policy.delay-seconds",
            "azure-key-vault.retry-policy.max-delay-seconds",
            "azure-key-vault.retry-policy.network-timeout-seconds",
            "runtime.telemetry.azure-log-analytics.enabled",
            "runtime.telemetry.azure-log-analytics.dab-identifier",
            "runtime.telemetry.azure-log-analytics.flush-interval-seconds",
            "runtime.telemetry.azure-log-analytics.auth.custom-table-name",
            "runtime.telemetry.azure-log-analytics.auth.dcr-immutable-id",
            "runtime.telemetry.azure-log-analytics.auth.dce-endpoint",
            "runtime.telemetry.file.enabled",
            "runtime.telemetry.file.path",
            "runtime.telemetry.file.rolling-interval",
            "runtime.telemetry.file.retained-file-count-limit",
            "runtime.telemetry.file.file-size-limit-bytes",
            "runtime.telemetry.log-level",
            "show-effective-permissions",
            "runtime.embeddings.enabled",
            "runtime.embeddings.provider",
            "runtime.embeddings.base-url",
            "runtime.embeddings.api-key",
            "runtime.embeddings.model",
            "runtime.embeddings.api-version",
            "runtime.embeddings.dimensions",
            "runtime.embeddings.timeout-ms",
            "runtime.embeddings.endpoint.enabled",
            "runtime.embeddings.endpoint.roles",
            "runtime.embeddings.endpoint.path",
            "runtime.embeddings.health.enabled",
            "runtime.embeddings.health.threshold-ms",
            "runtime.embeddings.health.test-text",
            "runtime.embeddings.health.expected-dimensions",
            "runtime.embeddings.chunking.enabled",
            "runtime.embeddings.chunking.size-chars",
            "runtime.embeddings.chunking.overlap-chars",
            "runtime.embeddings.cache.enabled",
            "runtime.embeddings.cache.ttl-hours",
            "runtime.embeddings.cache.level-2.enabled",
            "runtime.embeddings.cache.level-2.connection-string",
            // Auto-config, auto-config-simulate, and appname (output is shared with export).
            "patterns.include",
            "patterns.exclude",
            "patterns.name",
            "template.mcp.dml-tools",
            "template.rest.enabled",
            "template.graphql.enabled",
            "template.cache.enabled",
            "template.cache.ttl-seconds",
            "template.cache.level",
            "template.health.enabled",
            "decode");

        private static readonly ImmutableDictionary<string, string> _emptyOptions =
            ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

        private static readonly ImmutableDictionary<string, ImmutableArray<OptionMetadata>> _commandOptions =
            RegisteredCommands.ToImmutableDictionary(pair => pair.Key, pair => ReadOptions(pair.Value), StringComparer.Ordinal);

        // The exact assignment grammar in CommandLineParser v2.9.1 Tokenizer.TokenizeLongName.
        // Use IsMatch over a span: neither a match/capture nor an argument substring is retained.
        private static readonly Regex _longAssignment = new("^([^=]+)=([^ ].*)$", RegexOptions.NonBacktracking);

        /// <summary>
        /// Inspects the names recognized by Program.Execute's CommandLineParser 2.9.1 settings,
        /// without parsing again, constructing command options, invoking handlers, or writing help.
        /// Only fixed names and constant "true" presence flags escape this method.
        /// </summary>
        /// <remarks>
        /// The caller's single real parse remains authoritative for success and ErrorType. A recognized
        /// option missing its value is still supplied; unknown names and malformed name assignments
        /// cannot contribute keys. Defaults, positional arguments, and option values are never collected.
        /// </remarks>
        public static CliTelemetryCommand Inspect(string[] args)
        {
            if (args is null || args.Length == 0 || args.Any(arg => arg is null))
            {
                return new("unknown", "none", _emptyOptions);
            }

            // InstanceChooser recognizes these controls only as the first argument. Help's optional
            // target is a verb name, not another option; trailing arguments are not tokenized.
            if (args[0] is "help" or "--help")
            {
                string target = args.Length > 1 && RegisteredCommands.TryGetKey(args[1], out string? name)
                    ? name
                    : "unknown";
                return new(target, "help", _emptyOptions);
            }

            if (args[0] is "version" or "--version")
            {
                return new("unknown", "version", _emptyOptions);
            }

            if (!RegisteredCommands.TryGetKey(args[0], out string? command))
            {
                return new("unknown", "none", _emptyOptions);
            }

            // InstanceBuilder/PreprocessorGuards check only the FIRST argument following the verb.
            string control = args.Length > 1 ? args[1] switch
            {
                "--help" => "help",
                "--version" => "version",
                _ => "none"
            } : "none";

            if (control != "none")
            {
                return new(command, control, _emptyOptions);
            }

            ImmutableDictionary<string, string>.Builder supplied = _emptyOptions.ToBuilder();
            ImmutableArray<OptionMetadata> options = _commandOptions[command];
            for (int i = 1; i < args.Length; i++)
            {
                InspectToken(args[i].AsSpan(), options, supplied);
            }

            return new(command, "none", supplied.ToImmutable());
        }

        private static void InspectToken(
            ReadOnlySpan<char> token,
            ImmutableArray<OptionMetadata> options,
            ImmutableDictionary<string, string>.Builder supplied)
        {
            if (token.Length < 2 || token[0] != '-')
            {
                return;
            }

            if (token[1] == '-')
            {
                // Program does NOT enable EnableDashDash/GetoptMode: bare "--" is discarded by
                // TokenizeLongName, not a terminator. Do not stop inspecting the remaining arguments.
                if (token.Length == 2)
                {
                    return;
                }

                ReadOnlySpan<char> name = token[2..];
                int equals = name.IndexOf('=');
                if (equals > 0)
                {
                    // The pinned tokenizer rejects a one-character assignment name (even --c=x),
                    // empty values, and values rejected by its regex. Nothing is emitted for that token.
                    if (equals == 1 || !_longAssignment.IsMatch(name))
                    {
                        return;
                    }

                    name = name[..equals];
                }

                AddPresence(FindOption(name, options), supplied);
                return;
            }

            // TokenizeShortName treats a lone '-' (above) and anything beginning with a digit
            // after '-' as a value. This is char.IsDigit, not numeric conversion or just ASCII digits.
            if (char.IsDigit(token[1]))
            {
                return;
            }

            for (int i = 1; i < token.Length; i++)
            {
                OptionMetadata? option = FindOption(token.Slice(i, 1), options);
                if (i > 1 && option is null)
                {
                    break;
                }

                AddPresence(option, supplied);

                // Boolean short switches may be grouped. Any other option consumes the entire
                // attached remainder, including '-' and '='. bool? is value-taking, unlike bool.
                if (option is not null && !option.IsSwitch)
                {
                    break;
                }
            }

            // Crucially, do not skip the next argv element for scalar or sequence options. The
            // non-getopt tokenizer classifies each element before TokenPartitioner assigns values.
            // A separate --verbose is an option, while -c--verbose and --config=--verbose contain
            // values. ExplodeOptionList only splits value tokens; it never retokenizes their contents.
        }

        private static OptionMetadata? FindOption(ReadOnlySpan<char> name, ImmutableArray<OptionMetadata> options)
        {
            foreach (OptionMetadata option in options)
            {
                // NameLookup matches either spelling even after '--' (e.g. --c without '=').
                if (name.SequenceEqual(option.LongName.AsSpan()) ||
                    (option.ShortName.Length != 0 && name.SequenceEqual(option.ShortName.AsSpan())))
                {
                    return option;
                }
            }

            return null;
        }

        private static void AddPresence(OptionMetadata? option, ImmutableDictionary<string, string>.Builder supplied)
        {
            if (option?.TelemetryKey is string key && supplied.Count < MAX_OPTION_COUNT)
            {
                supplied[key] = "true";
            }
        }

        private static ImmutableArray<OptionMetadata> ReadOptions(Type type)
        {
            ImmutableArray<OptionMetadata>.Builder options = ImmutableArray.CreateBuilder<OptionMetadata>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                OptionAttribute? attribute = property.GetCustomAttribute<OptionAttribute>();
                if (attribute is null)
                {
                    continue;
                }

                string? key = null;
                if (ApprovedLongOptions.TryGetValue(attribute.LongName, out string? approved))
                {
                    string canonical = approved == "LogLevel" ? "log-level" : approved;
                    key = "option_" + canonical.Replace('-', '_').Replace('.', '_');
                }

                // Include unapproved metadata ONLY for token boundaries: a future value-taking
                // option must still consume its attached suffix without approving a new dimension.
                bool isSwitch = property.PropertyType == typeof(bool) ||
                    (property.PropertyType == typeof(int) && attribute.FlagCounter);
                options.Add(new(attribute.LongName, attribute.ShortName, isSwitch, key));
            }

            return options.ToImmutable();
        }

        private sealed record OptionMetadata(string LongName, string ShortName, bool IsSwitch, string? TelemetryKey);
    }
}
