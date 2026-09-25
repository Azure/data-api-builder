# Engine telemetry implementation handoff

Requirements: [Engine functional specification](functional-spec.md) and [shared contract](../shared-contract.md). This handoff describes the implemented baseline and its integration constraints, not approval to enable production collection.

## Baseline and ownership

The source baseline is [e85f3338](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a) on `dev/aaronburtle/engine-telemetry-phase1`. The documentation branch does not contain that implementation merely because these files are present.

| Boundary | Existing component at the baseline |
| --- | --- |
| Enablement | [ProductTelemetryPolicy](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Config/Telemetry/ProductTelemetryPolicy.cs), [EngineTelemetryHosting](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/EngineTelemetryHosting.cs). |
| Run lifecycle and captured request ownership | [EngineTelemetrySession](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetrySession.cs), [EngineTelemetryRequestScope](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryRequestScope.cs). |
| Identity | [EngineTelemetryIdentityStore](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryIdentityStore.cs). |
| Context and configuration | [EngineTelemetryContext](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryContext.cs), [EngineTelemetrySnapshotFactory](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryConfigurationSnapshot.cs#L19), [TelemetryConfigurationPresence](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Config/Telemetry/TelemetryConfigurationPresence.cs). |
| Failure boundaries | [TelemetryFailureContext](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Config/Telemetry/TelemetryFailureContext.cs), [TelemetryFailureStage](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Config/Telemetry/TelemetryFailureStage.cs). |
| Aggregation and delivery | [EngineTelemetryAggregator](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryAggregator.cs), [EngineTelemetryDelivery](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryDelivery.cs). |
| SDK boundary | [EngineTelemetryApplicationInsightsExporter](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/EngineTelemetryApplicationInsightsExporter.cs), [ApplicationInsightsEventAttributes](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/ApplicationInsightsEventAttributes.cs), [EngineTelemetrySdkTransportHandler](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/EngineTelemetrySdkTransportHandler.cs). |

One collector owns one engine run. Bootstrap owns the session; host callbacks supply serving readiness. Context is captured once, and accepted configuration projections are immutable. Request scopes capture their owner, epoch, start time, and configuration before asynchronous work. No process-global mutable collector may attribute work from another engine instance.

## Lifecycle and collection hooks

- [Service.Program](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Program.cs) creates the standalone session. [Startup](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Startup.cs), [RuntimeConfigProvider](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Configurations/RuntimeConfigProvider.cs), and [McpStdioHelper](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Utilities/McpStdioHelper.cs) distinguish accepted configuration, metadata initialization, serving readiness, and shutdown.
- Configuration acceptance reserves an ordering generation before identity I/O. Slow, older acceptance cannot replace a newer configuration. Identity work occurs outside the session lock; disable/shutdown cannot be held behind that lock while storage is accessed.
- Failure events use fixed stages: parsing, validation, metadata, serving, configuration, initialization, or unknown. Rejected reloads preserve the previous epoch. Preserve existing handler return/exception/cancellation semantics and the first observed failure within an attempt.
- [HTTP middleware](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/EngineTelemetryHttpMiddleware.cs) recognizes routed REST/embedding work; [HTTP completion](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/EngineTelemetryHttpCompletion.cs) resolves response completion/abort exactly once, independently of logical outcome.
- [GraphQL listener](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/EngineTelemetryGraphQLListener.cs) uses HC16 compiled selections and coerced variables. It establishes an ambient scope synchronously before resolvers, retains per-variable eligibility, and completes only eligible batch members. Syntax is a failure-intent fallback, never evidence of successful execution. Callbacks must not retain pooled GraphQL contexts or result payloads.
- [ExecutionHelper](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Services/ExecutionHelper.cs) and query/tool adapters measure logical actions. [QueryExecutor](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Resolvers/QueryExecutor.cs) measures actual SQL attempts, including observed retries, using captured source attribution before falling back to matching current metadata.
- MCP completion uses supported SDK hooks and opaque completion state. Legacy SSE completes per tool response, not when the entire session disconnects. Transport observation checks writes/flushes without examining payloads. Stdio has no HTTP denominator.
- Cache observation covers the default cache and the distinct embeddings cache without duplicate subscriptions. Background activity without a live eligible request is not invented. Engine-controlled health probes do not create product usage.

## Measurement and resource contract

| Family | Joint dimensions and measurement |
| --- | --- |
| Requests | API, transport, role class; outcome partition and latency. |
| Logical operations | API, operation, provider, object type; outcome partition. |
| Database attempts | Provider; outcome partition. |
| Cache lookups | Layer and hit/miss/unknown. |
| Embedding use | API and outcome. |
| HTTP outcomes | API and HTTP status class. |

Only closed categories reach aggregation. Static context/configuration belongs to snapshots rather than every counter key. Never retain names, queries, payloads, exceptions, or an unbounded history of configuration objects for attribution.

| Limit | Baseline value |
| --- | --- |
| Usage windows | Six hours, aligned to UTC 00:00/06:00/12:00/18:00; initial/final segments may be partial. |
| Open-window series | 256, including epoch and family-specific dimensions. |
| Pending windows | 4. |
| Queued/in-flight events | 256, owned by one delivery worker. |
| Attempts per event | At most 3, preserving event ID and immutable contents. |
| Transport attempt / graceful drain | Approximately 1 second / at most 2 seconds. |
| Latency upper bounds | Inclusive 1, 5, 10, 50, 100, 500, 1000, 5000, 30000 ms, then overflow; `request-latency-ms-v1`. |
| SDK HTTP payload / response guard | 64 KiB / 16 KiB. |
| API identity state | At most 1 KiB. |

Counter saturation, capacity loss, dropped windows, and backward-clock observations are explicit. Overflow must not be attributed to a different epoch. Shutdown seals only the remaining delta; disable discards it. An already-canceled stop does not construct final events, and cancellation of a later stop caller can cancel the shared pending drain.

## Identity and context compatibility

The API sidecar appends `.dab-telemetry.json` to the actual root configuration filename. Its version-1 payload contains only `version` and `apiId`. Reuse valid saved IDs; atomic no-overwrite publication handles concurrent creators. Unsafe, malformed, unsupported, missing-root, or inaccessible storage falls back to a flagged API ID for that run, without repairing owner files or changing permissions. The baseline supports persistence on Windows and Linux x64/arm64; unsupported environments fall back conservatively.

The context has 12 fixed properties. Known entry assemblies identify CLI/service launchers; this is not CLI command instrumentation or session linkage. Hosting uses nonblank presence pairs: Container Apps app name/revision or job name/execution; otherwise Kubernetes host/HTTPS-port with no Container Apps markers. Complete Container Apps evidence wins, partial evidence prevents a Kubernetes-specific inference, and explicit `.NET container=false` vetoes orchestrator inference. Values are never retained. Generic-container/unknown fallbacks and the independent container state preserve uncertainty.

CLI integration must preserve this API identity format and extend the bootstrap deliberately for the [explicit bridge](../shared-contract.md#cli-to-engine-launch-boundary). The baseline creates its own engine-session ID and does not accept a CLI launch context yet. It does not create installation IDs, CLI events, or persistent notice state.

## SDK and validation boundary

The adapter uses OpenTelemetry .NET with `Azure.Monitor.OpenTelemetry.Exporter` 1.9.0 and the public `AzureMonitorLogExporter`. A dedicated logger factory emits only approved custom events with empty resource/scope/trace context; it is not connected to customer logging or a broad auto-instrumentation distribution. The delivery worker calls the synchronous exporter; no SDK batch queue or disk event spool is registered.

Synthetic standalone validation requires `DAB_PRODUCT_TELEMETRY_TEST_MODE=1|true`, a valid `DAB_PRODUCT_TELEMETRY_CONNECTION_STRING`, both `APPLICATIONINSIGHTS_STATSBEAT_DISABLED=true` and `APPLICATIONINSIGHTS_SDKSTATS_DISABLED=true`, and no umbrella veto. SDK statistics settings accept case-insensitive literal `true` without whitespace trimming. Product code checks but never changes environment variables or `AppContext` switches. There is no compiled-in destination or fallback to customer routing.

Suppress source-IP geolocation using constant `microsoft.client.ip=0.0.0.0`, mapped by the SDK to `ai.location.ip`. The transport guard rejects unexpected host/user/location/trace context rather than rewriting payloads. Validate empty stored location fields as well as serialized envelopes. SDK-generated diagnostics can still be observed by a deliberately subscribed host.

SDK 1.9.0 shares transmitters by connection string and caches some process settings. A second exporter object does not guarantee independent settings or lifetime. Current support is a dedicated synthetic standalone process; arbitrary embedded/customer-exporter coexistence remains unproven. CLI integration in the same process must test shared sender ownership and disposal rather than assuming isolation. Redirect/throttling policy and production activation are not completed by the current finite best-effort retries.

## Acceptance evidence and remaining limits

Existing [telemetry tests](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service.Tests/Telemetry) cover lifecycle, snapshots, identity, aggregation, SDK envelopes, HC16 execution/batching, MCP transports, cache/embedding hooks, failure stages, and actual LocalDB serving paths. Revalidate changed paths rather than relying on historical totals.

- `EngineTelemetry` uses synthetic fixtures and memory/fake-HTTP destinations. `EngineTelemetryLocalDb` exercises actual REST, GraphQL, and MCP stdio serving with owned databases. Platform prerequisites may skip permission/symlink cases; skips are not evidence of that platform's behavior.
- Real cache hits must count without SQL attempts, and successful uncached empty reads must count. The separate cache-enabled empty-result serving defect is tracked by [#3704](https://github.com/Azure/data-api-builder/issues/3704); do not misreport that failing serving path as a successful telemetry test.
- `EngineTelemetryCloud` is separately gated. Only observed receipt from the exact built artifact, selected destination, and run proves cloud delivery; local tests do not. Preserve event-ID deduplication and privacy checks when validating receipt.
- Physical-attempt coverage is SQL commands only; Cosmos SDK-hidden retries are not claimed. Streaming/incremental GraphQL terminal outcomes remain unknown. External launcher markers, CLI linkage, additional packaging provenance, CPU/memory samples, and cross-platform identity execution evidence are not completed by this baseline.
