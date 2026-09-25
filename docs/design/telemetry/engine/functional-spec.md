# Engine telemetry functional specification

Engine product telemetry describes context, accepted configuration, lifecycle, and observed data-serving activity. It covers standalone, container, web, MCP stdio, and deliberately enabled embedded execution, including late configuration and hot reload.

The [shared contract](../shared-contract.md) supplies enablement, notice, identity, privacy, delivery, and CLI launch-linkage requirements. CLI command/profile events belong to the [CLI specification](../cli/functional-spec.md), not this collector. Current collection remains default-off with explicit synthetic validation only.

## Context: what is running

| Information | Required values |
| --- | --- |
| Product | DAB version, release channel, distribution, and packaging. |
| Process environment | OS family/coarse version, process architecture, and normalized .NET version. |
| Execution | Web, MCP stdio, or embedded mode; configured development/production host mode. |
| Launch and hosting | Launcher, hosting category, and container-detection state recorded separately. |
| Configuration delivery | Startup, late configuration, or hot reload. |
| Data sources | Distinct configured provider types and bucketed source count. |

Use reliable existing local signals or an explicit launcher bridge. Missing/conflicting evidence stays `unknown`. Container detection alone gives a generic-container classification, not an orchestrator or location. Do not require customer setup, network probes, raw environment values, hostname collection, or command-line inspection. An optional coarse cloud region requires approved explicit host input; it is never inferred from an observed IP.

Capture process context once per engine run. Configuration-specific context belongs to accepted configuration snapshots. Neither context nor configuration proves activity.

## Configuration: what is enabled

Capture an immutable snapshot on first readiness and each accepted configuration change, identified by engine session and configuration epoch.

| Family | Required information |
| --- | --- |
| API surfaces | REST, GraphQL, and MCP enablement independently. |
| Health/cache | Health endpoint, runtime cache, and L2 cache states. |
| API behavior | REST strict-body handling and GraphQL multiple-create state. |
| Integrations | Key Vault, automatic entity definitions, and multiple source-file presence. |
| Authentication | Provider category; any configured on-behalf-of delegation and session-context use. |
| Embeddings | Feature and endpoint presence, never endpoint values or content. |
| Customer observability | Enablement only for OpenTelemetry, Application Insights, Log Analytics, and file sinks. |
| Entity capabilities | Any table/view/procedure/persisted-document source; cache; REST/GraphQL/MCP DML/custom-tool exposure; custom role; request/database policy; description; relationship; parameter embedding. |
| Scale/limits | Bucketed entity count, default/max page size, maximum response bytes, cache TTL, and applicable query timeout. |

Feature states are `enabled`, `disabled`, `missing`, `unsupported`, `unknown`, and `not_applicable`. An entity-level "any" flag is enabled when at least one applicable entity has the feature; otherwise it is disabled when supported applicable entities exist. Zero entities has its own `0` bucket; supported entity features with no applicable targets are `not_applicable`. Preserve genuinely unsupported/unknown capability states.

Preserve explicit input versus known omission (`missing`). Record effective state separately where defaults or parent/provider rules affect behavior: explicit enabled and omitted/default-enabled must remain distinguishable. Missing telemetry is not evidence of omission, and enabled configuration is not evidence of use. Do not reconstruct input presence by serializing defaulted configuration. Rejected configurations produce no normal snapshot.

## Events

| Event | Boundary and event-specific data |
| --- | --- |
| `dab.engine.process_started` | Once at initialization; available context and epoch `0`. |
| `dab.engine.ready` | Once after usable configuration and actual serving readiness; snapshot and startup duration. |
| `dab.engine.startup_failed` | Known terminal failure before readiness; closed failure stage/category. |
| `dab.engine.configuration_changed` | Accepted replacement; next epoch and snapshot. |
| `dab.engine.configuration_change_failed` | Rejected replacement; failure stage/category and prior epoch retained. |
| `dab.engine.first_request_served` | First completed eligible data/tool request, successful or not; outcome and time since ready. |
| `dab.engine.first_successful_request` | First successful eligible request; time since ready. |
| `dab.engine.heartbeat` | Periodic liveness; run state, epoch, and uptime bucket. |
| `dab.engine.usage_summary` | Six-hour deltas and remaining graceful-shutdown delta; independent measurements and latency histograms. |
| `dab.engine.stopped` | Observed graceful shutdown; reason category and uptime bucket. |

Readiness requires usable data-serving capability, including initialized tools for MCP stdio; starting an HTTP host is not a stdio prerequisite. A host waiting for configuration is not ready. Epoch `1` is the first accepted configuration; rejected changes do not advance it. Normal reload does not emit another ready event.

First-served and first-success are independently once per engine run, including concurrent completions and reloads. A successful first completion can produce both; failure followed by success produces them separately. No eligible request means no milestone. Milestones do not add to request totals. Readiness/heartbeats are not usage; a missing stop is not proof of a crash.

## Observed usage

| Unit | Definition |
| --- | --- |
| Request | One completed REST data request, GraphQL execution, or MCP data/custom-tool call, including stdio. |
| Logical operation | One DAB read/write/execute action, including cache-served actions. A request can contain several. |
| Database attempt | One actual provider execution; each observed retry is separate. |
| Cache lookup | One lookup in one cache layer; L1 and L2 stay separate. |

A cached read is **1 request / 1 operation / 0 attempts**. A read with one database retry is **1 / 1 / 2**. Never derive one unit from another or assume a universal ordering between their totals.

- Exclude health probes, discovery, documentation/static assets, introspection-only GraphQL, and MCP protocol-control/metadata traffic.
- Success requires successful eligible data/tool execution and response completion. Cache hits and empty results qualify. HTTP 200 alone does not make GraphQL errors/partial results or MCP tool errors successful.
- GraphQL eligibility follows effective compiled selections and coerced variables, including defaults and conditional fields/fragments. Excluded data fields do not establish successful usage. Evaluate each variable-batch member independently; do not cache a request's eligibility on a reusable compiled operation. Rejected selected data operations can still count as failed attempts without resolver or database execution.
- Partition requests and logical operations into success, failure, partial failure, cancellation, and unknown. HTTP outcome classes are separate and do not apply to stdio. Preserve unknown when completion cannot be established.
- Aggregate independent request/outcome, operation/outcome, database-attempt/outcome, cache-layer hit/miss, embedding-use, and latency measurements. Include timed-request counts and measurable local loss/capping indicators.
- Retain applicable joint distinctions by API, transport, operation, provider, object type, role class, and outcome. Use closed categories, never entity/role names. Do not expand every family into a full dimension cross-product or duplicate totals across families.
- Update bounded counters directly, not a per-request event queue. Histogram schema, boundaries, timed count, and completeness must be explicit; incomplete measurements are not exact percentiles. Optional coarse CPU/memory samples need defined units/buckets and are not implied by the current implementation.

## Time, reload, and shutdown

Six-hour usage windows are nonoverlapping UTC-aligned segments. Preserve window boundaries, epoch, and measurement family. Count work in its completion-time segment using the accepted configuration captured for that work; reload must not relabel in-flight activity. Final shutdown sends only the remaining delta. Use monotonic duration measurements independently of wall-clock segmentation.

Apply the shared bounded-memory, nonblocking-delivery, disable/discard, and privacy requirements. Unobserved provider retries, background cache activity, dropped events, and incomplete stream outcomes must remain explicit coverage limitations rather than fabricated zeroes or successes.

## Functional acceptance

| Scenario | Expected result |
| --- | --- |
| Disabled/opted-out web, stdio, and embedded runs | No product identity I/O, counters, sender, notice, or transmission; normal serving and customer observability unchanged. |
| Context signals missing, blank, conflicting, or sensitive | Conservative categories; no raw values or extra network discovery. |
| Invalid startup, late configuration, and rejected reload | Correct failure stage; no premature ready/snapshot/epoch increment; prior telemetry epoch retained. |
| Accepted reload, automatic entities, and concurrent initialization | Snapshot describes the accepted serving model; stale completion cannot overwrite a newer acceptance. |
| Explicit/defaulted/unsupported/empty configuration | Configured/effective and all six states remain distinct; zero-entity applicability is preserved. |
| Failed request followed by success, concurrent requests, and reload | Each first-use milestone occurs at most once; no duplication in request totals. |
| Cached/empty reads, retries, batches, GraphQL conditions, and protocol errors | Independent units and correct logical outcomes; no database-attempt requirement for request success. |
| HTTP abort, MCP stdio/SSE, and streaming GraphQL | Completion is tied to the observable protocol boundary; no success inferred from an unfinished stream or session. |
| Window edge, midnight, backward clock, reload-spanning work, and shutdown | Disjoint counts, captured epochs, explicit loss/completeness, no replayed delta. |
| Opt-out, reset, unsafe state, concurrent identity creation, and offline delivery | Shared identity/privacy behavior; bounded memory and exit delay; telemetry never fails DAB. |
