### 1. Context - What is running?

Context describes the product, its execution environment, and the metadata needed to interpret or correlate records. Database types here mean **configured sources**, not proof that those sources were queried.

**Product and execution environment**

- **DAB product version:** `dab_version`.
- **Release channel:** stable, preview, development, or unknown - `dab_release_channel`.
- **Distribution:** OSS, hosted, other, or unknown - `dab_distribution`.
- **Packaging:** container, CLI tool, library, standalone, or unknown - `dab_packaging`.
- **Execution mode:** web, MCP stdio, embedded, or unknown - `dab_execution_mode`.
- **Configured host mode:** development, production, or missing - `dab_host_mode`; not proof of a production workload.
- **Configuration delivery:** startup configuration, late configuration, or hot reload - `dab_config_delivery`.
- **Engine operating system:** family and optional coarse version - `dab_os_family`, `dab_os_version_bucket`; not the caller's OS.
- **Process architecture:** x64, arm64, x86, arm, or other - `dab_process_architecture`.
- **.NET runtime version:** normalized version - `dab_dotnet_runtime_version`.
- **Container detection:** detected, not detected, or unknown - `dab_container_state`.
- **Hosting category:** an approved coarse hosting classification - `dab_hosting_kind`; lower priority where detection is unreliable.
- **Deployment geography:** approved cloud region or coarse geography - `dab_cloud_region`; an explicit location/privacy decision, not inferred from caller IP.
- **Configured database-engine mix:** distinct source types - `dab_database_types`.
- **Data-source scale:** coarse number of configured active sources - `dab_datasource_count_bucket`; not an installation count.

**Record metadata and correlation, shared by events in all categories**

- **Event kind and schema version:** `event_name`, `dab_schema_version`.
- **Occurrence and receipt times:** `event_time_utc`, `server_received_time_utc`; receipt time is assigned at an agreed backend receiving point, not captured by DAB's client clock.
- **Retry deduplication identity:** random `dab_event_id`, reused on retries; deduplication is not automatic.
- **Within-process correlation:** random per-run `dab_process_session_id`; does not identify a returning installation or customer.
- **Accepted-configuration sequence:** process-local `dab_config_epoch`, to associate records with a configuration without hashing it.
- **Synthetic test marker:** `dab_synthetic`; not proof of authenticity or of an internal Microsoft user.
- **Collection metadata to inspect if supplied by the platform:** `SDKVersion` and sampling weight `ItemCount`; these are not DAB version or an aggregate operation count.

**Higher-risk or platform-dependent context choices**

- **Returning-installation linkage:** `dab_installation_id` or a separately defined rotating ID; requires an explicit persistence, rotation, deletion, and privacy decision.
- **Replica/customer linkage:** `dab_deployment_id` or customer/tenant identity; outside the minimal process-only proposal and separately reviewable, never customer names or credentials.
- **More precise segmentation:** exact OS build or geography instead of coarse context; identifying combinations may make omission preferable.
- **DataX-specific identity/internal classification:** resolve any mapping to `Id` and `IsMicrosoftInternal` during onboarding. The guide's device-ID examples (`MacAddressHash`, `devdeviceid`) are not approved DAB fields and must not be added just to qualify.

Details: [Envelope and correlation](#envelope-and-correlation), [Product, platform, and data-source context](#product-platform-and-data-source-context), and [Higher-risk property choices](#higher-risk-property-choices).

### 2. Configuration - What is enabled?

Configuration describes settings and aggregate feature presence, **not activity**. Keep configured values, effective values after defaults, and observed use distinct. Preserve `missing`, `disabled`, `unsupported`, and `unknown` where applicable; an empty entity set is different from an unused feature.

**Runtime capabilities and integrations**

- **API enablement:** REST, GraphQL, and MCP - `dab_cfg_rest_state`, `dab_cfg_graphql_state`, `dab_cfg_mcp_state`.
- **Health feature enablement:** `dab_cfg_health_state`; not a health-check result.
- **Caching:** runtime cache and L2 cache - `dab_cfg_cache_state`, `dab_cfg_l2_cache_state`.
- **Key Vault integration presence:** `dab_cfg_key_vault_present`; no vault endpoint or secret names.
- **Automatic entity definitions present:** `dab_cfg_auto_entities_present`; no patterns or names, and not proof of successful expansion.
- **REST strict request-body behavior:** `dab_cfg_rest_strict_body_state`.
- **GraphQL multiple-create setting:** `dab_cfg_graphql_multiple_create_state`.
- **Customer observability integrations enabled:** OpenTelemetry, Application Insights, Log Analytics, and file logging - `dab_cfg_customer_otel_state`, `dab_cfg_customer_appinsights_state`, `dab_cfg_customer_log_analytics_state`, `dab_cfg_customer_file_sink_state`; flags only, not destinations or forwarded diagnostics.
- **Authentication-provider category:** `dab_cfg_auth_provider`; no authorities, tokens, or claims.
- **On-behalf-of authentication across sources:** `dab_any_obo_state`.
- **Database session-context integration across applicable sources:** `dab_any_session_context_state`; no claim values.
- **Embedding capability and embedding-endpoint enablement:** `dab_cfg_embeddings_state`, `dab_cfg_embedding_endpoint_state`; no endpoint URL, prompts, text, or vectors.
- **Multi-file data-source configuration present:** `dab_datasource_files_present`; no filenames or paths.

These include the existing runtime fingerprint candidates: the 17 `dab_cfg_*` rows in the detailed runtime table, plus host mode (listed under Context), data-source files, and OBO, account for all **20 Application Name runtime positions**. Session-context integration is an additional candidate.

**Aggregate entity capabilities - whether any entity has the feature**

- **Source shape:** table, view, or stored procedure - `dab_entity_any_table_state`, `dab_entity_any_view_state`, `dab_entity_any_stored_procedure_state`.
- **Persisted-document source:** `dab_entity_any_persisted_document_state`; currently represented as unsupported by the existing encoder, not false.
- **Entity-level caching:** `dab_entity_any_cache_state`.
- **API exposure:** REST or GraphQL - `dab_entity_any_rest_state`, `dab_entity_any_graphql_state`.
- **MCP tool exposure:** DML or custom tools - `dab_entity_any_mcp_dml_state`, `dab_entity_any_mcp_custom_tool_state`.
- **Custom roles:** `dab_entity_any_custom_role_state`; no role names.
- **Request/database policy presence:** `dab_entity_any_policy_state`; no expressions or claims. Separate policy-kind flags are another candidate, with names to be decided.
- **Description presence:** `dab_entity_any_description_state`; lower-priority presence signal, never the description text.
- **Relationship presence:** `dab_entity_any_relationship_state`; no entity names or field mappings.
- **Parameter embedding:** `dab_entity_any_parameter_embedding_state`; currently represented as unsupported by the existing encoder, not false.

Together these cover all **14 Application Name entity positions**, aggregated over the merged entity set without emitting entity names.

**Scale and configured limits**

- **Entity-count bucket:** `dab_entity_count_bucket`.
- **Default and maximum page-size buckets:** `dab_cfg_default_page_size_bucket`, `dab_cfg_max_page_size_bucket`.
- **Maximum response-size bucket:** `dab_cfg_max_response_bytes_bucket`.
- **Cache-lifetime bucket:** `dab_cfg_cache_ttl_seconds_bucket`.
- **Query-timeout bucket:** `dab_cfg_query_timeout_seconds_bucket`, where applicable.
- **Higher-risk alternatives:** exact entity counts or `dab_config_fingerprint`; prefer coarse counts or a local config epoch where sufficient. Hashing raw configuration does not anonymize its secrets or identifying content.

Details: [Runtime configuration candidates](#runtime-configuration-candidates), [Aggregate entity configuration candidates](#aggregate-entity-configuration-candidates), and [Additional configuration and scale candidates](#additional-configuration-and-scale-candidates).

### 3. Observed usage - What is exercised?

Observed usage includes actual activity, lifecycle outcomes, and reliability measurements. Use bounded aggregation where selected; a request, logical operation, and database attempt are different counting units. Only measure approved dimensions and combinations, not the full cross-product or customer request content.

**Activity categories and counting scope**

- **API actually exercised:** REST, GraphQL, or MCP - `dab_usage_api`.
- **Logical operation category:** a closed read/write/execute taxonomy - `dab_usage_operation`.
- **Database engine actually exercised:** `dab_usage_database_type`.
- **Object category actually exercised:** table, view, procedure, document, or unknown - `dab_usage_object_type`.
- **Authorization-role class:** anonymous, authenticated, custom, or unknown - `dab_usage_role_class`; more sensitive segmentation, never role names.
- **Logical outcome category:** `dab_usage_outcome`; HTTP success does not necessarily mean logical success.
- **Measurement window:** start and actual duration - `dab_usage_window_start_utc`, `dab_usage_window_seconds`; define partial windows, reloads, and counter resets.

**Usage and performance measurements**

- **API request count:** `dab_usage_request_count`.
- **Logical operation count:** `dab_usage_operation_count`; alternatively `dab_usage_operation_count_bucket` if buckets are selected instead of exact counts.
- **Database attempt count:** `dab_usage_database_attempt_count`; includes attempts such as retries, not necessarily distinct user operations.
- **Count capping/overflow indicator:** `dab_usage_count_capped`.
- **Cache hits and misses:** `dab_usage_cache_hit_count`, `dab_usage_cache_miss_count`; agree the cache layer and denominator.
- **Embedding invocation count:** `dab_usage_embedding_count`; no embedding content.
- **Latency distribution:** `dab_usage_latency_bucket`, with agreed boundaries and associated counts; not an exact percentile by itself.
- **Startup/initialization duration:** `dab_startup_duration_ms`, measured between defined boundaries.
- **Uptime bucket:** `dab_uptime_seconds_bucket`.
- **Coarse resource use:** `dab_process_cpu_bucket`, `dab_process_memory_bytes_bucket`; collection overhead and normalization need review.
- **Telemetry quality:** `dab_telemetry_dropped_event_count`; cannot account for every delivery failure, and opt-out must remain silent.

**Lifecycle and reliability events**

- **Process launch:** `dab.engine.process_started`.
- **Successful initialization/readiness:** `dab.engine.ready`; not automatically equivalent to generic host startup or one unique installation.
- **Startup failure:** `dab.engine.startup_failed`, with `dab_failure_stage` and `dab_failure_category` from closed categories, not exception text.
- **Accepted configuration change:** `dab.engine.configuration_changed`, tied to a confirmed commit boundary and the config epoch.
- **Rejected/failed configuration change:** `dab.engine.configuration_change_failed`, with approved failure stage/category.
- **Periodic liveness:** `dab.engine.heartbeat`; a missing heartbeat is not proof of failure.
- **Bounded activity summary:** `dab.engine.usage_summary`, carrying the selected window, dimensions, and counts.
- **Graceful termination:** `dab.engine.stopped`, optionally with `dab_shutdown_reason`; missing stop events do not prove a crash.

**Higher-risk or questionable extensions:** individual request/trace IDs and per-request events instead of summaries, or exact database row counts instead of coarse scale signals. These add linkage, volume, or database-access work and need a separate justification; this inventory does not authorize extra database queries.
