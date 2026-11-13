// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Config.Telemetry;
using static Azure.DataApiBuilder.Config.Telemetry.TelemetryConfigurationPresence;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Projects an accepted configuration into a fixed, immutable categorical schema. This does
    /// not validate or accept configurations, initialize telemetry, or perform any external I/O.
    /// Effective values describe configuration/runtime gates, not readiness, successful service
    /// registration, permission to a particular caller, or observed use.
    /// </summary>
    internal static class EngineTelemetrySnapshotFactory
    {
        private const string ENABLED = "enabled";
        private const string DISABLED = "disabled";
        private const string MISSING = "missing";
        private const string UNSUPPORTED = "unsupported";
        private const string UNKNOWN = "unknown";
        private const string NOT_APPLICABLE = "not_applicable";

        // Order is schema-defined, never derived from customer names, enum numeric values, or
        // dictionary enumeration order. Even combinations of these labels have bounded size.
        private static readonly ImmutableArray<string> _databaseTypes =
            ["mssql", "dwsql", "postgresql", "mysql", "cosmosdb_nosql", "cosmosdb_postgresql", UNKNOWN];

        public static ImmutableDictionary<string, string> Create(RuntimeConfig config)
            => Create(config, explicitConfiguration: null);

        /// <summary>
        /// Optional provenance must be the original input corresponding to this accepted config,
        /// NOT RuntimeConfig.ToJson(): serializers write defaults back as explicit values. The
        /// caller owns the live document for this call only; no JSON, config references, names,
        /// or customer strings are retained. Without that transient input, use the safe presence
        /// metadata captured by the loader. Root JSON alone cannot establish omission in merged
        /// child or generated entities. Their lost provenance remains unknown.
        /// </summary>
        public static ImmutableDictionary<string, string> Create(RuntimeConfig config, JsonElement? explicitConfiguration)
        {
            ArgumentNullException.ThrowIfNull(config);
            ImmutableDictionary<string, string>.Builder result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            Input input = Input.FromRoot(explicitConfiguration, config.TelemetryPresence);
            Input runtimeInput = input.Child("runtime");
            RuntimeOptions? runtime = config.Runtime;
            RuntimeCacheOptions? cache = runtime?.Cache;
            EmbeddingsOptions? embeddings = runtime?.Embeddings;
            bool canUseCache = config.CanUseCache();

            result.Add("snapshot_schema", "configuration-v1");
            Add(result, "runtime.rest",
                Configured(runtimeInput.Child("rest").Enablement(), State(runtime?.Rest?.Enabled), UNKNOWN),
                State(config.IsRestEnabled));
            Add(result, "runtime.graphql",
                Configured(runtimeInput.Child("graphql").Enablement(), State(runtime?.GraphQL?.Enabled), UNKNOWN),
                State(config.IsGraphQLEnabled));
            Add(result, "runtime.mcp",
                Configured(runtimeInput.Child("mcp").Enablement(), State(runtime?.Mcp?.Enabled), UNKNOWN),
                State(config.IsMcpEnabled));
            Add(result, "runtime.health",
                Configured(runtimeInput.Child("health").Child("enabled"), State(runtime?.Health?.Enabled),
                    Provided(runtime?.Health?.Enabled, runtime?.Health?.UserProvidedEnabled == true)),
                State(config.IsHealthEnabled));
            Add(result, "runtime.cache",
                Configured(runtimeInput.Child("cache").Child("enabled"), State(cache?.Enabled), NullableSetting(cache?.Enabled)),
                State(canUseCache));

            // Startup installs L2 only under the runtime cache gate; query engines additionally
            // require CanUseCache(), including its session-context rule. No Redis probe is made.
            Add(result, "runtime.cache.l2",
                Configured(runtimeInput.Child("cache").Child("level-2").Child("enabled"),
                    State(cache?.Level2?.Enabled), NullableSetting(cache?.Level2?.Enabled)),
                State(canUseCache && cache?.Level2?.Enabled == true));
            Add(result, "runtime.rest.strict_body",
                Configured(runtimeInput.Child("rest").Child("request-body-strict"), State(runtime?.Rest?.RequestBodyStrict), UNKNOWN),
                config.IsRestEnabled ? State(config.IsRequestBodyStrict) : NOT_APPLICABLE);

            MultipleCreateOptions? multipleCreate = runtime?.GraphQL?.MultipleMutationOptions?.MultipleCreateOptions;
            Input multipleCreateInput = runtimeInput.Child("graphql").Child("multiple-mutations").Child("create").Child("enabled");
            Add(result, "runtime.graphql.multiple_create",
                Configured(multipleCreateInput, State(multipleCreate?.Enabled), multipleCreate is null ? UNKNOWN : State(multipleCreate.Enabled)),
                State(config.IsGraphQLEnabled && config.IsMultipleCreateOperationEnabled()));

            AddKeyVault(result, config, input);
            bool hasAutoentities = config.Autoentities.Any();
            // RuntimeConfig replaces an omitted autoentities collection with an empty one.
            // Nonempty definitions survive merging, but an empty model alone proves no omission.
            Add(result, "integrations.autoentities",
                hasAutoentities ? ENABLED : Configured(input.Child("autoentities"), DISABLED, UNKNOWN),
                State(hasAutoentities));
            bool hasSourceFiles = config.DataSourceFiles?.SourceFiles?.Any() == true;
            Add(result, "integrations.multiple_source_files",
                Configured(input.Child("data-source-files"), State(hasSourceFiles), config.DataSourceFiles is null ? UNKNOWN : State(hasSourceFiles)),
                State(hasSourceFiles));

            Input embeddingsInput = runtimeInput.Child("embeddings");
            Add(result, "runtime.embeddings",
                Configured(embeddingsInput.Child("enabled", ignoreCase: true), State(embeddings?.Enabled),
                    Provided(embeddings?.Enabled, embeddings?.UserProvidedEnabled == true)),
                State(embeddings?.Enabled == true));
            Add(result, "runtime.embeddings.endpoint",
                Configured(embeddingsInput.Child("endpoint", ignoreCase: true).Child("enabled", ignoreCase: true),
                    State(embeddings?.Endpoint?.Enabled), Provided(embeddings?.Endpoint?.Enabled, embeddings?.Endpoint?.UserProvidedEnabled == true)),
                State(embeddings?.Enabled == true && embeddings.IsEndpointEnabled));
            result.Add("runtime.embeddings.endpoint_present", State(embeddings?.Endpoint is not null));

            AddCustomerTelemetry(result, runtime?.Telemetry, runtimeInput.Child("telemetry"));
            string authentication = AuthenticationCategory(config);
            Add(result, "authentication.provider",
                Configured(runtimeInput.Child("host").Child("authentication").Child("provider"), authentication, UNKNOWN), authentication);
            string hostMode = runtime?.Host?.Mode switch
            {
                null or HostMode.Production => "production",
                HostMode.Development => "development",
                _ => UNKNOWN
            };
            Add(result, "host.mode", Configured(runtimeInput.Child("host").Child("mode"), hostMode, UNKNOWN), hostMode);

            AddDataSources(result, config, input);
            AddEntities(result, config, input, canUseCache);
            AddLimits(result, config, runtimeInput);
            return result.ToImmutable();
        }

        private static void AddCustomerTelemetry(ImmutableDictionary<string, string>.Builder result, TelemetryOptions? telemetry, Input input)
        {
            OpenTelemetryOptions? otel = telemetry?.OpenTelemetry;
            ApplicationInsightsOptions? insights = telemetry?.ApplicationInsights;
            AzureLogAnalyticsOptions? analytics = telemetry?.AzureLogAnalytics;
            FileSinkOptions? file = telemetry?.File;
            Add(result, "customer_telemetry.open_telemetry",
                Configured(input.Child("open-telemetry").Child("enabled"), State(otel?.Enabled), UNKNOWN),
                State(otel?.Enabled == true && Uri.TryCreate(otel.Endpoint, UriKind.Absolute, out _)));
            Add(result, "customer_telemetry.application_insights",
                Configured(input.Child("application-insights").Child("enabled"), State(insights?.Enabled), UNKNOWN),
                State(insights?.Enabled == true));

            // These are Startup's local registration/configuration predicates, not exporter health.
            bool analyticsAvailable = analytics?.Enabled == true && analytics.Auth is not null
                && !string.IsNullOrWhiteSpace(analytics.Auth.CustomTableName)
                && !string.IsNullOrWhiteSpace(analytics.Auth.DcrImmutableId)
                && !string.IsNullOrWhiteSpace(analytics.Auth.DceEndpoint);
            Add(result, "customer_telemetry.log_analytics",
                Configured(input.Child("azure-log-analytics").Child("enabled"), State(analytics?.Enabled),
                    Provided(analytics?.Enabled, analytics?.UserProvidedEnabled == true)),
                State(analyticsAvailable));
            Add(result, "customer_telemetry.file",
                Configured(input.Child("file").Child("enabled"), State(file?.Enabled), Provided(file?.Enabled, file?.UserProvidedEnabled == true)),
                State(file?.Enabled == true && !string.IsNullOrWhiteSpace(file.Path)));
        }

        private static void AddKeyVault(ImmutableDictionary<string, string>.Builder result, RuntimeConfig config, Input input)
        {
            string configured = NOT_APPLICABLE;
            bool hasEndpoint = false;
            HashSet<RuntimeConfig> visited = new(ReferenceEqualityComparer.Instance);
            Stack<RuntimeConfig> pending = new();
            pending.Push(config);
            while (pending.TryPop(out RuntimeConfig? current))
            {
                if (!visited.Add(current))
                {
                    continue;
                }

                bool currentHasEndpoint = !string.IsNullOrEmpty(current.AzureKeyVault?.Endpoint);
                hasEndpoint |= currentHasEndpoint;
                string fallback = current.AzureKeyVault is null ? UNKNOWN : State(currentHasEndpoint);
                Input currentInput = ReferenceEquals(current, config) ? input : Input.FromPresence(current.TelemetryPresence);
                string state = Configured(currentInput.Child("azure-key-vault"), State(currentHasEndpoint), fallback);
                configured = Any(configured, state);
                foreach ((_, RuntimeConfig child) in current.ChildConfigs)
                {
                    pending.Push(child);
                }
            }

            // DoReplaceAkvVar belongs to deserialization settings, not RuntimeConfig. An endpoint
            // alone cannot prove that variable replacement was enabled (or that AKV was used).
            Add(result, "integrations.key_vault", configured, hasEndpoint ? UNKNOWN : DISABLED);
        }

        private static void AddDataSources(ImmutableDictionary<string, string>.Builder result, RuntimeConfig config, Input input)
        {
            int typeMask = 0;
            long count = 0;
            string oboConfigured = NOT_APPLICABLE;
            string oboEffective = NOT_APPLICABLE;
            string sessionConfigured = NOT_APPLICABLE;
            string sessionEffective = NOT_APPLICABLE;
            foreach ((string name, DataSource source) in config.GetDataSourceNamesToDataSourcesIterator())
            {
                count++;
                int type = source.DatabaseType switch
                {
                    DatabaseType.MSSQL => 0,
                    DatabaseType.DWSQL => 1,
                    DatabaseType.PostgreSQL => 2,
                    DatabaseType.MySQL => 3,
                    DatabaseType.CosmosDB_NoSQL => 4,
                    DatabaseType.CosmosDB_PostgreSQL => 5,
                    _ => 6
                };
                typeMask |= 1 << type;
                Input sourceInput = string.Equals(name, config.DefaultDataSourceName, StringComparison.Ordinal)
                    ? input.Child("data-source") : ChildSourceInput(config, name).Child("data-source");

                // OBO validation permits MSSQL only. Session context is read by the MSSQL/DWSQL
                // executor, using GetTypedOptions (whose actual omitted value is false).
                if (source.DatabaseType == DatabaseType.MSSQL)
                {
                    oboConfigured = Any(oboConfigured, Configured(sourceInput.Child("user-delegated-auth").Child("enabled"), State(source.IsUserDelegatedAuthEnabled), UNKNOWN));
                    oboEffective = Any(oboEffective, State(source.IsUserDelegatedAuthEnabled));
                }
                else
                {
                    string unsupported = type == 6 ? UNKNOWN : UNSUPPORTED;
                    oboConfigured = Any(oboConfigured, unsupported);
                    oboEffective = Any(oboEffective, unsupported);
                }

                if (source.DatabaseType is DatabaseType.MSSQL or DatabaseType.DWSQL)
                {
                    string setting = source.Options is null ? UNKNOWN : MISSING;
                    if (source.Options?.TryGetValue("set-session-context", out object? value) == true)
                    {
                        setting = value is bool enabled ? State(enabled) : UNKNOWN;
                    }

                    sessionConfigured = Any(sessionConfigured, Configured(sourceInput.Child("options").Child("set-session-context"), setting, setting));
                    sessionEffective = Any(sessionEffective, State(source.GetTypedOptions<MsSqlOptions>()?.SetSessionContext == true));
                }
                else
                {
                    string unsupported = type == 6 ? UNKNOWN : UNSUPPORTED;
                    sessionConfigured = Any(sessionConfigured, unsupported);
                    sessionEffective = Any(sessionEffective, unsupported);
                }
            }

            Add(result, "data_sources.obo", oboConfigured, oboEffective);
            Add(result, "data_sources.session_context", sessionConfigured, sessionEffective);
            result.Add("scale.data_source_count", CountBucket(count));
            result.Add("data_sources.types", typeMask == 0 ? "none" : string.Join(",", _databaseTypes.Where((_, index) => (typeMask & (1 << index)) != 0)));
            int distinctTypes = _databaseTypes.Where((_, index) => (typeMask & (1 << index)) != 0).Count();
            result.Add("data_sources.distinct_type_count", (typeMask & (1 << 6)) != 0 ? UNKNOWN : distinctTypes switch
            {
                0 => "0",
                1 => "1",
                2 => "2",
                3 => "3",
                4 => "4",
                5 => "5",
                6 => "6",
                _ => UNKNOWN
            });
        }

        private static void AddEntities(ImmutableDictionary<string, string>.Builder result, RuntimeConfig config, Input input, bool canUseCache)
        {
            // A fixed descriptor list prevents entity/role/property names becoming output keys.
            (string Key, EntityFeature Feature, Func<string, Entity, Input, string> Configured, Func<string, Entity, string> Effective)[] features =
            [
                ("entities.any.cache", EntityFeature.Cache,
                    (_, entity, raw) => Configured(raw.Child("cache").Child("enabled"), State(entity.Cache?.Enabled), Provided(entity.Cache?.Enabled, entity.Cache?.UserProvidedEnabledOptions == true)),
                    (name, _) => State(canUseCache && config.IsEntityCachingEnabled(name))),
                ("entities.any.rest", EntityFeature.Rest,
                    (_, entity, raw) => Configured(raw.Child("rest").Enablement(allowString: true), State(entity.IsRestEnabled), UNKNOWN),
                    (_, entity) => State(config.IsRestEnabled && entity.IsRestEnabled)),
                ("entities.any.graphql", EntityFeature.GraphQL,
                    (_, entity, raw) => Configured(raw.Child("graphql").Enablement(allowString: true), State(entity.IsGraphQLEnabled), UNKNOWN),
                    (_, entity) => State(config.IsGraphQLEnabled && entity.IsGraphQLEnabled)),
                ("entities.any.mcp_dml", EntityFeature.McpDml,
                    (_, entity, raw) => Configured(raw.Child("mcp").ShorthandOrChild("dml-tools"), State(entity.Mcp?.DmlToolEnabled ?? true), Provided(entity.Mcp?.DmlToolEnabled, entity.Mcp?.UserProvidedDmlToolsEnabled == true)),
                    (_, entity) => McpDmlEffective(config, entity)),
                ("entities.any.mcp_custom_tool", EntityFeature.McpCustomTool,
                    (_, entity, raw) => CustomToolApplicability(entity) ?? Configured(raw.Child("mcp").Child("custom-tool"), State(entity.Mcp?.CustomToolEnabled ?? false), Provided(entity.Mcp?.CustomToolEnabled, entity.Mcp?.UserProvidedCustomToolEnabled == true)),
                    (_, entity) => CustomToolApplicability(entity) ?? State(config.IsMcpEnabled && entity.Mcp?.CustomToolEnabled == true))
            ];

            foreach ((string key, EntityFeature feature, Func<string, Entity, Input, string> configured, Func<string, Entity, string> effective) in features)
            {
                string configuredAny = NOT_APPLICABLE;
                string effectiveAny = NOT_APPLICABLE;
                foreach ((string name, Entity entity) in config.Entities)
                {
                    // A missing entity in the root input can be a merged child or an expansion.
                    // It is NOT evidence that that entity omitted all its settings.
                    Input entityInput = input.Child("entities").ExistingObject(name);
                    configuredAny = Any(configuredAny, configured(name, entity, entityInput));
                    effectiveAny = Any(effectiveAny, effective(name, entity));
                }

                if (input.Provenance is not null)
                {
                    configuredAny = ConfiguredEntityPresence(config, input.Provenance, feature, configuredAny);
                }

                Add(result, key, configuredAny, effectiveAny);
            }

            result.Add("entities.any.table", AnyEntity(config, entity => SourceType(entity, EntitySourceType.Table)));
            result.Add("entities.any.view", AnyEntity(config, entity => SourceType(entity, EntitySourceType.View)));
            result.Add("entities.any.stored_procedure", AnyEntity(config, entity => SourceType(entity, EntitySourceType.StoredProcedure)));
            // Neither is a modeled entity capability. Cosmos DB is not MCP persisted documents.
            result.Add("entities.any.persisted_document", UNSUPPORTED);
            result.Add("entities.any.parameter_embeddings", UNSUPPORTED);
            result.Add("entities.any.custom_roles", AnyEntity(config, entity => State(entity.Permissions?.Any(permission =>
                !string.Equals(permission.Role, "anonymous", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(permission.Role, "authenticated", StringComparison.OrdinalIgnoreCase)) == true)));
            result.Add("entities.any.request_policy", AnyEntity(config, entity => State(HasPolicy(entity, request: true))));
            result.Add("entities.any.database_policy", AnyEntity(config, entity => State(HasPolicy(entity, request: false))));
            result.Add("entities.any.policies", AnyEntity(config, entity => State(HasPolicy(entity, request: true) || HasPolicy(entity, request: false))));
            result.Add("entities.any.descriptions", AnyEntity(config, entity => State(!string.IsNullOrEmpty(entity.Description))));
            result.Add("entities.any.relationships", AnyEntity(config, entity => State(entity.Relationships?.Count > 0)));
            result.Add("scale.entity_count", CountBucket(config.Entities.Entities.Count));
        }

        private static Input ChildSourceInput(RuntimeConfig config, string dataSourceName)
        {
            foreach (RuntimeConfig child in Configurations(config))
            {
                if (!ReferenceEquals(child, config) && string.Equals(child.DefaultDataSourceName, dataSourceName, StringComparison.Ordinal))
                {
                    return Input.FromPresence(child.TelemetryPresence);
                }
            }

            return default;
        }

        private static IEnumerable<RuntimeConfig> Configurations(RuntimeConfig root)
        {
            HashSet<RuntimeConfig> visited = new(ReferenceEqualityComparer.Instance);
            Stack<RuntimeConfig> pending = new();
            pending.Push(root);
            while (pending.TryPop(out RuntimeConfig? current))
            {
                if (!visited.Add(current))
                {
                    continue;
                }

                yield return current;
                foreach ((_, RuntimeConfig child) in current.ChildConfigs)
                {
                    pending.Push(child);
                }
            }
        }

        private static string ConfiguredEntityPresence(RuntimeConfig config, TelemetryConfigurationPresence rootPresence, EntityFeature feature, string fallback)
        {
            EntityPresence presence = default;
            foreach (RuntimeConfig current in Configurations(config))
            {
                TelemetryConfigurationPresence? original = ReferenceEquals(current, config) ? rootPresence : current.TelemetryPresence;
                if (original is not null)
                {
                    presence = presence.Combine(original.Entities[feature]);
                }
            }

            long applicable = 0;
            long enabled = 0;
            bool unknownApplicability = false;
            foreach ((_, Entity entity) in config.Entities)
            {
                if (feature == EntityFeature.McpCustomTool && CustomToolApplicability(entity) is string applicability)
                {
                    unknownApplicability |= applicability == UNKNOWN;
                    continue;
                }

                applicable++;
                bool enabledInModel = feature switch
                {
                    EntityFeature.Rest => entity.IsRestEnabled,
                    EntityFeature.GraphQL => entity.IsGraphQLEnabled,
                    EntityFeature.Cache => entity.Cache?.UserProvidedEnabledOptions == true && entity.Cache.Enabled == true,
                    EntityFeature.McpDml => entity.Mcp?.UserProvidedDmlToolsEnabled == true && entity.Mcp.DmlToolEnabled,
                    EntityFeature.McpCustomTool => entity.Mcp?.UserProvidedCustomToolEnabled == true && entity.Mcp.CustomToolEnabled,
                    _ => false
                };
                enabled += enabledInModel ? 1 : 0;
            }

            string configured = ResolveEntityPresence(presence, applicable, enabled,
                defaultEnabled: feature is EntityFeature.Rest or EntityFeature.GraphQL, fallback: fallback);
            return Any(configured, unknownApplicability ? UNKNOWN : NOT_APPLICABLE);
        }

        private static string ResolveEntityPresence(EntityPresence presence, long applicable, long enabled, bool defaultEnabled, string fallback)
        {
            if (presence.Total == 0)
            {
                return fallback;
            }

            if (presence.Total > applicable)
            {
                // The model no longer matches the captured declarations (for example after
                // programmatic removal). Do not present an old aggregate as current provenance.
                return UNKNOWN;
            }

            long unavailable = applicable - presence.Total;
            // REST/GraphQL converters erase explicitness and default missing/null options to on.
            // Subtract those known defaults before claiming an explicitly enabled entity. The
            // other three features retain UserProvided flags, so their defaults were not counted.
            long defaultedEnabled = defaultEnabled ? presence.Missing + presence.ExplicitNull : 0;
            if (presence.Present > 0 && enabled > defaultedEnabled + presence.Indeterminate + unavailable)
            {
                return ENABLED;
            }

            if (unavailable > 0)
            {
                // Uncaptured children/generated entities are not omitted declarations. A retained
                // UserProvided flag can still prove a positive, but negatives cannot erase doubt.
                return fallback == ENABLED ? ENABLED : UNKNOWN;
            }

            if (presence.ExplicitNull > 0 || presence.Indeterminate > 0)
            {
                return UNKNOWN;
            }

            return presence.Present > 0 ? DISABLED : MISSING;
        }

        private static string McpDmlEffective(RuntimeConfig config, Entity entity)
        {
            if (!config.IsMcpEnabled || entity.Mcp?.DmlToolEnabled == false)
            {
                return DISABLED;
            }

            DmlToolsConfig? tools = config.McpDmlTools;
            if (tools is null)
            {
                // IsEnabled advertises default-on tools, but ExecuteAsync rejects a null
                // McpDmlTools. Do not invent effective data-serving enablement from discovery.
                return UNKNOWN;
            }

            return entity.Source.Type switch
            {
                EntitySourceType.StoredProcedure => State(tools.ExecuteEntity == true),
                EntitySourceType.Table or EntitySourceType.View => State(tools.CreateRecord == true || tools.ReadRecords == true
                    || tools.UpdateRecord == true || tools.DeleteRecord == true || tools.AggregateRecords == true),
                _ => UNKNOWN
            };
        }

        private static string? CustomToolApplicability(Entity entity) => entity.Source.Type switch
        {
            EntitySourceType.StoredProcedure => null,
            EntitySourceType.Table or EntitySourceType.View => NOT_APPLICABLE,
            _ => UNKNOWN
        };

        private static bool HasPolicy(Entity entity, bool request) => entity.Permissions?.Any(permission =>
            permission.Actions?.Any(action => (request ? action.Policy?.Request : action.Policy?.Database) is not null) == true) == true;

        private static string SourceType(Entity entity, EntitySourceType expected) => entity.Source.Type switch
        {
            EntitySourceType.Table or EntitySourceType.View or EntitySourceType.StoredProcedure => State(entity.Source.Type == expected),
            _ => UNKNOWN
        };

        private static string AnyEntity(RuntimeConfig config, Func<Entity, string> select)
        {
            string state = NOT_APPLICABLE;
            foreach ((_, Entity entity) in config.Entities)
            {
                state = Any(state, select(entity));
            }

            return state;
        }

        private static void AddLimits(ImmutableDictionary<string, string>.Builder result, RuntimeConfig config, Input input)
        {
            PaginationOptions? pagination = config.Runtime?.Pagination;
            HostOptions? host = config.Runtime?.Host;
            RuntimeCacheOptions? cache = config.Runtime?.Cache;
            string defaultPage = PageBucket(config.DefaultPageSize());
            string maxPage = PageBucket(config.MaxPageSize());
            string maxBytes = BytesBucket((long)config.MaxResponseSizeMB() * 1024 * 1024);
            Add(result, "limits.default_page_size", Configured(input.Child("pagination").Child("default-page-size"), defaultPage, pagination?.UserProvidedDefaultPageSize == true ? defaultPage : UNKNOWN), defaultPage);
            Add(result, "limits.max_page_size", Configured(input.Child("pagination").Child("max-page-size"), maxPage, pagination?.UserProvidedMaxPageSize == true ? maxPage : UNKNOWN), maxPage);
            Add(result, "limits.max_response_bytes", Configured(input.Child("host").Child("max-response-size-mb"), maxBytes, host?.UserProvidedMaxResponseSizeMB == true ? maxBytes : UNKNOWN), maxBytes);
            result.Add("limits.max_response_enforced", State(config.MaxResponseSizeLogicEnabled()));
            Add(result, "limits.cache_ttl_seconds", Configured(input.Child("cache").Child("ttl-seconds"), SecondsBucket(cache?.TtlSeconds), cache?.UserProvidedTtlOptions == true ? SecondsBucket(cache.TtlSeconds) : UNKNOWN), SecondsBucket(config.GlobalCacheEntryTtl()));

            // There is no global query-timeout setting. The modeled timeout is specifically for
            // MCP aggregate-records; do not label it as the limit on REST/GraphQL/all SQL queries.
            Add(result, "limits.query_timeout_seconds", UNSUPPORTED, UNSUPPORTED);
            DmlToolsConfig? tools = config.McpDmlTools;
            int timeout = tools?.EffectiveAggregateRecordsQueryTimeoutSeconds ?? DmlToolsConfig.DEFAULT_QUERY_TIMEOUT_SECONDS;
            bool valid = timeout is >= 1 and <= DmlToolsConfig.MAX_QUERY_TIMEOUT_SECONDS;
            string timeoutBucket = valid ? SecondsBucket(timeout) : UNKNOWN;
            string configuredTimeout = Configured(input.Child("mcp").Child("dml-tools").Child("aggregate-records", ignoreCase: true).Child("query-timeout", ignoreCase: true),
                timeoutBucket, tools?.UserProvidedAggregateRecordsQueryTimeout == true ? timeoutBucket : UNKNOWN);
            // AggregateRecordsTool applies this same defensive fallback, even for a programmatic
            // out-of-range value that has not passed configuration validation.
            string effectiveTimeout = config.IsMcpEnabled && tools?.AggregateRecords == true
                ? SecondsBucket(valid ? timeout : DmlToolsConfig.DEFAULT_QUERY_TIMEOUT_SECONDS) : NOT_APPLICABLE;
            Add(result, "limits.mcp_aggregate_query_timeout_seconds", configuredTimeout, effectiveTimeout);
        }

        private static string AuthenticationCategory(RuntimeConfig config)
        {
            if (config.IsUnauthenticatedIdentityProvider)
            {
                return "unauthenticated";
            }

            if (config.IsStaticWebAppsIdentityProvider)
            {
                return "static_web_apps";
            }

            if (config.IsAppServiceIdentityProvider)
            {
                return "app_service";
            }

            string? provider = config.Runtime?.Host?.Authentication?.Provider;
            if (string.IsNullOrWhiteSpace(provider))
            {
                return UNKNOWN;
            }

            if (string.Equals(provider, AuthenticationOptions.SIMULATOR_AUTHENTICATION, StringComparison.OrdinalIgnoreCase))
            {
                return "simulator";
            }

            return string.Equals(provider, "AzureAD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider, "EntraID", StringComparison.OrdinalIgnoreCase) ? "entra_id" : "jwt";
        }

        private static void Add(ImmutableDictionary<string, string>.Builder result, string key, string configured, string effective)
        {
            result.Add(key + ".configured", configured);
            result.Add(key + ".effective", effective);
        }

        private static string State(bool? value) => value switch { true => ENABLED, false => DISABLED, _ => UNKNOWN };
        // A null or false UserProvided flag can also represent explicit JSON null. Only positive
        // provenance (or a preserved nullable value) proves an explicit assignment without input.
        private static string NullableSetting(bool? value) => value.HasValue ? State(value) : UNKNOWN;
        private static string Provided(bool? value, bool provided) => provided ? State(value) : UNKNOWN;

        // Enabled proves "any"; otherwise uncertainty must not be erased by a known negative.
        // Unsupported-only inputs stay unsupported; no applicable targets stay not_applicable.
        private static string Any(string left, string right)
        {
            if (left == ENABLED || right == ENABLED)
            {
                return ENABLED;
            }

            if (left == UNKNOWN || right == UNKNOWN)
            {
                return UNKNOWN;
            }

            if (left == DISABLED || right == DISABLED)
            {
                return DISABLED;
            }

            if (left == MISSING || right == MISSING)
            {
                return MISSING;
            }

            if (left == UNSUPPORTED || right == UNSUPPORTED)
            {
                return UNSUPPORTED;
            }

            return NOT_APPLICABLE;
        }

        // Upper endpoints are inclusive; labels and boundaries are part of configuration-v1.
        private static string CountBucket(long value) => value switch
        {
            < 0 => UNKNOWN,
            0 => "0",
            1 => "1",
            <= 10 => "2-10",
            <= 50 => "11-50",
            <= 100 => "51-100",
            <= 500 => "101-500",
            _ => "501+"
        };

        private static string PageBucket(long value) => value switch
        {
            <= 0 => UNKNOWN,
            <= 10 => "1-10",
            <= 100 => "11-100",
            <= 1000 => "101-1000",
            <= 10000 => "1001-10000",
            <= 100000 => "10001-100000",
            _ => "100001+"
        };

        private static string BytesBucket(long value) => value switch
        {
            <= 0 => UNKNOWN,
            <= 1048576 => "1-1048576",
            <= 16777216 => "1048577-16777216",
            <= 67108864 => "16777217-67108864",
            <= 268435456 => "67108865-268435456",
            _ => "268435457+"
        };

        private static string SecondsBucket(long? value) => value switch
        {
            null or <= 0 => UNKNOWN,
            <= 5 => "1-5",
            <= 30 => "6-30",
            <= 60 => "31-60",
            <= 300 => "61-300",
            <= 3600 => "301-3600",
            _ => "3601+"
        };

        private static string Configured(Input input, string resolved, string fallback) => input.Kind switch
        {
            InputKind.Missing => MISSING,
            InputKind.Value => input.IsNull ? UNKNOWN : resolved,
            InputKind.Indeterminate => UNKNOWN,
            _ => fallback
        };

        private enum InputKind { Unavailable, Missing, Value, Indeterminate }

        /// <summary>Transient presence cursor, never retained in a snapshot or static state.</summary>
        private readonly struct Input
        {
            private readonly JsonElement _value;
            private InputKind OriginalKind { get; }
            private readonly string? _path;
            public TelemetryConfigurationPresence? Provenance { get; }
            private Presence Captured => _path is not null && Provenance is not null && Provenance.Settings.TryGetValue(_path, out Presence value)
                ? value : Presence.Unavailable;
            public InputKind Kind => Provenance is null ? OriginalKind : Captured switch
            {
                Presence.Missing => InputKind.Missing,
                Presence.Present or Presence.ExplicitNull => InputKind.Value,
                Presence.Indeterminate => InputKind.Indeterminate,
                _ => InputKind.Unavailable
            };
            public bool IsNull => Provenance is null
                ? _value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                : Captured == Presence.ExplicitNull;

            private Input(InputKind kind, JsonElement value = default)
            {
                OriginalKind = kind;
                _value = value;
                _path = null;
                Provenance = null;
            }

            private Input(TelemetryConfigurationPresence provenance, string path)
            {
                Provenance = provenance;
                _path = path;
                OriginalKind = InputKind.Unavailable;
                _value = default;
            }

            public static Input FromPresence(TelemetryConfigurationPresence? presence) => presence is null ? default : new(presence, string.Empty);

            public static Input FromRoot(JsonElement? value, TelemetryConfigurationPresence? fallback) => value is null ? FromPresence(fallback)
                : value.Value.ValueKind == JsonValueKind.Object ? new(InputKind.Value, value.Value) : new(InputKind.Indeterminate);

            public Input Child(string property, bool ignoreCase = false)
            {
                if (Provenance is not null)
                {
                    // Paths here contain only factory-owned schema names. Case handling and
                    // shorthand/ancestor-null normalization already happened during capture.
                    return new(Provenance, string.IsNullOrEmpty(_path) ? property : _path + "." + property);
                }

                if (Kind != InputKind.Value)
                {
                    return this;
                }

                if (_value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.String)
                {
                    return new(InputKind.Missing);
                }

                if (_value.ValueKind != JsonValueKind.Object)
                {
                    return new(InputKind.Indeterminate);
                }

                if (!ignoreCase)
                {
                    return _value.TryGetProperty(property, out JsonElement child) ? new(InputKind.Value, child) : new(InputKind.Missing);
                }

                Input found = new(InputKind.Missing);
                foreach (JsonProperty candidate in _value.EnumerateObject())
                {
                    if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                    {
                        found = new(InputKind.Value, candidate.Value);
                    }
                }

                return found;
            }

            public Input Enablement(bool allowString = false) => Provenance is null && Kind == InputKind.Value
                && (_value.ValueKind is JsonValueKind.True or JsonValueKind.False || (allowString && _value.ValueKind == JsonValueKind.String))
                ? this : Child("enabled");

            public Input ShorthandOrChild(string property) => Provenance is null && Kind == InputKind.Value
                && _value.ValueKind is JsonValueKind.True or JsonValueKind.False ? this : Child(property);

            public Input ExistingObject(string property)
            {
                if (Provenance is not null)
                {
                    // Safe metadata has aggregate provenance, never customer-named children.
                    return default;
                }

                Input child = Child(property);
                return child.Kind == InputKind.Value && child._value.ValueKind == JsonValueKind.Object ? child : default;
            }
        }
    }
}
