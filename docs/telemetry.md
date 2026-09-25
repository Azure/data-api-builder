# Engine and CLI product telemetry validation

Engine and CLI product telemetry are **off by default in every build**. This implementation is for explicit synthetic validation; production enablement requires privacy/security review. Customer-configured logs, traces, metrics and telemetry destinations are separate.

## What "synthetic" means

Synthetic means **deliberately generated test activity**, not real customer usage. A live smoke test creates made-up rows in a temporary database, starts the actual DAB engine, makes real requests and checks the resulting events in Azure. The database calls, measured timings and delivery are real; the workload is artificial. Runtime and OS categories still describe the actual test process.

| Use of the term | Meaning |
| --- | --- |
| Test fixtures | Made-up configuration, rows, queries and errors. Recognizable sentinel values help detect accidental collection. |
| Product test-mode switch | `DAB_PRODUCT_TELEMETRY_TEST_MODE` permits the current validation-only path. It does not generate test data or change application requests. |
| Internal controls | `EnableSyntheticCollection` / `enableSyntheticCollection` enable internal collectors and sessions for validation; their defaults are off. |
| Event marker | Current event/window objects have `IsSynthetic=true`. The exporter writes `dab_is_synthetic=true` and rejects nonsynthetic events. Queries must explicitly exclude this custom marker from real-usage reporting; Azure does not do that automatically. |
| Local exporter tests | Fake HTTP/exporters and names such as `synthetic.invalid` exercise serialization without contacting Azure. They are distinct from live-cloud tests. |
| Live test-runner opt-in | `DAB_TELEMETRY_CLOUD_TEST=1` allows the `EngineTelemetryCloud` test to send to an explicitly supplied destination. This is a test-harness safeguard, not another customer-facing product setting. |

**The switch and marker do not anonymize or sanitize data, enforce a test-only database, or grant privacy approval.** Enabling test mode against real customer traffic would mislabel it as test traffic. Use an isolated fixture and destination; all field restrictions still apply. The marker is not a Microsoft-internal classification or an Azure availability-test marker. Likewise, a resource's synthetic-data-only tag is an administrative label, not an ingestion filter.

This temporary validation opt-in is not the intended production enablement policy. Production requires reviewed enablement and event classification, not asking customers to set a test-mode variable.

## Selecting a validation destination

Set these variables in a dedicated synthetic test process, then start DAB normally:

| Variable | Value / purpose |
| --- | --- |
| `DAB_PRODUCT_TELEMETRY_TEST_MODE` | `1` or `true` explicitly enables synthetic validation. A destination alone never enables collection. |
| `DAB_PRODUCT_TELEMETRY_CONNECTION_STRING` | The complete connection string copied from the chosen Application Insights resource. This is independent of `APPLICATIONINSIGHTS_CONNECTION_STRING` and the customer's runtime telemetry configuration. |
| `APPLICATIONINSIGHTS_STATSBEAT_DISABLED` | Exactly `true` (case-insensitive, no surrounding whitespace), set before the dedicated validation process starts. Suppresses Microsoft's SDK-health/usage stream for this validation phase. |
| `APPLICATIONINSIGHTS_SDKSTATS_DISABLED` | Exactly `true` (case-insensitive, no surrounding whitespace), set before process startup. Separately suppresses the SDK's customer-facing delivery counters and their metadata detection. |
| `DAB_TELEMETRY_OPT_OUT` | `1` or `true` overrides enablement. Values are trimmed and `true` is case-insensitive. |

Changing only `DAB_PRODUCT_TELEMETRY_CONNECTION_STRING` and restarting the process selects another instance. There is no compiled-in test or production destination, no automatic fallback to customer configuration, and no required AME credential in DAB. The resource must permit connection-string/key-based ingestion; the current adapter does not obtain Entra tokens.

The connection string needs a nonempty GUID instrumentation key and an explicit HTTPS ingestion endpoint (or a supported Azure endpoint suffix). Credential-bearing extensions, malformed endpoints and redirects are rejected. Do not commit connection strings or paste credentials into diagnostics. Azure management access is needed to create/manage/query the destination, not for every engine installation to send telemetry.

The product adapter uses **OpenTelemetry .NET with `Azure.Monitor.OpenTelemetry.Exporter` 1.9.0**. A private logger factory emits explicit custom events to `AzureMonitorLogExporter`; the SDK owns Application Insights serialization and response interpretation. No automatic request/dependency collectors, host logging providers, SDK batch processor or event disk spool are registered. The existing DAB delivery worker calls the synchronous SDK exporter; no task is created per event.

**The two SDK statistics opt-outs are temporary validation prerequisites, not requirements for sending custom events to Application Insights.** Statsbeat normally reports SDK health/usage to Microsoft; customer-facing SDK stats report delivery counters to the configured resource. Both can perform metadata detection outside DAB's current allowlist. DAB checks that both are disabled before initializing the product SDK and otherwise leaves product collection off. It never sets environment variables or `AppContext` switches. Do not apply these opt-outs to an unrelated customer host: SDKs in the same process can observe them too.

SDK 1.9.0 shares transmitters by connection string and caches some process configuration. DAB's CLI and engine lease a single serialized product sender per normalized destination, with at most eight destinations retained per process. Each collector owns its queue and cancellation; closing one cannot dispose another's transport. Idle senders remain process-owned to bound the SDK's destination cache. This does not isolate an unrelated exporter using the same routing. This phase supports a dedicated synthetic standalone process, not arbitrary embedded coexistence. Public embedded collection and production collection remain off. Switching the destination does not reset identities or change the event contract.

Start the validation process without `APPLICATIONINSIGHTS_CLOUD_ROLE_NAME`, `APPLICATIONINSIGHTS_CLOUD_ROLE_INSTANCE`, or `APPLICATIONINSIGHTS_COMPONENT_VERSION` overrides. The SDK caches these at first envelope construction, independently of the empty OpenTelemetry resource. DAB rejects enriched envelopes before HTTP rather than transmitting those values, changing environment settings, or resetting SDK internals. Removing an override later in that process does not clear the SDK cache.

## Enablement, notice and reset

- Disabled collection does not initialize identities, counters, timers, notices or sender resources. Saved identity state is untouched.
- An enabled run writes a noninteractive notice to stderr before sending, never to MCP stdout. Notice persistence is currently unavailable, so the notice repeats per enabled run.
- The API ID is a random UUID, stored best-effort beside an existing root configuration in a sidecar ending in `.dab-telemetry.json`. It is not a configuration hash, a person, a machine or a guarantee of replica grouping. The sidecar contains only a format version and the random ID.
- Safe persistence is currently supported on Windows and Linux x64/arm64. Missing, read-only, unsafe or unsupported storage uses a flagged per-run ephemeral API ID. Direct engine runs do not allocate CLI installation IDs.
- To reset, stop all affected processes and delete that configuration's identity sidecar. The next enabled run generates an unrelated ID. Opt-out is not reset. Reset does not delete earlier ingested telemetry.
- Hosts can resolve `IProductTelemetryControl` from the engine service provider and call `Disable()` to discard counters and pending delivery without a final send. Already transmitted data cannot be retracted. Environment changes apply to newly initialized collection lifetimes; SDK prerequisites must be set before process startup.
- The umbrella opt-out also prevents the DAB-added database Application Name segment, including its version marker. Recognizable existing DAB segments are removed at configuration load while customer prefixes are retained. Ambiguous/truncated customer text is not destructively guessed. The legacy `DAB_TELEMETRY_APPNAME_OPT_OUT` used alone retains its previous version-only behavior.
- Cosmos DB clients also omit DAB's additional Application Name/user-agent suffix under the umbrella opt-out; the Cosmos SDK's own identification is unchanged.

## Collected fields

All events include `dab_schema_version` (`"1"`), `dab_event_id` (random UUID), `dab_process_session_id` (random UUID per CLI invocation or engine run), `dab_sequence` (positive within-session integer), `dab_occurred_at` (UTC round-trip timestamp), and `dab_is_synthetic` (`"true"`). Engine events additionally include `dab_config_epoch`; CLI events omit it. Available API identity includes `dab_api_id` and `dab_api_id_stability` (`newly_saved`, `reused`, `ephemeral`). Retries preserve immutable event metadata. Application Insights stores custom property values as strings; receipt time belongs to the backend, not client occurrence time.

Explicit CLI launches also carry `dab_parent_cli_session_id`, available `dab_installation_id`, `dab_installation_id_stability`, and `launch_source` on engine events, beginning with `process_started` and including startup failure. Direct engine runs omit these fields. An accepted root different from the explicitly handed-off root cannot inherit its API identity.

Context includes DAB version; coarse OS family/version; process architecture; normalized .NET version; execution mode; categorical launcher/hosting/container detection; and distribution/channel/packaging labels. Known CLI/service entry assemblies select a categorical launcher; other entry points remain unknown. Distribution/channel/packaging remain `unknown` without reliable build provenance; synthetic opt-in does not prove source packaging. No network discovery or raw environment value is recorded.

Hosting is a best-effort observation captured once per enabled run, not platform attestation. Only nonblank presence of these documented built-in signals is considered:

| `hosting` category | Required signals |
| --- | --- |
| `azure_container_apps` | Both `CONTAINER_APP_NAME` and `CONTAINER_APP_REVISION`, or both `CONTAINER_APP_JOB_NAME` and `CONTAINER_APP_JOB_EXECUTION_NAME` ([Container Apps built-ins](https://learn.microsoft.com/azure/container-apps/environment-variables#built-in-environment-variables)). |
| `kubernetes` | Both `KUBERNETES_SERVICE_HOST` and `KUBERNETES_SERVICE_PORT_HTTPS`, with no nonblank Container Apps marker above ([Kubernetes in-pod signals](https://kubernetes.io/docs/tasks/run-application/access-api-from-pod/#directly-accessing-the-rest-api)). |
| `generic_container` | `DOTNET_RUNNING_IN_CONTAINER=true` without sufficient evidence for a specific platform. |
| `unknown` | Insufficient evidence, or an explicit `DOTNET_RUNNING_IN_CONTAINER=false` conflicting with platform inference. |

Complete Container Apps evidence takes precedence over Kubernetes evidence. Partial Container Apps evidence blocks a Kubernetes-specific inference; the generic/unknown fallback remains. `container` independently reflects the parsed .NET flag (`enabled`, `disabled` or `unknown`), so hosting can be known when that flag is absent. No names, revisions, job identifiers, addresses or ports are retained or exported. Disabled sessions do not inspect hosting signals; reloads do not refresh them.

Configuration events contain `snapshot_schema=configuration-v1`, configuration delivery, source-provider categories and bucketed counts/limits. Fixed feature families are:

| Family | Settings |
| --- | --- |
| API/runtime | `runtime.rest`, `runtime.graphql`, `runtime.mcp`, `runtime.health`, `runtime.cache`, `runtime.cache.l2`, `runtime.rest.strict_body`, `runtime.graphql.multiple_create` |
| Integrations | `integrations.key_vault`, `integrations.autoentities`, `integrations.multiple_source_files`, `runtime.embeddings`, `runtime.embeddings.endpoint`, endpoint presence |
| Authentication | `authentication.provider`, `host.mode`, `data_sources.obo`, `data_sources.session_context` |
| Customer observability enablement only | `customer_telemetry.open_telemetry`, `customer_telemetry.application_insights`, `customer_telemetry.log_analytics`, `customer_telemetry.file` |
| Entity capabilities | Any table/view/procedure/document, cache, REST/GraphQL/MCP exposure, custom roles, policies, descriptions, relationships and parameter embeddings |
| Scale/limits | Source/entity count, distinct provider count, page sizes, response bytes, cache TTL and modeled query timeouts |

Where applicable, `.configured` and `.effective` are separate. States are `enabled`, `disabled`, `missing`, `unsupported`, `unknown` and `not_applicable`. Original-input presence is captured as fixed-size, value-free metadata only during loads authorized by an enabled engine session, including nested source files. Ambient test mode alone does not enable capture in a CLI or disabled embedded host. Lost provenance is not reconstructed by serializing defaults. No customer entity/role names or values are retained in that metadata. Unsupported capabilities remain unsupported even when there are zero entities.

## Events and usage

### CLI events

| Event | Boundary and event-specific fields |
| --- | --- |
| `dab.cli.first_run` | Only the atomic publication winner of a new enabled installation profile. Reused, corrupt, or unavailable state does not claim another first run. |
| `dab.cli.command` | At most once when top-level parsing/dispatch actually finishes. `command`, `subcommand`, `control`, `outcome`, `failure_category`, monotonic `duration_ms`, and approved option-presence flags. |
| `dab.cli.engine_launch` | Actual preflight-approved service handoff, not parsing or readiness. `dab_parent_cli_session_id`, `dab_launched_engine_session_id`, and `launch_source`: `start_web`, `start_stdio`, or `export_graphql`. |

CLI fields and constraints:

- `command`: `init`, `add`, `update`, `start`, `validate`, `export`, `add-telemetry`, `configure`, `auto-config`, `auto-config-simulate`, `appname`, or `unknown`. There are no registered nested commands: `subcommand` is `none`. `control` is `none`, `help`, or `version`, confirmed by the actual parser result.
- `outcome`: `success`, `parse_failure`, `validation_failure`, `execution_failure`, `canceled`, or `unknown`. `failure_category`: `none`, `arguments`, `configuration`, `storage`, `initialization`, `execution`, `canceled`, or `unknown`. No numeric exception codes or diagnostic messages are sent.
- `duration_ms`: nonnegative integer measured until completion. For in-process `start`, this includes the serving lifetime, not only startup. A web host can historically return success after failed initialization; the exit code remains unchanged while an explicitly observed engine startup failure is reported as `execution_failure`.
- `option_*`: at most 96 constant `"true"` flags. The complete approved names are the fixed `ApprovedLongOptions` registry in [CliTelemetryCommand](../src/Cli/Telemetry/CliTelemetryCommand.cs). Keys are `option_` plus the approved long name with hyphens/dots replaced by underscores; `LogLevel` normalizes to `log-level`. Short aliases normalize to the same key. Presence is not value assignment or validation success. Defaults and positional values never contribute flags. The adapter follows the pinned parser's actual grammar, including its disabled `EnableDashDash` setting; it does not change command semantics.
- CLI context keys: `dab_version`, `os_family`, `os_version`, `architecture`, `dotnet_version`, `hosting`, `container`, `distribution`, `release_channel`, `packaging`, `install_channel`. OS/runtime and hosting use the same coarse categories as the engine. Distribution, channel, packaging and installation channel remain `unknown` without reliable provenance. CLI events do not manufacture engine execution mode or configuration snapshots.
- `dab_installation_id`: available persistent random UUID; `dab_installation_id_stability`: `newly_saved`, `reused`, or `unavailable`. No ephemeral installation ID is substituted. `sender_dropped_events` is a saturating nonnegative local loss snapshot, not complete delivery accounting.

Installation state is `<OS user profile>/.dab/telemetry/installation.json`, containing only `{"version":1,"installationId":"<random UUID v4>"}` within 1 KiB. It identifies a local profile, not a person, machine, executable version, or configuration. Windows validates ownership/ACLs; Linux validates ownership/private modes. Links, remote paths, malformed/future state, unsupported platforms and unsafe profiles fail closed to unavailable. Existing owner state and permissions are not repaired. Only missing private child directories/state are created; concurrent creators safely converge. Reset by removing that state file while affected processes are stopped; the next enabled invocation creates an unrelated ID. Opt-out performs no product-state I/O and is not a reset.

Successful enabled `init` allocates/reuses the existing engine API-sidecar format **after the configuration write succeeds and before completion**. Later commands only look up existing identity for the root they actually resolve. An actual engine handoff may create the engine's API identity and passes it explicitly, including same-run ephemeral identity. Source files are not separate API deployments. An environment-generated merged configuration is its actual distinct root; input sidecars are not copied or guessed. Ambiguous command targets omit API linkage.

`export` remains one command, even when its helper runs asynchronously. A scheduled helper reserves bounded observation ownership without emitting anything. If preflight succeeds after CLI completion, its actual launch can appear later in the same CLI sequence; command duration is not extended. Normal CLI disposal does not revoke those reservations or stop an active engine. Explicit disable revokes reservations and discards their pending delivery. No speculative launch is emitted for canceled or rejected helpers. There are at most 16 launches/reservations per invocation, with a capacity-one, independently drained queue for each reserved launch. Engine and CLI sequences remain distinct.

`Main` bootstraps CLI collection only after the shared enablement/routing checks. The public `Execute` API does not create a CLI collector from environment settings; internal tests inject one explicitly. Customer `add-telemetry` settings never enable or select the product sender.

### Engine events

Events are `dab.engine.process_started`, `ready`, `startup_failed`, `configuration_changed`, `configuration_change_failed`, `first_request_served`, `first_successful_request`, `heartbeat`, `usage_summary` and `stopped`, all under the `dab.engine.` prefix.

Both failure events include a closed `failure_stage`: `parsing`, `validation`, `metadata`, `serving`, `configuration`, `initialization` or `unknown`. It identifies the failing boundary, not exception text or a customer-supplied value. `failure_category` remains `initialization` for startup failures and `configuration` for rejected changes. Rejections retain the prior telemetry epoch and do not stop collection; the engine's existing configuration acceptance and retry rules are unchanged. Concurrent initialization handlers preserve the first observed failure within that attempt, not a later successful stage. An unclassified handler rejection uses `initialization` rather than guessing a cause.

The mapped embedding HTTP endpoint is included as REST request traffic even when its path is outside the entity REST prefix. Its embedding-service invocations form the separate embedding measurement family; cache-served invocations count without inventing a database attempt. Cache lookups include the dedicated embedding cache when enabled, without double-counting the default-cache fallback. This identification uses endpoint metadata, not request/response contents. Valid REST entity routes are not excluded merely because their names resemble documentation or static assets.

Readiness requires accepted usable configuration and host/tool readiness. First-served and first-success are independently once per run. Discovery, health, documentation, introspection-only GraphQL, and MCP protocol-control/metadata traffic are excluded. HTTP 200 with GraphQL errors or an MCP tool error is not logical success. Variable-batch GraphQL results count independently. The current incremental/streaming GraphQL path reports `unknown` rather than inventing success from an unfinished stream.

GraphQL data eligibility uses the executor's compiled root selections and coerced variables, including variable defaults and conditional fields/fragments. A successful execution whose data selections are all excluded by `@skip`/`@include` does not count as data usage. Decisions are request-local, including each variable-batch member; cached compiled operations do not retain telemetry eligibility. Failed validation/coercion of a selected data operation can still count as a failed attempt when effective selections are unavailable, never as successful usage. Eligible cached and empty reads count without requiring a database attempt or returned row. Each completed batch member uses its own result outcome, not another member's error.

The dedicated internal health client marks its self-probes with an in-memory per-session value so REST/GraphQL health queries cannot establish usage milestones or add usage counts. The marker is neither stored nor exported and does not grant authorization. Ordinary requests, including callers supplying an unrelated marker, remain eligible. System roles are classified case-insensitively, matching authentication behavior.

MCP HTTP completion is observed per tool response, including legacy SSE sessions. The SDK's nonserialized message context carries only an opaque completion holder; an outgoing filter and byte-opaque stream observer check write/flush completion without inspecting payloads or storing request IDs. A completed tool response does not wait for session disconnection. A send with no observable write/flush remains `unknown`, and observed write failures remain failures even if the SDK absorbs the exception. This proves server-side flush, not client receipt.

Usage summaries separate requests, logical operations, database attempts, each cache layer, embedding calls and HTTP outcome classes. No HTTP denominator applies to stdio. Outcomes partition request/operation counts into success, failure, partial failure, cancellation and unknown. Latency is a noncumulative histogram with inclusive millisecond bounds **1, 5, 10, 50, 100, 500, 1000, 5000, 30000**, plus an unbounded final bucket. Timed counts, completeness and capping are explicit.

Six-hour windows align to UTC 00:00/06:00/12:00/18:00. Work retains its captured configuration epoch across reloads and completes in its completion-time segment. SQL attempt attribution first uses the captured source metadata, then matching current metadata; if neither still describes the executing source, the attempt remains counted with an `unknown` provider. Final shutdown includes only the remaining delta. Memory limits are 256 occupied series per open window, four pending windows and 256 queued/in-flight immutable events. Excess observations/events are dropped with measurable loss counters. Counter saturation and backward-clock loss are explicit.

### Coverage limits

- Actual SQL command executions, including observed retries, are counted. Cosmos SDK-internal physical retries are not available through the supported public hooks used here; `database_attempt_coverage=sql_commands_only` must not be interpreted as zero Cosmos attempts.
- Cache measurements observe actual L1/L2 events with a live eligible request context. Background/unattributed activity is not invented; coverage is labeled accordingly.
- Full backend ingestion, deployed destination policy and sender isolation in arbitrary embedded hosts are separate acceptance gates. Local tests do not prove cloud receipt.

## Privacy and failure behavior

No request/response contents, raw command lines or argument values, SQL, URLs, connection strings, customer/host/database/entity/role names, claims, tokens, observed IP addresses, exception text, hardware identifiers or request/trace IDs are product event fields. Registered CLI command names and approved option-presence flags are the only command-line categories collected. The private logger excludes scopes/resources and clears diagnostic and ambient trace fields before SDK export. A transport guard rejects unexpected SDK envelope context rather than rewriting it. SDK diagnostic `EventSource` output can still be observed by a host that deliberately subscribes; general host isolation is not claimed. Identity is pseudonymous, not anonymous.

The SDK maps the constant `microsoft.client.ip=0.0.0.0` attribute to the required `ai.location.ip=0.0.0.0` tag. The guard permits the SDK/runtime version tag and null resource tags, but rejects populated host/user/trace context or missing IP suppression. Omitting the IP tag allows the receiver to derive geolocation from the connection before masking the stored IP. Destination validation must also check that stored location fields are empty; IP masking alone is insufficient.

Delivery is bounded and memory-only: no event spool or restart replay. Export happens only on the worker, with at most three attempts per immutable event, a one-second HTTP attempt budget and a two-second graceful drain budget. Disable cancels in-flight work and discards pending data. Outages, process termination, caps and transport ambiguity can lose or duplicate received events; deduplicate by `dab_event_id`. A missing stop is not proof of a crash.

Identity resolution runs outside the session lock; an in-flight filesystem operation cannot hold up disable/shutdown or re-enable collection when it finishes. An older configuration acceptance finishing late cannot overwrite a newer acceptance. An already-cancelled stop discards without constructing final events, and cancellation from a later stop caller also cancels the shared pending drain. Filesystem calls themselves are best-effort and are not forcibly interrupted.

Each attempt sends one event through the SDK. Attribute lengths/counts are bounded before SDK conversion; the transport rejects payloads above 64 KiB and response bodies above 16 KiB. SDK offline storage and pipeline retries are disabled, so the DAB worker owns the finite retry budget. The temporary endpoint guard refuses redirects. SDK success is not a guarantee of later storage or exactly-once delivery. It does not mean data was merely queued: this path has no SDK batch queue or offline backlog. Supported production handling of throttling/redirects and independent SDK lifetime remains part of the integration review.

## Local validation

`CliTelemetry` adds parser/handler parity, profile/API persistence, explicit launch linkage, bounded delivery and SDK envelope tests. `CliTelemetryLocalDb` runs real CLI `init`/`add`/web/stdio/export lifetimes against owned LocalDB fixtures and local test transports. An isolated child test verifies SDK-cached host overrides are rejected rather than contaminating other tests. See the [implementation design](design/telemetry/cli/implementation.md) for requirement boundaries.

The `EngineTelemetry` tests use synthetic input and fake HTTP/exporters. The test project's default [run settings](../src/Service.Tests/telemetry.runsettings) set both SDK statistics opt-outs before testhost startup; an explicitly supplied replacement settings file must do the same. `EngineTelemetryLocalDb` additionally executes actual REST, GraphQL and MCP stdio against a unique LocalDB database and cleans up after host disposal. Exporter tests inspect actual SDK-serialized envelopes, cancellation/response handling, unchanged environment settings and the shared-transmitter limitation. OS-permission and symlink cases may skip when prerequisites are unavailable.

The separately gated `EngineTelemetryCloud` test launches the exact standalone engine selected by `DAB_TELEMETRY_CLOUD_TEST_ENGINE`, using the approved `DAB_PRODUCT_TELEMETRY_CONNECTION_STRING` supplied privately in the test environment. It sets both SDK statistics opt-outs only in that child process. Without `DAB_TELEMETRY_CLOUD_TEST=1`, it skips before database/cloud activity. It creates and removes its own LocalDB fixture, performs two successful reads (including an empty result) and one rejected read, exercises excluded discovery/control calls, then closes stdin for graceful shutdown. Its receipt contains the engine hash, time window, random API identity and expected counts, not the connection string or fixture contents.

A passing serving test alone does not prove delivery. Correlate its API identity to the engine session and query `customEvents` in Application Insights and `AppEvents` in the linked workspace. Compare event IDs, deduplicate by `dab_event_id`, reconcile usage counts and inspect privacy indicators. These are two query views of the same workspace-backed data, not separate DAB uploads. Allow for ingestion delay and out-of-order arrival; do not rerun the workload just because the first query is incomplete. Application Insights' default Overview request/availability charts do not display these custom events; use **Monitoring > Logs**.
