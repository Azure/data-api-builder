# Engine Usage Telemetry

> **Status:** Design discussion, not an implementation plan &middot; **Tracking issue:** [#3215](https://github.com/Azure/data-api-builder/issues/3215) &middot; **Last updated:** 2026-09-10

## Decisions this document supports

1. **Properties:** What might we collect, what question does each property answer, and what are the costs of including it?
2. **Names and meanings:** What should those properties be called, what types and values should they have, and how do they map to platform-provided fields?
3. **Application Insights operating model:** Do we manage an Application Insights resource in Azure Monitor with a linked Log Analytics workspace, or onboard Application Insights-based collection to DataX? Could we start with the former and adopt the latter later? Compare setup, access, ownership, cost, and migration work.

**Design direction: reuse Application Insights-based collection rather than build a telemetry system.** The operating model, exact client/exporter, approved fields, consent, and rollout remain open. Candidate events are not assigned to implementation phases. The optional two-stage process concerns platform adoption, not which product features ship first.

**Current evaluation scope:** documentation and local-only exploration. No collector, Azure resource, shared table, or live telemetry POC has been created. Cost scenarios are illustrative, not deployment forecasts or quotes.

For the decision summary, see [DAB-managed Application Insights versus DataX: pros and cons](#dab-managed-application-insights-versus-datax-pros-and-cons) and [Optional two-stage adoption: Azure Monitor first, DataX later](#optional-two-stage-adoption-azure-monitor-first-datax-later).

For the consolidated collection list, see [Potential engine telemetry inventory](#potential-engine-telemetry-inventory), grouped as **Context**, **Configuration**, and **Observed usage** to match the demo.

## Design direction: Application Insights, not a custom telemetry system

### Why this choice makes sense

- **Custom facts do not require a custom backend.** Application Insights supports our own event names, properties, and measurements. DAB supplies its configuration and usage facts; generic HTTP instrumentation does not discover them. [Data model](https://learn.microsoft.com/en-us/azure/azure-monitor/app/data-model-complete)
- **Reuse delivery rather than build it.** An approved SDK/exporter supplies the service protocol, serialization, batching, and delivery mechanisms. We configure and test these behaviors instead of implementing our own transport and receiving service.
- **Storage and analysis already exist.** Workspace-based Application Insights provides managed ingestion and integration with Log Analytics/KQL, access controls, Workbooks, and alerts. The aggregate product questions discussed here do not establish a need for a separate analytics cluster.
- **The ecosystem is established.** Public .NET/OpenTelemetry documentation, supported tooling, and Azure integrations reduce technology and maintenance risk. This is maturity evidence, not a measured market-share or customer-count claim.
- **It fits the DataX onboarding pattern.** The supplied guide calls for privacy review, testing in a product-owned Application Insights resource, then a switch to a platform-provided collection destination. We can reuse this foundation whether or not we later adopt DataX.
- **A custom system adds ownership without a demonstrated requirement.** A DAB-owned ingestion service, bespoke transport, independently operated export pipeline, or owned ADX cluster would add security, capacity, schema, and on-call responsibilities. These are not active options in this design. DataX's existing managed pipeline is not a proposal for DAB to build one.

This does **not** eliminate DAB instrumentation work or approve SDK defaults. DAB still owns its property/event definitions, collection hooks, bounded usage aggregation where selected, consent/opt-out, allowlisting, lifecycle semantics, and tests. Custom events and a local test receiver are not a custom production telemetry system.

The exact supported .NET client/exporter and external-OSS ingestion/authentication path remain prerequisites. The public Azure Monitor OpenTelemetry path and the DataX guide's usual **1DS OpenTelemetry client** are not assumed interchangeable. If an approved path cannot meet our constraints, record a blocker for service/privacy review; do not silently introduce a DAB-built collector or distribute credentials to work around it.

### The two operating models

**Azure Monitor, Application Insights, and Log Analytics are parts of one service approach, not competing destinations.** Azure Monitor is the monitoring service (the "AMS" route discussed here); Application Insights supplies application-telemetry capabilities; the linked Log Analytics workspace stores logs and provides their query surface. Managing these Azure resources is not hosting the monitoring service ourselves.

| Layer | A. DAB-managed Azure Monitor / Log Analytics | B. DataX-managed product-data route |
| --- | --- | --- |
| Instrumentation | Approved Application Insights client/exporter plus DAB-defined facts | Approved client compatible with DataX; facts mapped to platform requirements |
| Collection destination | DAB-managed Application Insights resource | Product-owned Application Insights for testing, then platform-provided collection destination |
| Storage and processing | Linked Log Analytics workspace; Azure-managed ingestion | Platform-managed export/processing/classification into shared Kusto and requested downstream stores |
| Analysis | Application Insights/Log Analytics Logs, KQL, Workbooks, alerts, APIs | Authorized shared-Kusto queries/APIs; optional curated metrics and AdVent |
| Ownership difference | DAB manages resource policies, budget, and consumers; Azure operates the service | DataX operates the shared data pipeline; DAB maintains instrumentation, classifications, and consumers |

DataX adds **processing, governance, and shared analytics**; it is not needed merely to store custom properties. Its addition should address a concrete product need or organizational requirement. The supplied guide establishes the pattern, not DAB eligibility or a specific approved configuration. A handoff does not inherently require permanent dual-write. The [two-stage option](#optional-two-stage-adoption-azure-monitor-first-datax-later) retains model A until there is a justified, approved move to model B.

### Keep the data paths separate

- **Customer observability:** `runtime.telemetry` sinks chosen by customers. These do not automatically give the DAB team access to their data.
- **Application Name telemetry:** the database-visible fingerprint in [application-name-telemetry.md](application-name-telemetry.md). It describes pools and configuration, not requests or unique installations.
- **Engine product telemetry:** Application Insights-based collection to the agreed product destination, not silent forwarding of customer logs or traces. An isolated `ILogger`/OpenTelemetry exporter is possible; isolation, not a ban on those APIs, is the requirement. Selecting a platform does not enable collection or change the Application Name opt-out.

## Potential engine telemetry inventory

This is a consolidated collection of the candidates discussed in this document, using the three categories from [demo slide 04](../demos/engine-telemetry/slides/slide04.png). It is a selection menu, **not an approved payload or a claim that these fields are already implemented or automatically collected**. Names remain proposals; the detailed catalog below retains their types, sources, benefits, and costs. Platform support and useful precision vary by engine and execution mode.

Across all three categories, collect only approved states, categories, counts, or buckets. Secrets and customer content are excluded: no connection strings, credentials, SQL/GraphQL text, MCP arguments, request/response bodies, raw exception messages, arbitrary logs, customer-defined names, URLs, or paths. Higher-risk candidates are identified explicitly rather than treated as defaults.

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

Details: [Actual usage, lifecycle, and reliability candidates](#actual-usage-lifecycle-and-reliability-candidates) and [Supporting context: events, consent, and reliability](#supporting-context-events-consent-and-reliability). Choosing which candidates to implement is separate from the Azure Monitor-to-DataX platform-adoption stages.

## Property naming and representation

### Naming options

| Convention | Example | Benefits | Costs |
| --- | --- | --- | --- |
| Flat DAB-prefixed snake case | `dab_cfg_rest_state` | Identifies custom fields; easy dictionary/KQL access | Longer names; native-field mappings needed |
| Dotted attributes | `dab.config.rest.state` | Fits OpenTelemetry-style grouping | Bracket access in KQL; custom names are not automatically standard semantic conventions |
| Nested objects | `configuration.rest.state` | Natural JSON structure | May require string serialization and query-time parsing in some exporters |
| Platform-native names only | `AppVersion`, `Name` | Familiar platform tooling | Most DAB settings have no native counterpart; backend coupling |

**Working proposal:** flat `dab_` custom names and explicit native-field mappings. All names below are candidates. Shared-platform guidelines may require adapting them before approval. A backend change should not change a property's meaning.

- `dab_cfg_*` describes configuration; `dab_usage_*` describes observed activity in a defined window.
- Configured, effective-after-defaults, and actually exercised are different facts. Give them separate names if more than one is needed.
- Use explicit units: `_ms`, `_seconds`, `_bytes`, `_count`, `_bucket`. Never put a bucket label in a numeric `_count` property.
- Use closed values, never customer-defined names as property keys or values.
- **Config state:** `enabled`, `disabled`, `missing`, `unsupported`, `not_applicable`, `unknown`. Missing must not silently mean disabled.
- **Entity-use state:** `present`, `absent`, `no_entities`, `unsupported`, `unknown`. This distinguishes an empty configuration from an unused feature.
- Decide complete versus sparse snapshots per schema version; an old client omitting a new field is not evidence of disabled functionality.
- Categorical strings/booleans/sets belong in properties. Quantities such as durations and counts are candidates for numeric measurements or independent metrics. The .NET exporter may serialize numeric log attributes into properties instead of `Measurements`; pin and test the mapping rather than assume it. A numeric value can still be queried by casting, but that is a different representation.

### Reading the catalog

**AI source** is a capability classification, not collection approval: **Custom** means DAB must compute and emit the field; **Native slot** means Application Insights has a matching field but DAB may need to populate it; **Instrumentation-dependent** means coverage and semantics vary with SDK/instrumentation.

This is a review catalog, **not one payload containing every property**. Each approved event should have a closed subset and a documented counting scope.

### Envelope and correlation

| Proposed name | Type / meaning | Benefit of including | Cost or reason to omit | AI source |
| --- | --- | --- | --- | --- |
| `event_name` | Registered name | Distinguishes lifecycle and usage | Avoid one name per instance | Native slot: `Name` |
| `event_time_utc` | Event timestamp | Time-series analysis | Client clock skew; not receipt time | Native slot: `TimeGenerated` |
| `server_received_time_utc` | Timestamp assigned at an agreed receiving service, not by the client | Matches DataX's stated server-receipt timestamp requirement | Define which ingestion hop; do not substitute client event time or final Kusto ingestion time silently | Backend metadata; mapping needs agreement |
| `dab_schema_version` | Integer contract version | Interprets evolving fields | Compatibility maintenance | Custom |
| `dab_event_id` | Random ID reused on retries | Detects duplicate delivery | High cardinality; backend deduplication is not automatic | Custom; not a request/span ID |
| `dab_process_session_id` | Random ID per process run | Joins lifecycle and summary events | Pseudonymous linkage; cannot count returning installations | Custom; not user `SessionId` |
| `dab_config_epoch` | Process-local accepted-config sequence | Attributes activity without hashing config | Needs an authoritative config-commit boundary | Custom |
| `dab_synthetic` | Test-traffic boolean | Separates POC data | Not proof of event authenticity | Custom |

Event-only identity is least linkable. A process session enables correlation within a run. A **rotating ID** would survive restarts within a defined time window and change afterward; a **persistent installation ID** would survive indefinitely until reset/deletion. Neither is intrinsically required to store events in Application Insights. DataX's additional identity/eligibility expectations are discussed below; collecting either ID needs an explicit privacy and persistence decision.

### Product, platform, and data-source context

| Proposed name | Type / meaning | Benefit of including | Cost or reason to omit | AI source |
| --- | --- | --- | --- | --- |
| `dab_version` | Normalized product version | Adoption and regression comparisons | Not SDK or embedded host version | Native slot: `AppVersion`; set explicitly |
| `dab_release_channel` | Stable, preview, development, unknown | Preview adoption | Requires reliable build metadata | Custom |
| `dab_distribution` | OSS, hosted, other, unknown | Hosting-model trends | Normalize `DAB_APP_NAME_ENV`; never emit it raw | Custom |
| `dab_packaging` | Container, CLI tool, library, standalone, unknown | Distribution investment | Detection may need build input; no path probing | Custom |
| `dab_execution_mode` | Web, MCP stdio, embedded, unknown | Entry-mode adoption | Embedded semantics need definition | Custom |
| `dab_host_mode` | Development, production, missing | Configuration interpretation | Config mode is not proof of production use | Custom |
| `dab_config_delivery` | Startup, late configuration, hot reload | Configuration lifecycle | First late config may be readiness, not reload | Custom |
| `dab_os_family` | Windows, Linux, macOS, other, unknown | Platform support priorities | Must describe engine, not caller | Custom; not `ClientOS` |
| `dab_os_version_bucket` | Approved coarse OS version | Compatibility concentration | More fingerprinting; family may suffice | Instrumentation-dependent; normalize |
| `dab_process_architecture` | x64, arm64, x86, arm, other | Architecture support | Host and process can differ under emulation | Custom/runtime API |
| `dab_dotnet_runtime_version` | Normalized runtime version | Servicing/compatibility | More version combinations; no raw runtime description | Instrumentation-dependent |
| `dab_container_state` | Detected, not detected, unknown | Container adoption | Imperfect detection; no orchestrator metadata probing | Custom |
| `dab_hosting_kind` | Approved hosting category | Deployment investment | Unreliable detection, extra metadata access; lower priority | Custom/explicit host input |
| `dab_cloud_region` | Approved region/coarse geography | Residency and regional planning | Location disclosure; IP geography is not deployment region | Custom/explicit host input |
| `dab_database_types` | Sorted distinct engine set | Mixed-source configurations | Array representation needs mapping; configured is not queried | Custom |
| `dab_datasource_count_bucket` | Coarse active-source count | Multi-database scale | Fingerprinting; not installation count | Custom |
| `dab_datasource_files_present` | Multi-file configuration present | Existing fingerprint parity | File presence is not distinct database count | Custom |
| `dab_any_obo_state` | Any source enables OBO | Delegated-auth adoption | Engine-wide aggregation differs from the per-pool flag | Custom |
| `dab_any_session_context_state` | Any applicable source enables session context | Database-policy integration | Provider-specific; never emit claim values | Custom |

### Runtime configuration candidates

Every row requires DAB instrumentation; generic Application Insights HTTP monitoring does not discover these settings. Config state refers to the resolved object model unless a separately named effective-value definition is selected.

| Proposed name | Value / source meaning | Benefit of including | Cost or reason to omit |
| --- | --- | --- | --- |
| `dab_cfg_rest_state` | REST state | REST adoption | Enabled does not mean used |
| `dab_cfg_graphql_state` | GraphQL state | GraphQL adoption | Configured-versus-used distinction |
| `dab_cfg_mcp_state` | MCP state | MCP adoption | Separate API enablement from execution mode |
| `dab_cfg_key_vault_present` | Endpoint configured, boolean | Secret-management integration | Never endpoint, vault, or secret names |
| `dab_cfg_health_state` | Health state | Health feature adoption | Not a health outcome or uptime measurement |
| `dab_cfg_cache_state` | Runtime cache state | Cache adoption | Does not measure effectiveness |
| `dab_cfg_l2_cache_state` | L2 state | Distributed-cache adoption | Parent-cache interaction; no cache address |
| `dab_cfg_auto_entities_present` | Definitions present | Auto-configuration adoption | No patterns/names; not proof of successful expansion |
| `dab_cfg_rest_strict_body_state` | Strict-body setting | Compatibility/default decisions | Defaults and schema version affect interpretation |
| `dab_cfg_graphql_multiple_create_state` | Multiple-create setting | Batch-mutation adoption | Not batch-operation count |
| `dab_cfg_customer_otel_state` | Customer OTel enabled | Observability ecosystem | No endpoint, headers, or service name |
| `dab_cfg_customer_appinsights_state` | Customer Application Insights enabled | Integration adoption | Distinct from our backend; no connection string |
| `dab_cfg_customer_log_analytics_state` | Customer Log Analytics enabled | Sink adoption | No workspace, DCR, or table identifiers |
| `dab_cfg_customer_file_sink_state` | File sink enabled | Local logging adoption | No file path |
| `dab_cfg_auth_provider` | Unauthenticated, simulator, SWA, App Service, Entra ID, custom JWT, missing | Auth priorities | Normalize custom providers; no authority or claims |
| `dab_cfg_embeddings_state` | Embeddings state | Embedding capability adoption | Not an invocation count |
| `dab_cfg_embedding_endpoint_state` | Endpoint's enabled setting | Endpoint capability adoption | Enabled flag, not its URL or merely presence |

Host mode, data-source files, and OBO are in the preceding table. Together with these 17 rows, they account for **all 20 current Application Name runtime positions**. Source-engine context is `dab_database_types`; per-request protocol/object/role categories appear below as usage candidates instead of pool-key fields.

### Aggregate entity configuration candidates

These cover **all 14 current Application Name entity positions**. Each is custom entity-use state over the merged entity set. No entity names are emitted.

| Proposed name | Any entity... | Benefit of including | Cost or reason to omit |
| --- | --- | --- | --- |
| `dab_entity_any_table_state` | Uses a table | Source-shape adoption | Presence only, not names/scale |
| `dab_entity_any_view_state` | Uses a view | View support investment | Does not prove views were queried |
| `dab_entity_any_stored_procedure_state` | Uses a stored procedure | Procedure support investment | No procedure names or parameters |
| `dab_entity_any_persisted_document_state` | Uses an MCP persisted document | Future capability tracking | Current encoder reports unsupported; not false |
| `dab_entity_any_cache_state` | Enables caching | Entity cache adoption | Separate from runtime enablement |
| `dab_entity_any_rest_state` | Exposes REST | Entity exposure patterns | Overlap with runtime flag; not activity |
| `dab_entity_any_graphql_state` | Exposes GraphQL | Entity exposure patterns | Same overlap; not activity |
| `dab_entity_any_mcp_dml_state` | Exposes MCP DML tools | DML-tool adoption | No tool names or arguments |
| `dab_entity_any_mcp_custom_tool_state` | Exposes a custom tool | Custom-tool adoption | Names may reveal business context |
| `dab_entity_any_custom_role_state` | Uses custom roles | Authorization adoption | Presence/category only, never role names |
| `dab_entity_any_policy_state` | Has request/database policy | Policy adoption | No expressions/claims; splitting policy kinds is another candidate |
| `dab_entity_any_description_state` | Has a description | Documentation feature adoption | Lower analytical value; never description text |
| `dab_entity_any_relationship_state` | Defines relationships | Relationship adoption | No entities or field mappings |
| `dab_entity_any_parameter_embedding_state` | Uses parameter embedding | Future capability tracking | Current encoder reports unsupported; no parameter content |

Parity anchor: [ApplicationNameTelemetry source](../../src/Config/Telemetry/ApplicationNameTelemetry.cs#L1). Options are independent snapshot code, a shared pure semantic model with separate serializers, selected shared helpers, or sending the existing compact token as one property. Token reuse means embedding the token unchanged, not decoding it into named properties. It simplifies parity but makes querying harder. Preserve the existing Application Name format and pooling behavior under every option.

### Additional configuration and scale candidates

These are possible extensions, not claims that every setting is currently modeled. All require custom instrumentation.

| Proposed name | Meaning | Benefit of including | Cost or reason to omit |
| --- | --- | --- | --- |
| `dab_entity_count_bucket` | Coarse entity count | Deployment complexity | Fingerprinting; presence may suffice |
| `dab_cfg_default_page_size_bucket` | Pagination default | Default-setting decisions | Exact values can distinguish deployments |
| `dab_cfg_max_page_size_bucket` | Pagination maximum | Workload bounds | Configured versus effective matters |
| `dab_cfg_max_response_bytes_bucket` | Response bound | Large-response needs | Normalize units and defaults |
| `dab_cfg_cache_ttl_seconds_bucket` | Cache lifetime | Cache tuning | Multiple scopes; avoid per-entity values |
| `dab_cfg_query_timeout_seconds_bucket` | Applicable timeout | Timeout decisions | Provider-specific; modeling may differ |

### Actual usage, lifecycle, and reliability candidates

A request, a logical GraphQL operation, an MCP tool call, and a database attempt are different counting units. Define each before instrumentation; retries and multiple resolver calls must not inflate an API-request counter.

| Proposed name | Type / meaning | Benefit of including | Cost or reason to omit | AI source |
| --- | --- | --- | --- | --- |
| `dab_usage_api` | REST, GraphQL, MCP, unknown | Actual protocol mix | HTTP alone misses logical GraphQL/MCP boundaries | Custom |
| `dab_usage_operation` | Closed operation class | Read/write/execute mix | Taxonomy must be consistent | Custom |
| `dab_usage_database_type` | Supported engine enum | Exercised source types | Generic dependency type is not DAB engine type | Custom |
| `dab_usage_object_type` | Table, view, procedure, document, unknown | Actual object-category use | Extra resolution/instrumentation | Custom |
| `dab_usage_role_class` | Anonymous, authenticated, custom, unknown | Authorization usage | More sensitive segmentation; no names | Custom |
| `dab_usage_outcome` | Approved success/error category | Reliability | HTTP success may differ from logical success | Custom; related native outcome fields |
| `dab_usage_window_start_utc` | Window start | Interprets aggregation | Timing linkage; consider coarsening | Custom |
| `dab_usage_window_seconds` | Actual window duration | Comparable rates | Define reload, reset, and partial windows | Custom |
| `dab_usage_request_count` | Aggregate API requests | Traffic trends | Lost windows/sampling bias totals | HTTP instrumentation-dependent; custom summary |
| `dab_usage_operation_count` | Aggregate logical operations | Cross-protocol usage | Not interchangeable with HTTP count | Custom |
| `dab_usage_database_attempt_count` | Database attempts | Backend/retry insight | Not user-operation count | Instrumentation-dependent; custom summary |
| `dab_usage_count_capped` | Capping/overflow boolean | Avoids interpreting capped counts as exact | Needs defined capping behavior | Custom |
| `dab_usage_cache_hit_count` | Window hits | Cache effectiveness | Layer and denominator definition | Custom |
| `dab_usage_cache_miss_count` | Window misses | Cache effectiveness | Extra dimensions/instrumentation | Custom |
| `dab_usage_embedding_count` | Window embedding invocations | Actual feature use | No prompts, vectors, or text | Custom |
| `dab_usage_latency_bucket` | Aggregate operation-duration bucket | Latency distribution | Needs counts/boundaries; no exact percentile reconstruction | Instrumentation-dependent; custom aggregation |
| `dab_startup_duration_ms` | Defined initialization duration | Startup regressions | Dependency time/late-config waiting confound it | Custom boundary |
| `dab_uptime_seconds_bucket` | Coarse uptime | Long-running deployments | Define start point; recurring volume | Custom |
| `dab_failure_stage` | Fixed checkpoint | Actionable reliability categories | Early failures may be unobservable | Custom |
| `dab_failure_category` | Closed typed-outcome category | Reliability trends | No exception text parsing; fallback to unknown | Custom, not raw exception telemetry |
| `dab_shutdown_reason` | Closed host reason | Graceful lifecycle trends | Absence of stop is not proof of crash | Custom |
| `dab_process_cpu_bucket` | Coarse CPU utilization | Resource investment | Normalization/coverage and recurring volume | Instrumentation-dependent |
| `dab_process_memory_bytes_bucket` | Coarse process memory | Sizing trends | Container limits differ from process use | Instrumentation-dependent |
| `dab_telemetry_dropped_event_count` | Locally dropped events | Data quality | Cannot report all delivery failures; opt-out stays silent | Custom |

Presence-only flags, logarithmic buckets, capped counts, and sampling are alternatives to exact counts. For example, choose `dab_usage_operation_count_bucket` instead of `_count` if values are buckets. Review a few useful dimension combinations rather than sending their full Cartesian product.

### Higher-risk property choices

| Candidate | Analytical benefit | Reasons for caution / alternative |
| --- | --- | --- |
| `dab_installation_id` or rotating ID | Returning-installation/upgrade-path estimates | Persistent linkage, deletion/rotation rules, ephemeral-container ambiguity; process identity may suffice |
| `dab_config_fingerprint` | Deduplicates configuration | Hashing raw config does not anonymize secrets/unique settings; use local epochs or coarse flags |
| `dab_deployment_id` or customer/tenant identity | Correlates replicas or known customers | Materially changes privacy/consent; unnecessary for aggregate adoption |
| Exact entity/row counts, precise OS build or geography | Detailed segmentation | Rare combinations fingerprint workloads; compare buckets or omission |
| Individual request/trace IDs and events | Request correlation | Volume/linkage; aggregate summaries or customer observability may suffice |

Secrets/customer content remain safety exclusions, not lower-priority telemetry: no connection strings, tokens, query/GraphQL text, MCP arguments, request/response bodies, raw exception messages, arbitrary logs, customer names, URLs, or paths. Managed storage does not remove this responsibility. Random IDs can still be pseudonymous; absence of names does not establish anonymity.

## Application Insights: automatic collection versus DAB instrumentation

### What the SDK supplies, and what DAB adds

| Concern | Default Application Insights instrumentation | DAB-defined telemetry through the approved SDK/exporter |
| --- | --- | --- |
| Which facts exist | General request/dependency/log/runtime signals, depending on instrumentation | Approved configuration, lifecycle, and aggregate usage facts we implement |
| Property names | Mostly platform/instrumentation-defined | DAB custom names and native-field mappings; agreed DataX common fields when applicable |
| DAB configuration | Not discovered automatically | DAB computes the snapshot; no new backend required |
| Payload shape | Application Insights telemetry model | Service envelope plus properties/measurements; verify exporter type and size limits |
| Privacy | Collectors and enrichment require examination | Isolate and allowlist the product stream, including native context |
| Delivery | SDK/exporter mechanisms | Configure/test batching, retry, sampling, offline storage, opt-out, and flush rather than replace the transport |
| Query/UI | Built-in application views | Product-specific KQL/dashboards; user/session views are not installation counts |
| Service control | Azure owns the service model and quotas | DAB manages resource settings in model A or follows the DataX contract in model B |

**Main distinction:** we need custom instrumentation on an existing service, not a custom telemetry system. We retain DAB-specific product meaning while accepting the supported service model and limits in return for managed collection, storage, and querying.

For a new .NET POC, public Application Insights documentation provides a supported Azure Monitor OpenTelemetry path, including custom events via an isolated `ILogger` pipeline using `microsoft.custom_event.name`. The DataX guide instead calls its usual choice the **1DS OpenTelemetry client** and links [Geneva instrumentation guidance](https://eng.ms/docs/products/geneva/collect/instrument/overview). Confirm the supported .NET package, exporter, redistribution terms, authentication, and event mapping with the platform team before choosing. The names do not establish equivalence between the clients. The classic `TrackEvent` API is another SDK path; do not assume identical defaults or type mappings. [Public custom-event documentation](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-add-modify#send-custom-events)

### Native fields are not interchangeable with DAB fields

| Application Insights / Log Analytics field | Actual meaning | Design consequence |
| --- | --- | --- |
| `name` / `Name` | Event name | Use an approved DAB event name; not a separate name per instance |
| `application_Version` / `AppVersion` | Instrumented application's version | Explicitly set to DAB's product version if reused; an embedded host's version is different |
| `cloud_RoleName` / `AppRoleName` | Instrumented service/role | A constant such as DataApiBuilder is useful; not a customer's authorization role |
| `cloud_RoleInstance` / `AppRoleInstance` | Host/instance identity | May contain a machine name; suppress or replace according to approved policy |
| `client_OS` / `ClientOS` | Client-device context | Not a reliable substitute for `dab_os_family` |
| `session_Id` / `SessionId` | User interaction session context | Do not treat it as an engine process ID without an explicit mapping |
| `operation_Id` / `OperationId` | Distributed request/operation context | Not a process ID or stable retry-deduplication key |
| `sdkVersion` / `SDKVersion` | Telemetry SDK version | Not DAB version; useful for collection troubleshooting |
| `itemCount` / `ItemCount` | Occurrences represented by a sampled record | Not our aggregate operation count and not an installation count |
| `TenantId`, `_ResourceId`, `_SubscriptionId` in the workspace | Workspace/destination resource metadata | These do not identify the customer's DAB tenant or subscription |

The [data model](https://learn.microsoft.com/en-us/azure/azure-monitor/app/data-model-complete) and [AppEvents reference](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/appevents) define these fields. Their existence in a table does not mean every SDK populates them.

### Shape, precision, and enrichment to verify

- Custom property keys have a documented maximum length of 150 characters and values 8,192 characters. Total item size and exporter attribute limits also matter; test the complete candidate snapshot, not just one field.
- Keep simple scalar states portable. For `dab_database_types`, compare an array, a documented JSON-encoded string, or fixed per-engine flags. Do not assume a nested .NET object survives every exporter unchanged.
- The Application Insights model supports numeric custom measurements. Those measurements belong to their event and share its sampling fate. Independent metric instruments are a different signal; lack of trace sampling does not guarantee delivery or preserve a complete histogram distribution.
- Sampling, filtering, queue overflow, retries, and ingestion throttling all affect interpretation. `count()` counts stored rows; `sum(ItemCount)` can compensate for supported sampling, not missing deployments, dropped windows, or duplicate retries.
- SDK/resource enrichment can add host and cloud context. IP masking alone does not establish anonymity: Application Insights can derive location before discarding the IP. Review all native fields, automatic collectors, scopes, and diagnostic channels. [IP handling](https://learn.microsoft.com/en-us/azure/azure-monitor/app/ip-collection)
- Retry/offline storage and shutdown behavior are SDK-specific. If the design prefers memory-only buffering, verify that the selected exporter does not persist events to disk. [Exporter configuration](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-configuration)

## Two Application Insights operating models: setup and access

### A. DAB-managed Application Insights in Azure Monitor

**Path:** DAB instrumentation -> approved Application Insights ingestion path -> Application Insights component -> linked Log Analytics workspace -> KQL, portal, Workbooks, or query API.

Workspace-based Application Insights stores telemetry in the associated Log Analytics workspace; ingestion and retention are billed there. DAB would choose or obtain a governed resource/workspace and region. This is not an automatically provisioned workspace in each customer's subscription, nor a separate ADX cluster that DAB must size. [Resource and storage model](https://learn.microsoft.com/en-us/azure/azure-monitor/app/create-workspace-resource)

**What setup involves:** obtain an approved subscription/resource group, Application Insights component, and linked Log Analytics workspace, using new or approved existing resources. Agree region, separate test/production data, appropriate reader/publisher permissions, retention, budgets and ingestion controls. Configure the approved SDK/exporter for that resource and build product queries/Workbooks and any alerts needed. Azure runs the ingestion and storage service; DAB manages its resource configuration and data stewardship. A connection string identifies the resource but is not proof that every customer-hosted client is authorized to publish.

**Access:** engineers sign in with Microsoft Entra identities and appropriate resource/workspace permissions. Application Insights resource-context queries and Log Analytics workspace-context queries have different scopes. Grant readers and automation the least required data access; an ingestion connection string is not read authorization. [Access model](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/manage-access)

The two query surfaces expose the same underlying application telemetry under different names:

| Concept | Application Insights Logs | Log Analytics workspace Logs |
| --- | --- | --- |
| Custom events table | `customEvents` | `AppEvents` |
| Event name | `name` | `Name` |
| Event timestamp | `timestamp` | `TimeGenerated` |
| Custom fields | `customDimensions` | `Properties` |
| Event measurements | `customMeasurements` | `Measurements` |
| Sampling weight | `itemCount` | `ItemCount` |

Illustrative **workspace-context** query for the proposed custom property names; not executed, and the events do not exist yet:

```kusto
AppEvents
| where TimeGenerated >= ago(7d)
| where Name == "dab.engine.ready"
| summarize StoredRows = count()
    by DabVersion = tostring(Properties["dab_version"]),
       RestState = tostring(Properties["dab_cfg_rest_state"])
```

This reports stored rows, not customers or installations. A single Application Insights resource collecting many DAB deployments does not supply deployment identity automatically.

**What DAB still owns:** the event/property contract, opt-out, privacy filtering, ingestion eligibility, reader permissions, budgets, data retention choices, useful queries, and operational response for its instrumentation. Standard schemas and some workspace ingestion transformations are available, but arbitrary product-specific validation/deduplication is not automatically provided by Application Insights.

### B. Application Insights collection onboarded to DataX

This option is **a table/dataset on a platform-managed cluster**, not a DAB-owned cluster and not posting events to the AdVent website.

**Evidence:** the [Data Platform Overview](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/11338/Data-Platform-Overview) was read through authenticated repository access. The user supplied the text of [DataX Onboarding](https://aka.ms/dataxonboarding) on 2026-09-09. The following summarizes that supplied copy; its published revision, DAB's eligibility, and DAB-specific terms have not been independently confirmed. This supersedes the earlier lack of onboarding-page content.

#### Collection flow described by the guide

1. **Privacy review before telemetry implementation.** Share intended data types and the future catalog destination; complete the required review rather than treating downstream processing as approval to collect.
2. **Implement and validate in an Application Insights resource the product controls.** The guide says the 1DS OpenTelemetry client is usually used. Identify the exact supported .NET client/exporter before implementation.
3. **Onboard with DataX after validation.** Partners are expected to be ready to switch from their test resource's iKey to the Data Platform-provided key. The platform's setup includes an Application Insights resource, data export, catalog registration, and processing/storage configuration.
4. **Use platform-managed stores and optional metrics.** The guide describes Kusto for querying, Cosmos for longer-term/big-data processing, and cohort tags, engagement/retention metrics, and AdVent where requested.

Conceptually: **DAB -> approved client -> platform-provided Application Insights collection destination -> export/processing/classification -> shared Kusto; optionally Cosmos and curated metrics/AdVent.** This is a documented onboarding pattern, not a verified wire protocol for DAB. Do not infer that changing an iKey alone configures a modern exporter, endpoint, authentication, or permissions.

This handoff does **not** inherently require keeping two permanent production ingestion pipelines or writing a new DAB collector. The development resource can remain separate for testing. Any temporary overlap or platform-managed historical backfill needs explicit agreement and cost/privacy review; a new independent export service is not proposed. The VS Code extension-specific pipeline/key in the guide is not a destination for the DAB engine merely because it exposes MCP tools or can be launched from an editor.

#### What DataX provides, and what DAB still owns

| Guide-described service | Benefit | DAB responsibility or question |
| --- | --- | --- |
| Per-property classification, delinking/obfuscation, and DSR handling when applicable | Reuses a governed data-processing and privacy workflow | Owners classify fields correctly; client-side minimization and privacy review remain necessary |
| Kusto ingestion within hours | Shared queryable product data without an owned cluster | Specify required freshness; do not treat this as near-real-time observability or a negotiated SLA |
| Cosmos ingestion at end of day, retained up to 18 months | Longer-term storage and big-data processing | Confirm which datasets are enabled and their retention; this is not an 18-month hot-Kusto promise |
| Cohort tags, engagement/retention metrics, and AdVent presence | Shared business analytics | Request the metrics and tags needed; standard user-oriented metrics may not fit process-only identity |
| Latency, completeness, anomaly, and PII monitoring/cleanup | Platform operates pipeline-quality controls | Product team still monitors volume, fixes unexpected PII in the client promptly, and supports its instrumentation |

The guide expects partners to keep up with Application Insights releases/security fixes, monitor volume before shipping, throttle overly verbose clients/telemetry, and maintain catalog classifications after onboarding. Its processing of classified customer content is a safeguard, **not permission to emit customer content**.

#### Platform schema expectations versus proposed DAB names

| Guide field | Stated meaning | Mapping or decision for DAB |
| --- | --- | --- |
| `Id` | `MacAddressHash` and `devdeviceid` are described as standard | These imply device-oriented identity, unlike `dab_event_id` or `dab_process_session_id`. Ask whether a process-only/no-device-ID product is accepted; do not manufacture device identifiers to qualify |
| `Timestamp` | Time the event reached the server | Requires a defined receiver-side timestamp, such as logical `server_received_time_utc`; retain `event_time_utc` separately if needed |
| `EventName` | Name used extensively in queries | Map from the approved `event_name` without changing its semantics |
| `IsMicrosoftInternal` | Distinguishes internal users/test data for pipeline development and filtering | Define a safe, supported source. It is not automatically the same as `dab_synthetic`, development mode, or hosted distribution |
| `Properties`, `Measures` | Optional data bags | Map approved custom DAB fields and quantities; verify allowed types, names, and classification requirements |

The guide favors a simpler schema with fewer tables. One approved event table with registered event names and field subsets may be easier to onboard than a new table per feature; the platform must confirm the physical schema.

#### Eligibility, residency, and retention decisions

- **Organizational eligibility:** products outside DevDiv require additional discussion before the platform commits. Confirm how DAB is sponsored; do not assume eligibility from repository access.
- **Scale/identity condition:** the partner section states, "A minimum goal of 1000 users or must have EUPI in telemetry for onboarding." Clarify what this means for a server product measured in processes/deployments, whether it is a current requirement, and whether an exception is appropriate. **Do not add personal or persistent identifiers solely to satisfy an onboarding condition.**
- **US processing/storage:** the supplied FAQ states that data is stored and processed in the US. This is a concrete compatibility question for any residency or sovereign-cloud promise, not a freely selectable region in the shared route.
- **Retention:** the questionnaire asks for needs ranging from days to up to 18 months, and the service description distinguishes Cosmos retention from Kusto freshness. Ask for the actual raw-Kusto, curated-Kusto, Blob, and Cosmos policies, deletion/delinking behavior, and queryable history for DAB; do not assume all stores have the same retention.

**Remaining owner confirmations:** the supported OSS/.NET client and authentication path; process-only identity and internal/test classification; approved field/type mapping and enrichment; DAB eligibility; required latency/retention/residency; quota/chargeback; and current scheduling. Application Insights as part of the onboarding flow is now documented rather than speculative. See [technical onboarding steps](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/29934/Steps-for-Onboarding-New-Products) and [service-level guidance](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/18103/Data-Platform-Service-Levels) for follow-up; those linked procedures were not validated as part of reading the supplied copy.

Internal guidance and any resulting agreement should be reviewed before publishing this discussion externally. No onboarding or permission request has been submitted.

### Shared ingestion prerequisites for both models

- Arbitrary customer-hosted DAB processes do not have a Microsoft organizational managed identity or permission to write our private Kusto tables.
- Application Insights supports Microsoft Entra authenticated ingestion and can disable local/key-only authentication. When enforced, an authorized publisher identity is required. Do not assume the chosen subscription's policies allow a public resource identifier alone. [Authenticated ingestion](https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication)
- Do not embed reusable secrets, storage keys, shared workspace keys, or privileged Kusto credentials in an OSS binary. Read/query credentials never belong in the engine.
- Agree an existing service/platform-approved collection endpoint and client configuration. If direct submission is incompatible with policy, resolve that with the service owners. A DAB-built gateway is not an assumed fallback in this design.
- Publicly writable collection can receive spoofed clients, malformed payloads, and volume abuse. Review available service controls and client throttling; managed ingestion is not proof of genuine installations.
- DataX's destination handoff and managed export still require onboarding. An Application Insights connection string cannot simply target a Kusto table, and the guide's iKey wording is not a complete modern SDK configuration. Historical data movement must be agreed separately.
- Agree TLS, network/proxy behavior, residency, and sovereign-cloud routing. No fallback to a disallowed public-cloud destination.

## DAB-managed Application Insights versus DataX: pros and cons

This comparison starts with the same DAB analytical goals, not a guarantee of identical payloads: DataX's common schema and identity expectations need separate agreement. It compares **continuing to operate our own Application Insights resource/workspace** with **onboarding Application Insights-based collection to DataX's managed processing and stores**. The supplied guide explicitly describes testing in a product-owned resource and switching to the platform-provided destination. Neither choice inherently means writing an entire telemetry service from scratch.

### How established is each option?

**Application Insights is an established, mainstream option in the Azure monitoring ecosystem.** It is Azure Monitor's application performance monitoring service, with public SDK/exporter documentation, OpenTelemetry integration, KQL, dashboards, alerts, and Azure support. Supported App Service and Functions scenarios have built-in monitoring integration, and code-based instrumentation supports other hosting environments. This provides a broad ecosystem of tooling and transferable skills. It does not mean every language/hosting combination has identical automatic instrumentation. [Product overview](https://learn.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview), [supported integrations](https://learn.microsoft.com/en-us/azure/azure-monitor/app/codeless-overview), [Functions integration](https://learn.microsoft.com/en-us/azure/azure-functions/functions-monitoring).

**Evidence boundary:** this review does not establish a current customer count or market-share percentage. The documented integrations support the maturity/ecosystem assessment, not a numerical adoption claim or proof of suitability for our privacy requirements.

**The shared Data Platform is also an established internal product-telemetry route.** Its DataX/Nova documentation describes support for product families including Visual Studio, VS Code, and .NET, multiple telemetry SDKs/channels, and destinations including Kusto. The supplied [Telemetry & Data overview](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/1689/Telemetry-Data) links data access, Kusto learning material, product metrics, and privacy guidance. Its advantage is existing organizational processes and consumers, rather than the public ecosystem of a general-purpose Azure service. This does not establish DAB's eligibility or a particular SDK's approval.

### DAB-managed Azure Monitor advantages

- **Relatively straightforward independent evaluation.** Once an approved resource/workspace and permissions exist, the technical path is to configure a supported exporter, emit a synthetic custom event, and query it. There is no need to provision an ADX cluster or design its ingestion schema first.
- **An existing managed ingestion endpoint.** The SDK/exporter supplies serialization, batching, and delivery behavior through the approved service path; building a DAB receiving service is not part of this approach.
- **Our properties remain ours.** DAB-specific names, feature flags, and measurements can be added without relying on automatic HTTP instrumentation to discover them.
- **Analysis is available with the service.** KQL, Workbooks, alerts, and query APIs are available without onboarding a second analytics platform. Public documentation and supported tooling make the knowledge reusable outside this team.
- **No dedicated cluster capacity baseline.** A small telemetry stream can use pay-as-you-go workspace ingestion. The team controls its resource's permissions and retention within organizational policy and Azure's supported settings.

### DAB-managed Azure Monitor disadvantages

- **DAB still builds the product-specific instrumentation.** Generic monitoring cannot determine our effective configuration, logical operation counts, or approved failure categories. Users/session views do not automatically become installation-count dashboards.
- **General-purpose defaults need privacy review.** Automatic requests, dependencies, scopes, host context, diagnostics, sampling, and offline storage may not match a minimal product-usage design. A quick demo is not a safe production configuration.
- **DAB owns more of its data operation.** Azure runs the service, but our team must manage the resource budget, permissions, retention choices, data quality, queries, and instrumentation changes. Using it does not remove Microsoft privacy or telemetry-classification obligations.
- **Usage costs and service constraints remain.** Ingestion, retention, export, quotas, event-model limits, and sampling can affect both cost and analysis. It is not inherently cheaper than a shared platform; the latter's chargeback is not yet known.
- **External OSS ingestion is a separate approval question.** If local/key-only ingestion is disallowed and customer processes have no authorized publisher identity, resolve a supported collection path with the service owners before rollout. Do not work around this with distributed secrets or an unreviewed custom receiver.

### DataX advantages

- **Reuse product-telemetry operations.** The guide lists classification-based delinking/obfuscation, applicable DSR processing, Kusto/Cosmos delivery, PII cleanup, and latency/completeness/anomaly monitoring. DAB does not have to assemble those capabilities independently; their applicability still depends on the approved fields and onboarding agreement.
- **No DAB-owned cluster administration.** Capacity, shared infrastructure, and its operational response belong to the platform owners. DAB still owns its event semantics, product code, and data stewardship.
- **Fits existing internal analysis practices.** The guide offers cohort/workload tagging, engagement and retention metrics, and AdVent presence in addition to Kusto access. Select only the analytics that fit DAB's approved identity and counting model.
- **A documented Application Insights handoff.** Partners develop/test in their own resource and prepare to switch to a platform-provided destination. That can reuse instrumentation rather than require a new DAB collector. Confirm the exact 1DS/.NET client and modern exporter configuration, not just the destination identifier.
- **Established governance is reusable.** Event/property ownership and classification provide a maintained process for handling telemetry changes. That is valuable infrastructure, even though it introduces onboarding tasks.

### DataX disadvantages

- **More cross-team coordination before first usable data.** Agree product eligibility, source/channel, expected volumes, destination mapping, schema/classification, and reader access. Allocating a table alone does not connect an OSS process to it.
- **Less unilateral control.** Retention, quotas, processing/enrichment rules, schema changes, and support expectations must fit platform guidelines. New or changed fields may require review rather than only a client release.
- **Device-oriented identity and eligibility are a real design question.** The guide names `MacAddressHash`/`devdeviceid` as standard IDs and includes a "1000 users or ... EUPI" condition. These do not automatically fit process-only engine telemetry. Seek clarification or an exception rather than add personal data solely to qualify.
- **Shared storage is not an unrestricted residency choice.** The supplied FAQ states US processing/storage. Kusto arrival is described in hours, with end-of-day Cosmos delivery and longer-term retention up to 18 months. These may fit product trends, but not all residency or near-real-time scenarios.
- **Client obligations remain.** Partners must keep telemetry dependencies patched, mitigate unexpected PII in the client promptly, classify fields, monitor volume before release, and support throttling. DataX does not take ownership of all product telemetry correctness or security work.
- **Shared does not mean free or automatically accessible.** Obtain the cost/chargeback agreement and required reader/service-principal access. Shared capacity and platform change schedules are dependencies rather than resources DAB controls directly.

### How hard would setup be for DAB?

The assessments below separate engineering work from permission/onboarding waiting time. Application Insights effort remains a planning judgment; DataX also supplies a variable onboarding estimate, summarized below. Neither is a measured DAB implementation schedule or a negotiated SLA.

| Work item | A. DAB-managed Application Insights / Log Analytics | B. DataX onboarding |
| --- | --- | --- |
| Define events and properties | Same DAB design/instrumentation work in both options | Same work, plus mapping to required platform conventions |
| Local serialization POC | Low technical friction with a supported exporter and synthetic data | Validate the guide's usual 1DS client choice for .NET; a compatible POC can inform both choices |
| First cloud event and query | Product-controlled resource after Azure permissions, billing, and ingestion policy are approved | The guide starts with the same product-owned Application Insights validation, then adds platform handoff and export/processing before shared-Kusto queries |
| Reader access and governance | Configure appropriate resource/workspace data access, retention, and product privacy review | Follow platform data-access and event/property classification processes; product privacy review still applies |
| Ship in external OSS deployments | Substantial validation: consent, privacy, publisher authentication, bounded delivery, and supported SDK behavior | The same validation, plus agreement on OSS client distribution, common schema, identity eligibility, US residency, and platform configuration |
| Ongoing operations | DAB manages its resource/data policies and consumers; Azure manages the service | Platform manages shared infrastructure; DAB maintains its schema/classification and consumers within agreed processes |

**Typical setup tasks, not instructions to execute now:**

1. **Application Insights:** complete the required privacy review; obtain a governed resource and linked workspace; agree ingress/authentication; configure an isolated exporter and custom event; verify fields and KQL access; then validate consent, budget, and delivery behavior for release.
2. **DataX:** engage privacy/platform owners early; develop and validate telemetry in our own Application Insights resource using an agreed client; submit the questionnaire; agree schema/identity and data policies; coordinate the platform-provided resource/destination, export, catalog, and processing; switch the approved client configuration; validate raw Kusto data and separately requested Cosmos/metrics/AdVent outputs.

#### Questionnaire inputs to prepare

The supplied guide asks for the following. Prepare answers for discussion; do not submit a request as part of this documentation task.

| Input | DAB decision/evidence needed |
| --- | --- |
| Team, organization, and owner | Identify the sponsor; products outside DevDiv need additional discussion |
| Product objectives/goals/KRs | Explain which decisions the telemetry will support |
| Expected events per day | Refine the illustrative volumes and include request/summary frequency and bursts |
| Required date/ship timeline | Separate code readiness, privacy approval, and platform onboarding |
| Privacy review status | Engage the privacy champion before implementation and record sign-off requirements |
| Reason for DataX | Specify needed governance, shared metrics, or permitted joins; joining identity data is not an automatic goal |
| Customer content and PII | Explicit field inventory/classification; customer content must not be logged |
| Freshness and retention | Explain any need faster than next-day analysis; identify history needed in each store |
| Schema and table types | Identify custom events, measurements, or other types and required common-field mappings; fewer tables are preferred |
| AdVent, engagement metrics, workload tags | State which outputs are needed instead of assuming every capability is enabled |

#### Guide estimates, not a committed DAB schedule

The supplied guide summarizes **about 10 days for data to flow into Kusto**, conditional on fast collaboration and with explicitly large variability. It does not specify business versus calendar days. This is not a ten-day estimate for designing, privacy-reviewing, implementing, testing, and shipping DAB telemetry.

The breakdown mentions initial infrastructure/resource/export/catalog setup, roughly a week for pipeline setup, and Kusto egress work. It also lists about a week for Cosmos egress/metrics, three days for All Events View/AdVent, and two days for initial catalog classification with the owner. **Do not mechanically add these overlapping or optional entries or treat every output as covered by the Kusto summary.** Confirm the critical path, prerequisites, and current team availability.

The guide also says new Collector++ products are not generally being onboarded, except potentially case by case. Treat that as a source-channel constraint to confirm, not evidence that a chosen .NET/1DS path is approved or blocked. No comparable Application Insights production-delivery estimate has been established in this review.

### Reader and automation access: confirmed from repository documentation

The [access guide repository source](https://devdiv.visualstudio.com/DevDiv/_git/DSI-Curation?path=%2FAccessWiki.md) was read through authenticated repository access on 2026-09-09. This verifies the source's requirements, not that its revision is identical to the currently published eng.ms page.

- **Human readers:** the guide requires privacy training, Kusto best-practice guidance, and an access-group request with a specific business justification and training attestation. Additional approvals and data-use restrictions depend on the organization, dataset, and access context.
- **Automated consumers:** managed identities, service principals, and applications use a separate access-request process. The request describes the purpose, responsible owners, identity, requested database/table permissions, and how the service prevents exposing data to unauthorized downstream users.
- **Authentication:** the guide recommends managed identity and describes federation/certificate alternatives for service principals; it states that new secret-based access will not be approved. This concerns access to platform data, not a requirement that arbitrary customer-hosted DAB processes authenticate as Microsoft employees or services.
- **Permission scope:** reading, ingesting, and administering data are distinct permissions. A reader entitlement does not provision a product's table or approve its telemetry ingestion route.

For setup planning, **documentation sign-in, analyst access, automated-consumer access, and product ingestion onboarding are separate tasks**. VPN connectivity or successful sign-in to eng.ms does not grant access to Kusto data. No access requests or permission changes have been made as part of this design work.

The [telemetry catalog FAQ](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/2247/FAQ-Cataloging-Telemetry-Data) and repository access guide were read directly. The user-supplied copy of [the migrated DataX onboarding guide](https://aka.ms/dataxonboarding), provided on 2026-09-09, now supplies the collection flow, questionnaire, partner expectations, and estimates above. Browser access was not independently completed; the pasted guide is the evidence source. DAB eligibility, the exact supported OSS client/authentication configuration, identity exception if needed, quotas/costs, and actual scheduling still require owner agreement.

### What would favor one choice?

- Favor **DAB-managed Azure Monitor / Log Analytics** when a self-contained telemetry resource and direct KQL feedback meet the product needs and all required policies. It offers independent operation without DataX onboarding, not an exemption from privacy/security work.
- Favor **DataX's Application Insights-based onboarding** when its shared governance and analytics are useful and DAB can agree on identity, eligibility, US residency, and freshness. Additional onboarding can reduce long-term platform ownership, but does not remove client responsibilities.
- **Testing in our own Application Insights resource preserves both choices.** The guide explicitly expects this before the platform handoff. Any temporary production coexistence needs a bounded transition plan; permanent dual-write and independently built exports are not assumed.
- **Require a reason to add DataX.** If the approved need is only custom events, configuration/usage trends, and KQL, the Azure Monitor route may be sufficient. DataX should add a required managed data-handling capability, approved shared-data analysis, standard metrics, or meaningful operational benefit that outweighs its coordination and migration costs.

Application Insights is the common design direction. Choosing model A, model B, or the optional progression from A to B remains a team decision; event/property phasing is still open. The highest-value next inputs are DAB-specific eligibility/identity and SDK/authentication decisions, followed by residency, freshness, retention, and cost agreement. No implementation or provisioning is authorized by this document.

## Optional two-stage adoption: Azure Monitor first, DataX later

**Proposal for discussion, not an automatic migration commitment:** establish Application Insights in Azure Monitor/Log Analytics as an independently useful operating model, then add DataX only when its benefits justify the additional onboarding and transition work. **Remaining at stage 1 is a valid long-term outcome** if it meets product and policy requirements.

This differs from the guide's basic "test in your own Application Insights, then onboard" sequence: stage 1 could be an approved, continuing product telemetry operation rather than only a test resource. The guide does not promise automatic migration of an established production population or its history. Both stages remain future possibilities; the current task is documentation only.

### Stage 1: make Application Insights in Azure Monitor useful on its own

- Complete the privacy/security and external-OSS ingestion decisions before implementation or rollout; DataX is not where those obligations begin.
- Configure the approved Application Insights resource and linked Log Analytics workspace, with appropriate test/production separation, read/publish access, retention, budgets, and volume controls.
- Implement only the approved DAB fields/events through a supported client/exporter, isolated from customer observability. Event/feature scope is decided separately from this platform stage.
- Validate collection and opt-out, stored field names/types, sampling/loss behavior, lifecycle counting, and the queries/Workbooks needed for product decisions.
- Gather evidence about analytical usefulness, SDK overhead, event volume, billable size, freshness, and ongoing resource/data-stewardship work.

The outcome is usable approved reporting, not merely a successful SDK send. If this meets the needs and policies, no DataX migration is required.

### Stage 2: onboard to DataX when there is a demonstrated need

- Identify the specific additional value: managed classification/delinking/DSR workflows where required, approved joins with shared datasets, standard engagement/retention metrics, or platform-operated data-quality monitoring.
- Obtain DAB-specific agreement on eligibility, process-only identity or any separately reviewed change, the exact 1DS/.NET client, schema mappings, internal/test flags, authentication, US residency, per-store retention, freshness, and chargeback.
- Follow DataX's documented resource/destination handoff and managed export/processing setup. Adapt the supported client configuration and queries as necessary; do not assume this is only an iKey substitution.
- Validate shared-Kusto results and any requested Cosmos, metrics, and AdVent outputs before making them the reporting source of record.

Stage 2 is **adoption of an existing managed service**, not a new DAB collector, transport, or independent export pipeline. Platform onboarding remains optional unless an applicable organizational requirement makes it necessary; such a requirement must be addressed before stage 1 rollout, not deferred.

### Benefits and costs of staging the decision

| Potential benefit | Cost or risk to weigh |
| --- | --- |
| Earlier useful feedback without waiting for DataX-specific analytics | Stage 1 still needs all required privacy/security approvals and resource ownership |
| Choose fields and reporting from observed value, volume, and costs | Some SDK, schema, or dashboard work may need adaptation later |
| Avoid onboarding complexity if Azure Monitor remains sufficient | Shared metrics, permitted joins, and DataX-managed workflows are unavailable until onboarding |
| Reuse Application Insights instrumentation and the guide's test-first pattern | 1DS/client requirements, standard device IDs, and server-timestamp semantics could prevent a simple handoff |
| Decide with a clearer business case for DataX | Later migration introduces historical-data gaps, client rollout work, and possible overlap costs |
| Keep the initial operating model self-contained | Eligibility, platform capacity, costs, and policy can change before a later onboarding request |

Staging reduces the need to commit to DataX immediately; it does not guarantee less total work. Early, noncommittal platform consultation can identify incompatibilities without committing to stage 2 or sending telemetry.

### Decision gate: when would stage 2 make sense?

Proceed only when the team can answer these questions with evidence:

1. **Added value:** which DataX capability is needed beyond custom events, KQL, Workbooks, and alerts already available in Azure Monitor? What is the cost of meeting that need while remaining at stage 1?
2. **Compatibility:** can the agreed identity and field contract satisfy DataX without collecting extra personal data just to qualify? Do eligibility, US processing, latency, and per-store retention fit DAB's commitments?
3. **Operational case:** who owns onboarding, classifications, client fixes, data quality, access, and consumers? Are platform support/freshness commitments sufficient?
4. **Total cost:** do expected benefits outweigh onboarding, SDK/query adaptation, historical-data handling, possible overlap, and ongoing chargeback?
5. **Migration readiness:** is there an approved cutover, validation, history, older-client, and rollback plan?

If the answers are no, remain with Azure Monitor when compliant, or stop collection until a required policy issue is resolved. Do not add device/customer identifiers or relax consent to make the transition possible.

### Preserve the option without building a migration platform

- Keep DAB event semantics, names, and schema versions stable and map them explicitly to the supported exporter and DataX contract. Never relabel process identity as device identity or event time as receiver time.
- Keep collection-destination configuration separate from the code that computes facts, using supported SDK configuration. Do not introduce arbitrary remote endpoints, a home-grown transport, or a remote-command channel.
- Check the likely .NET client/redistribution path and required fields with DataX early. Do not collect speculative "future DataX" personal identifiers in stage 1.
- Document field meanings, units, missing values, aggregation windows, and data-loss assumptions. Maintain a small query/test set that can later be mapped from `AppEvents`/`Properties` to the agreed DataX schema.
- Record stage 1 retention and ownership so the team knows which history would still exist if migration is considered. Migration compatibility is not a reason to retain unnecessary data indefinitely.

### Transition work that must not be assumed away

| Area | Required decision |
| --- | --- |
| Deployed clients | A resource change does not update installed OSS versions. Determine the supported release/configuration mechanism and how long old clients may continue reporting to the old resource |
| Historical records | Existing workspace data does not automatically move. Choose leaving history under its approved retention or an explicitly agreed platform-managed backfill; assess scope, privacy, cost, and comparability |
| Field and query mapping | Confirm `Name`/`EventName`, event/receipt timestamps, IDs, internal/test flags, numeric fields, enrichment, and sampling. Reuse analytical definitions, not assumptions of identical physical schemas |
| Cutover and overlap | Prefer an agreed destination handoff. Permanent dual-write is not the default. Any temporary overlap needs approved duration, data destinations, cost bounds, and duplicate-count handling |
| Validation | Use synthetic test data first, verify reader/automation access and raw/curated results, and reconcile only the agreed population and time window before switching reports |
| Rollback | Define when clients/reports can use the prior approved resource again, who owns it during transition, and how loss/duplicates are reported. Returning to a disallowed location or identity model is not a valid rollback |
| Completion | Set criteria for ending coexistence, retiring old reporting, and retaining/deleting old data according to policy. Do not leave two unmanaged production operations indefinitely |

The guide's variable **about 10 days to Kusto** estimate is not an estimate for all this migration work. There is no stage-2 date or commitment until the benefits, dependencies, and approvals are established.

## Benefits and costs under illustrative workloads

### Volume model, not a price quote

Use the same workload when comparing the two operating models. For illustration, let $N$ be average concurrently active processes, $S$ successful starts per process per day, $H$ periodic events per process per day, and $B$ average uncompressed bytes per event:

$$
E_{day} = N(S + H), \qquad V_{day} = E_{day}B / 10^9
$$

Assume **2 KiB (2,048 bytes) per event** and a 30-day month. These are planning scenarios, not measured DAB populations, recommended intervals, or release phases. A periodic event can combine a heartbeat and summary; separate events would increase the count.

| Scenario | Active processes | Events per process/day | Events/day | Uncompressed GB/day | Uncompressed GB/30 days |
| --- | --- | --- | --- | --- | --- |
| Low | 1,000 | One start, no periodic event | 1,000 | 0.002048 | 0.06144 |
| Medium | 10,000 | One start + 24 hourly events | 250,000 | 0.512 | 15.36 |
| High | 100,000 | One start + 96 quarter-hourly events | 9,700,000 | 19.8656 | 595.968 |

An 8 KiB average event multiplies these volumes by four. Request-level automatic collection scales with request/dependency/log activity instead of just processes and intervals; it may be much larger than a manually summarized stream.

These are **payload volumes, not Azure bills**. Application Insights envelopes/enrichment and Log Analytics' billable-size calculation differ from raw JSON size; a later sandbox can inspect `_BilledSize`. DataX's processing and retained copies may have a different chargeback basis. Include retries, bursts, extra events, retention, and any approved migration overlap separately.

### What appears on the bill or in the ownership budget?

| Cost/control area | A. DAB-managed Application Insights / Log Analytics | B. DataX onboarding |
| --- | --- | --- |
| Low-volume entry cost | Pay-as-you-go ingestion; no dedicated cluster to size | No DAB-dedicated cluster; shared chargeback/minimums unconfirmed |
| Growth | Billable ingested GB, quotas, and any commitment tier | Platform quotas, marginal capacity, chargeback, and volume-change approvals |
| Retention | Workspace/table retention and long-term retrieval costs | Guide describes up to 18 months for longer-term storage; agree per-store policies, not an assumed hot-Kusto retention |
| Queries | Analytics-plan interactive queries are not billed per GB scanned; other plans/features differ | Shared quotas, access rules, and prioritization |
| Data movement | No separate production export pipeline is proposed | Platform resource/export/processing is part of onboarding; confirm who pays and what copies persist |
| Operations | DAB manages resource settings, budget, data quality, and consumers; Azure operates the service | DataX operates the shared pipeline; DAB maintains fields, classifications, client patches, and consumers |
| Migration | Not required if model A remains sufficient | Add SDK/config/query adaptation, approved backfill if any, and temporary overlap to the onboarding cost |
| Flexibility | Supported resource settings within Azure and organizational policy | Supported settings within the shared platform contract and change lead times |

`AppEvents` does not support the Basic or Auxiliary table plans in the current [table reference](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/appevents); do not price custom Application Insights events as the cheapest arbitrary log-table plan.

For a numeric comparison, obtain region-specific Azure rates/discounts and the DataX cost/chargeback agreement. Test **30 versus 90 days of interactive analysis and one year of retained history** as separate, unapproved scenarios. Include engineering effort, on-call/data-stewardship time, and any transition costs. DataX is not proven cheaper, more expensive, or free from the guide alone.

Sources: [Azure Monitor cost model](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/cost-logs) and the supplied [DataX onboarding guide](https://aka.ms/dataxonboarding), which describes services but does not provide a DAB price quote.

## POC plan: learn what Application Insights actually supplies

**Status: proposed, documentation/local-only. No POC code or cloud telemetry has been run.** The purpose is to verify the selected Application Insights approach and inform its operating model, not implement engine telemetry or commit to DataX adoption.

The supplied guide requires privacy review before telemetry implementation and recommends validating in a product-owned Application Insights instance before DataX onboarding. That sequence supports a later sandbox experiment but **does not change the current documentation/local-only authorization**. Resolve the 1DS versus Azure Monitor .NET client choice and required review first; no identifiers should be added merely to satisfy DataX onboarding.

If approved for a local experiment, use a small isolated .NET harness with synthetic config/events, a pinned supported exporter, and an in-memory or loopback capture path. Keep it outside the DAB request pipeline. No real connection strings, customer databases, public ingestion requests, or Azure resources are needed for the local comparison.

| Experiment | Compare / inspect | Evidence wanted |
| --- | --- | --- |
| Native baseline | Installed automatic instrumentation and resource detectors, using synthetic requests/workload | What is emitted without DAB-specific code; which host/context fields appear; no assumption that DAB flags are discovered |
| Custom event | Approved candidate subset with explicit DAB version and names | All intended keys, exact spellings, and values survive serialization |
| Type mapping | Booleans, config states, numeric values, missing values, a source-type set | Actual `Properties` versus `Measurements` mapping; whether arrays/nested objects need encoding |
| DataX contract mapping | Synthetic `Id`, receiver `Timestamp`, `EventName`, `IsMicrosoftInternal`, and property/measure bags | Show mappings and unresolved meanings; do not equate process identity with device identity, event time with receipt time, or test traffic with an internal user |
| Full snapshot | All candidate snapshot keys plus normal envelope overhead | Size and attribute-count behavior; no silent truncation/drop; this is not approval to collect all fields |
| Isolation/privacy | Canary-filled synthetic config and workload, before and after allowlisting | No canaries, customer diagnostic stream, machine names, or unapproved enriched context in the approved payload |
| Counts and buffering | Known event set, sampling configurations, retry, cancellation, and process exit | Local attempted/captured/dropped counts and bounded shutdown; no promise of exactly-once delivery |
| Disabled state | Consent disabled before initialization | No event construction, export attempts, or replay of previously buffered data |

A local capture can verify client behavior and serialized mapping, **not** Azure ingestion, server enrichment, query permissions, end-to-end freshness, retention, or actual billing. If those remain decision-critical, request an approved disposable/existing sandbox later, send synthetic events only, inspect `AppEvents` and `customEvents`, verify reader access and `_BilledSize`, and document cleanup and cost limits before running it.

The output should be a field comparison: **wanted -> native without DAB code -> explicitly supplied -> serialized -> stored -> queryable**, with any untested steps labeled. Preserve package versions and configuration so SDK defaults are reproducible.

## Supporting context: events, consent, and reliability

These candidates determine when properties are useful; none is assigned to a phase. `dab.engine.ready` is the working name for the earlier draft's successful `dab.engine.started` concept, to distinguish engine readiness from process entry.

| Candidate event | Useful property groups | Tradeoff for discussion |
| --- | --- | --- |
| `dab.engine.process_started` | Product/platform, correlation | Launch denominator; consent must be available before config |
| `dab.engine.ready` | Initial configuration snapshot, startup duration | Successful initialization, not unique deployment count |
| `dab.engine.startup_failed` | Closed failure stage/category | Cannot observe every early crash; never exception text |
| `dab.engine.configuration_changed` | Config epoch and snapshot | Needs an authoritative successful-commit boundary |
| `dab.engine.configuration_change_failed` | Closed stage/category | Reliability value versus extra instrumentation |
| `dab.engine.heartbeat` | Process ID, uptime, optional current snapshot | Liveness evidence, recurring volume, missing-interval ambiguity |
| `dab.engine.usage_summary` | Window and approved aggregate dimensions/counts | Actual feature use, but cardinality/aggregation and sampling need definition |
| `dab.engine.stopped` | Duration and closed stop reason | Supplemental: graceful exits are not representative of all terminations |

`Startup.PerformOnConfigChangeAsync` is a candidate config-readiness boundary for normal and late-config initialization, not proof that every host startup action is complete. Hot reload needs its own confirmed commit boundary. MCP stdio must keep stdout protocol-only; ephemeral/embedded modes may warrant different consent or cadence. Multi-source and shared-host aggregation must not introduce tenant identifiers or silently inherit consent.

Consent defaults remain undecided: explicit opt-in, opt-out, or a hosted-managed model. The existing `DAB_TELEMETRY_APPNAME_OPT_OUT` stays a compatibility contract. A new `DAB_TELEMETRY_ENGINE_OPT_OUT` and an umbrella `DAB_TELEMETRY_OPT_OUT` are naming candidates, **not implemented settings**. Define precedence and re-evaluation before use; disabling a customer sink is not a product-telemetry choice. An offline status/payload-preview tool could improve transparency.

Across all destinations, approve bounds for event size, counters, queue memory, retry lifetime, and graceful flush. No network wait on startup/request paths; telemetry failure must not fail DAB. Memory-only client buffering is a candidate policy that must be reconciled with exporter defaults. Suppress instrumentation of telemetry transport to avoid leaking it into customer traces. A server-side drop switch stops downstream retention, not already deployed clients' network attempts.

Remote experiments/commands remain a separate, questionable extension of a measurement channel, not an assumed requirement or hidden benefit of a collector. Source-build eligibility, sovereign routing, retention/deletion, access auditing, and aggregation thresholds need product/privacy/platform agreement before rollout.

## Questions to resolve in the design meeting

| Decision | Evidence to bring / question to answer |
| --- | --- |
| Candidate property list | Which rows answer a real product question? Which add little value or too much identifying detail? |
| Property names and types | Accept `dab_` flat names? Which values are native-mapped, custom scalar states, numeric measurements, or buckets? |
| Configuration versus usage | Is configured capability enough, or do we need bounded activity summaries? What exactly is one operation? |
| Identity | Event-only, process, rotating, or installation identity? What valid counting claims follow? |
| Application Insights client | Which approved .NET SDK/exporter and configuration meet our field, privacy, delivery, and external-OSS needs? |
| Shared-platform eligibility and identity | How do DAB's organization, server-oriented population, and process-only identity fit the stated 1000-user/EUPI condition and standard device IDs? Is an exception needed? |
| Shared-platform client and handoff | Which .NET client implements the guide's 1DS path? What resource/endpoint/authentication settings replace the guide's iKey shorthand? |
| Shared-platform schema and residency | Agree `Id`, receipt `Timestamp`, `EventName`, `IsMicrosoftInternal`, field bags, US processing/storage, and per-store retention |
| Shared-platform delivery and cost | Is hours/next-day freshness sufficient? Confirm the variable 10-day onboarding estimate, requested downstream outputs, quotas, and chargeback |
| DataX value | Which required governance, shared analysis, standard metrics, or operating benefit justifies adding DataX rather than remaining in Azure Monitor? |
| Ingestion route | Can external OSS clients use an existing approved collection path without embedded secrets? Resolve any blocker with the service owners, not a new DAB telemetry system |
| Query/access experience | Can the intended engineers and automation query raw and curated data? Does AdVent need additional onboarding? |
| POC scope | Which unanswered facts require a local harness, and which later require a separately approved cloud sandbox? |
| Optional two-stage adoption | Adopt DataX during initial onboarding, later when justified, or stay with Azure Monitor? Review migration gates, history, older clients, rollback, and costs |
| Feature sequencing | Choose event/property implementation phases separately; this document does not assign them |

## References and evidence status

- [#3215 - Telemetry in Engine](https://github.com/Azure/data-api-builder/issues/3215), [#3683 - Application Name telemetry](https://github.com/Azure/data-api-builder/pull/3683), and [application-name-telemetry.md](application-name-telemetry.md): existing feature context, not approval of outbound engine collection.
- [Application Insights overview](https://learn.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview), [supported integrations](https://learn.microsoft.com/en-us/azure/azure-monitor/app/codeless-overview), and [Functions monitoring](https://learn.microsoft.com/en-us/azure/azure-functions/functions-monitoring): ecosystem/maturity evidence, not a customer-count or market-share estimate.
- [Application Insights data model](https://learn.microsoft.com/en-us/azure/azure-monitor/app/data-model-complete), [custom instrumentation](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-add-modify), and [AppEvents schema](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/appevents): documented capabilities; pinned SDK behavior still needs verification.
- [Application Insights resources](https://learn.microsoft.com/en-us/azure/azure-monitor/app/create-workspace-resource), [Log Analytics access](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/manage-access), and [ingestion authentication](https://learn.microsoft.com/en-us/azure/azure-monitor/app/azure-ad-authentication): destination, querying, and authorization model.
- [Azure Monitor cost model](https://learn.microsoft.com/en-us/azure/azure-monitor/logs/cost-logs): cost drivers, not a DAB quote. DataX costs require a platform agreement.
- [Data Platform Overview](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/11338/Data-Platform-Overview): internal reference, read through authenticated repository access. AdVent is an analysis application; the platform is broader. DAB-specific onboarding terms and pricing remain unconfirmed.
- [Telemetry & Data overview](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/1689/Telemetry-Data), [telemetry catalog FAQ](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/2247/FAQ-Cataloging-Telemetry-Data), and [DataX onboarding entry](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/11521/DataX-Onboarding): authenticated wiki guidance.
- [DataX Onboarding](https://aka.ms/dataxonboarding): page text supplied by the user on 2026-09-09, not a newly authenticated eng.ms retrieval. Source for the Application Insights handoff, 1DS client recommendation, questionnaire, identity/eligibility expectations, US processing/storage, described services, and variable estimates; not DAB-specific approval or a current service commitment.
- [Technical product-onboarding steps](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/29934/Steps-for-Onboarding-New-Products) and [service levels](https://devdiv.visualstudio.com/DevDiv/_wiki/wikis/DevDiv.wiki/18103/Data-Platform-Service-Levels): follow-up links from the supplied guide; their procedures and applicability to DAB remain to be validated.
- [Access guide repository source](https://devdiv.visualstudio.com/DevDiv/_git/DSI-Curation?path=%2FAccessWiki.md): retrieved through authenticated repository access on 2026-09-09; confirms reader and automation prerequisites, not DAB product-ingestion onboarding or parity with the published eng.ms revision.
