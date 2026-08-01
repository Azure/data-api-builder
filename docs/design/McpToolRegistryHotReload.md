# Design Document: MCP Tool Registry Hot-Reload

## Status

Implemented by this change; retained as the design and review record.

This document describes the implemented design for refreshing Data API builder's MCP tool registry
when runtime configuration is hot-reloaded. It records not only the final behavior but also the
compatibility boundaries, lifecycle guarantees, rejected alternatives, and known limitations that
are important during review.

Source links point to the implementation delivered by this change.

## Summary

Before this change, DAB built its MCP tool registry once at startup. A hot-reloaded `RuntimeConfig`
could change which custom tools existed and could change their names, descriptions, and input
schemas, but the registry continued serving the startup tool instances and startup metadata.

The implemented design keeps `McpToolRegistry` as a singleton and changes its contents to an atomically published immutable snapshot. A singleton refresh service builds a complete candidate snapshot after the existing metadata, engine, and authorization hot-reload handlers have run. If candidate construction succeeds, the service atomically swaps the snapshot. If it fails, the previous snapshot remains active.

The design also sends `notifications/tools/list_changed` to an initialized stdio client when the advertised tool list or metadata changes. HTTP requests always read the current snapshot, but HTTP push notifications are deferred because the installed MCP SDK requires experimental session tracking APIs for broadcast notifications.

## Reviewer Guide: Deliberate Design Calls

The following decisions are intentional. They are summarized here because they are the places most
likely to look surprising when reviewing the implementation in isolation.

| Design call | Why this design was chosen | Accepted consequence |
|---|---|---|
| Replace the complete registry instead of mutating it or DI | One atomic reference swap gives lookup and discovery one generation and preserves the previous generation on failure. | Every applicable reload rebuilds all generated custom tools. |
| Keep both a loader serialization gate and a registry writer lock | The loader gate protects cross-component metadata/config ordering; the registry lock protects publication from direct or out-of-band callers. They have different ownership and neither is redundant. | Registry rebuilds are serialized even outside the normal file-loader path. |
| Defer initial publication from hosted-service `StartAsync()` | Generic hosted services start before `Startup.Configure` completes database metadata initialization. Publishing there would advertise config-only or stale schemas. | The hosted service subscribes early, while the shared startup helper performs strict initial publication after metadata is ready. |
| Use `RuntimeConfig` reference identity as the generation token | The loader creates and publishes a new configuration object for each parsed generation. Reference identity is cheaper and less error-prone than structural comparison or maintaining a second version counter. | Callers that replace the current configuration object create a new generation even when values are equal. |
| Publish a fresh generation even when discovery metadata is equivalent | Generated tool instances must align with the current configuration and metadata generation. | Registry version and instances change, but no `list_changed` notification is sent for semantically equivalent discovery. |
| Retain the previous registry when a reload candidate fails | A complete previously validated snapshot is safer than a partial or invalid new snapshot. | Until a later successful reload, runtime configuration can be newer than MCP discovery; execution-time revalidation prevents obsolete authorization or execution. |
| Permit configuration-schema fallback when DB enrichment is unavailable | Existing custom-tool behavior already has a usable configuration-derived schema, and metadata unavailability should not silently remove an otherwise callable tool. | Discovery can be less precise; the fallback reason is logged. All other construction failures reject the whole candidate. |
| Keep ordered hot-reload callbacks synchronous | Existing DAB ordering is defined by synchronous event completion. Returning early or introducing an untracked queue would let later components publish before earlier ones finish. | A watcher callback can remain occupied for the reload duration; concurrent callbacks wait on the loader gate and shutdown cancels queued waiters. |
| Add cancellation through Core rather than only canceling gate waits | Shutdown cannot safely drain a reload if metadata connection opening, schema discovery, query execution, or token acquisition ignores cancellation. | The change necessarily touches shared query and metadata paths; default interface implementations preserve existing implementers. |
| Bound shutdown by `HostOptions.ShutdownTimeout` | An unbounded drain can hang process shutdown forever when an extension callback does not cooperate. | A successful drain guarantees dependency safety; after timeout, the host may dispose dependencies while non-cooperative extension code is still running. .NET cannot forcibly terminate that code. |
| Make synchronous `Dispose()` nonblocking | `Dispose()` can run after the host's bounded drain has already timed out and must not reintroduce an infinite wait. | Coordinated hosts and direct consumers that need a drain must call `StopAsync()` before disposing dependent services. |
| Dispose the OS watcher and blocked stdout resources without joining them | `FileSystemWatcher.Dispose()` and an abandoned stdout pipe can block independently of reload correctness. | Event admission is stopped synchronously; exceptional resource cleanup is left to a background worker or process teardown rather than delaying host shutdown. |
| Coalesce stdio invalidations and do not replay pre-initialization changes | `list_changed` is an invalidation, not a change log; one frame tells the client to fetch the latest complete snapshot. Before initialization the client has no established cache to invalidate. | Intermediate generations are not individually reported. |
| Advertise stdio push but not HTTP push | Stdio has one owned connection. HTTP broadcast requires experimental MCP SDK session interception and tracking. | HTTP clients see the latest snapshot on their next explicit `tools/list` request but receive no push invalidation in this change. |
| Remove CLR-public MCP implementation plumbing but preserve supported Core interfaces | Incremental registry methods permit a second, non-atomic construction model and the MCP assembly is not a supported reference package. Core interfaces are supported extension points. | Manual consumers of unsupported MCP runtime members must migrate; Core implementers remain source- and binary-compatible through default interface methods. |
| Allow an in-flight call to retain its resolved old tool instance | Invalidating or canceling arbitrary requests at the instant of publication would require per-request generation tracking. | The call can finish, but generated tools revalidate current configuration, metadata, and authorization before execution. |

The detailed sections below define the exact guarantees and alternatives behind these calls.

## Motivation

Custom MCP tools are generated from stored-procedure entities with `mcp.custom-tool` enabled. Before
this change, those tools were constructed from the startup configuration and registered as DI
singletons. The registry was then populated once by a hosted service.

Consequently, a configuration hot-reload can leave MCP discovery stale in several ways:

- A newly enabled custom tool does not appear.
- A removed or disabled custom tool remains registered.
- Renaming an entity does not update the tool name.
- Changing an entity description does not update the tool description.
- Changing stored-procedure parameters does not update the advertised input schema.
- A custom tool can retain metadata derived from an old database metadata generation.

Built-in tool visibility already evaluated the current configuration during each `tools/list`
request, but combined it with a fixed startup registry. This avoided some stale built-in visibility,
but did not solve stale custom tools or provide a single consistent registry generation.

## Previous Implementation

### Registry construction

Before this change, [McpServiceCollectionExtensions.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpServiceCollectionExtensions.cs):

1. Registers `McpToolRegistry` as a singleton.
2. Registers `McpToolRegistryInitializer` as a hosted service.
3. Discovers built-in `IMcpTool` implementations and registers them as singletons.
4. Builds custom tools from the startup `RuntimeConfig` and registers each custom tool as a singleton.

The former `McpToolRegistryInitializer` resolved every `IMcpTool` and registered it once when the host started.

[McpStdioHelper.cs](../../src/Service/Utilities/McpStdioHelper.cs) separately initializes the registry because stdio mode deliberately builds, but does not start, the ASP.NET Core web host.

### Registry state

[McpToolRegistry.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpToolRegistry.cs) stored tools in a mutable, case-insensitive `Dictionary<string, IMcpTool>`. It supported individual registration, lookup by name, and filtering enabled tools using a supplied `RuntimeConfig`.

The dictionary was safe under startup-only mutation, but could not be modified concurrently with MCP requests.

### Custom tool metadata

[DynamicCustomTool.cs](../../src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs) captures an `Entity` at construction time. Its tool name, description, and configuration-based parameter schema therefore belong to that configuration generation. `InitializeMetadata(IServiceProvider)` may cache a schema enriched from database metadata.

Execution is safer than discovery: `ExecuteAsync()` retrieves the current `RuntimeConfig`, verifies that the entity still exists, verifies that it is still a stored procedure with custom-tool enabled, and uses current database metadata and authorization state. A stale tool can therefore fail safely, but it can still be advertised with stale metadata.

### MCP request handlers

[McpServerConfiguration.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpServerConfiguration.cs) implements HTTP `tools/list` and `tools/call` handlers using the registry singleton.

[McpStdioServer.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpStdioServer.cs) implements the equivalent stdio JSON-RPC handlers.

Both transports combined a fixed registry with the latest runtime configuration during `tools/list`.

### Existing hot-reload pipeline

[RuntimeConfigLoader.cs](../../src/Config/RuntimeConfigLoader.cs) raises named hot-reload events in an intentional order:

1. Query-manager factory.
2. Metadata-provider factory.
3. Query-engine factory.
4. Mutation-engine factory.
5. Documentation.
6. Authorization resolver.
7. GraphQL schema operations.
8. Log-level initialization.

The MCP registry needs refreshed database metadata and must not publish newly callable tools before their query, mutation, and authorization dependencies are ready. It must therefore participate in this ordered pipeline rather than subscribing directly to the earlier runtime change token.

## Goals

1. Refresh custom tool membership after a successful runtime configuration hot-reload.
2. Refresh custom tool names, descriptions, and input schemas.
3. Preserve current built-in tool enablement behavior.
4. Ensure `tools/list` observes one internally consistent registry generation.
5. Ensure `tools/call` never observes a partially rebuilt registry.
6. Keep registry reads lock-free or effectively lock-free.
7. Preserve the previous registry when a hot-reload rebuild fails.
8. Use the same construction path for HTTP startup, stdio startup, and hot-reload.
9. Notify an initialized stdio client when advertised tools change.
10. Keep the implementation compatible with the existing ordered hot-reload architecture.
11. Avoid dynamic mutation of the DI container.
12. Provide deterministic behavior and sufficient logging for diagnosis.
13. Preserve independently DI-registered `IMcpTool` extensions across registry generations.

## Non-Goals

This work does not:

- Dynamically enable MCP when it was disabled at application startup.
- Dynamically disable or unmap an MCP endpoint.
- Dynamically change `runtime.mcp.path`.
- Rebuild ASP.NET Core endpoint routing or middleware.
- Change initialize instructions for an already-established MCP session.
- Broadcast HTTP tool-list notifications through experimental MCP SDK session APIs.
- Make the complete DAB hot-reload pipeline transactional.
- Solve cross-component generation isolation for every DAB hot-reload consumer.
- Change MCP authorization semantics.
- Change the wire shape of existing tools.

Changes to startup-bound MCP settings continue to require a process restart. Improving global hot-reload rollback and transactionality is tracked separately.

## Design Decisions

### 1. `McpToolRegistry` remains a singleton

HTTP and stdio request handlers already retain a reference to the registry. Keeping one singleton avoids rebuilding MCP servers, handlers, transports, or service providers.

The singleton no longer exposes a dictionary that is incrementally mutated during normal operation. Instead, it holds one current immutable snapshot reference.

### 2. Registry generations are immutable snapshots

The registry snapshot conceptually contains:

```csharp
internal sealed record McpToolRegistrySnapshot(
    long Version,
    ImmutableDictionary<string, IMcpTool> Tools,
    int AdvertisedToolCount,
    string DiscoveryJson,
    string DiscoveryCanonicalJson);
```

`Tools` contains:

- Every built-in tool, including built-ins currently disabled by DML tool configuration.
- Every independently DI-registered `IMcpTool` implementation.
- Every configuration-generated custom tool enabled in the configuration used to build the snapshot.

`DiscoveryJson` contains precomputed metadata for tools whose `IsEnabled(config)` result was true
for that same configuration generation. Tools are sorted deterministically by name, while nested
schema-property insertion order is preserved for clients that render parameters in wire order.
`DiscoveryCanonicalJson` is a separate recursively property-sorted representation used only for
semantic change comparison. `AdvertisedToolCount` avoids retaining a duplicate object graph solely
for diagnostics.

Keeping lookup state and advertised metadata in the same snapshot prevents a request from combining tools from one generation with enablement or metadata from another generation.

Protocol `Tool` objects are mutable SDK models, so the registry defensively clones metadata during
candidate construction and again when returning public discovery results. Candidate publication
pre-serializes the order-preserving discovery representation, which the accessor deserializes to
produce caller-owned clones without reserializing every tool per request. The separate canonical
representation is never served. Neither a tool retaining its source metadata object nor a caller
mutating a returned object can modify a published snapshot.

Tool names must be nonempty and must not contain leading or trailing whitespace. Rejecting rather
than trimming guarantees that every exact name returned by `tools/list` resolves through
`TryGetTool()`.

### 3. Publication is atomic

A candidate snapshot is built completely before the live registry is changed. The registry publishes it with a single `Interlocked.Exchange` or equivalent atomic reference swap.

Readers capture the current snapshot once per operation:

- `tools/list` deserializes caller-owned metadata from one snapshot's order-preserving discovery JSON.
- `tools/call` resolves a tool from `Tools` in one snapshot.

Readers do not acquire the rebuild lock. They observe either the complete previous snapshot or the complete replacement snapshot.

### 4. DI-owned and configuration-generated tool lifetimes differ

Built-in tools remain DI-owned application singletons because they are stateless and their execution paths already read current request/configuration state. Independently registered custom `IMcpTool` implementations are also DI-owned and remain part of every candidate; `IMcpTool` remains an extension point regardless of the implementation's `ToolType` value.

Only `DynamicCustomTool` objects generated from entity configuration are removed from automatic DI registration. They are configuration-generation objects and are recreated for every registry candidate. A name collision between an independently registered tool and a generated tool rejects the candidate rather than silently discarding either implementation.

This avoids treating the immutable DI service collection as a dynamic registry.

### 5. Refresh orchestration is separate from state storage

A singleton `McpToolRegistryRefreshService` coordinates initialization and hot-reload. It also implements `IHostedService` for normal HTTP-host startup.

Its responsibilities are:

1. Capture the current `RuntimeConfig` generation.
2. Obtain all DI-owned `IMcpTool` implementations, including independently registered custom tools.
3. Create fresh configuration-generated custom tools from the captured configuration.
4. Enrich custom tool schemas from refreshed database metadata.
5. Ask the registry to validate and build a complete candidate snapshot.
6. Verify that the captured configuration is still current.
7. Atomically publish the candidate.
8. Notify configured transports if advertised metadata changed.
9. Log success or failure.

`McpToolRegistry` owns registry invariants and publication. The refresh service owns lifecycle and dependencies. The bulk construction and publication surface is internal to the MCP runtime assembly rather than an advertised embedding API.

### 6. Custom tool creation is strict

`CustomMcpToolFactory` previously caught broad exceptions and skipped individual entities. Retaining
that behavior would allow a partial candidate to be published.

For registry initialization and refresh:

- Unexpected custom tool construction failures reject the complete candidate.
- The exception identifies the source entity.
- Empty names and case-insensitive collisions reject the candidate.
- Collisions are checked across built-in and custom tools.
- No candidate tool is silently omitted because construction failed.

Database metadata unavailability is a deliberate exception to this strict behavior. `DynamicCustomTool` already supports a configuration-derived schema fallback. The candidate may use that fallback, but the reason must be logged so reduced schema accuracy is visible.

### 7. Metadata initialization uses explicit dependencies

The metadata initialization path receives explicit dependencies:

```csharp
void InitializeMetadata(
    RuntimeConfig config,
    IMetadataProviderFactory metadataProviderFactory);
```

`McpMetadataHelper` has an overload that accepts `IMetadataProviderFactory` directly. Execution call sites retain the service-provider overload where resolving request services is appropriate.

This ensures that:

- Configuration-derived metadata belongs to the captured generation.
- Database metadata comes from the factory already refreshed earlier in the ordered pipeline.
- Candidate construction does not resolve arbitrary application services.

### 8. MCP receives a dedicated ordered hot-reload event

Add a named event such as:

```text
MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED
```

The event is added to `DabConfigEvents` and `HotReloadEventHandler`, then raised by `RuntimeConfigLoader` after `AUTHZ_RESOLVER_ON_CONFIG_CHANGED` and before GraphQL schema events.

The order is intentional:

1. Query and metadata dependencies are refreshed first.
2. Query and mutation engines are refreshed.
3. Authorization state is refreshed.
4. MCP builds and publishes tools that depend on those services.
5. GraphQL performs its independent schema lifecycle.

The refresh callback catches and logs hot-reload failures so an MCP candidate failure does not prevent later hot-reload handlers from running. Startup initialization remains strict because there is no previous valid registry to preserve.

### 9. Initialization is shared and idempotent

The refresh service exposes one idempotent initialization path.

Direct `EnsureInitialized()` calls remain no-ops after the current `RuntimeConfig` reference has
successfully been applied. The ordered hot-reload event is intentionally different: it always
rebuilds the current configuration because the configuration becomes visible before its metadata
event runs. This distinction prevents an out-of-band initialization during that interval from
permanently publishing the new configuration with the previous metadata generation.

#### HTTP mode

The host resolves the singleton through `IHostedService` early enough to subscribe to ordered
hot-reload events, but `StartAsync()` does not publish the initial snapshot. ASP.NET Core starts
hosted services before `Startup.Configure` finishes initializing database metadata.

`Startup.PerformOnConfigChangeAsync()` invokes a shared runtime-initialization helper. Configuration
capture and validation, `IMetadataProviderFactory` initialization, and the refresh service's
idempotent registry publication execute as one asynchronous operation under the
`FileSystemRuntimeConfigLoader` serialization gate. A file callback therefore cannot replace the
active configuration between those steps.

The DI registration must ensure that resolving the concrete refresh service and resolving `IHostedService` return the same object, for example by registering the concrete singleton and mapping `IHostedService` to it.

#### Stdio mode

Stdio intentionally does not start the ASP.NET Core host, so `Startup.Configure` does not initialize
database metadata. `McpStdioHelper` invokes the same serialized initial dependency operation used by
HTTP startup before starting the stdio loop.

This replaces the former duplicate per-tool registration path and gives both transports identical validation and metadata behavior.

### 10. A stale candidate is never published

Distinct file edits can produce overlapping hot-reload callbacks even though duplicate notifications for one file content are suppressed by `ConfigFileWatcher`.

`FileSystemRuntimeConfigLoader` serializes initial dependency construction and the complete reload
operation per loader instance. Its async-capable gate is held across initial configuration capture
and validation, metadata initialization, and MCP registry publication. For reload, the gate is
acquired before loading the new configuration and remains held until every synchronous
`SignalConfigChanged()` handler returns. Consequently, one complete generation finishes before
another path can replace the active configuration or begin updating dependencies.

#### Serialization layers and lock ordering

The refresh service retains its own writer gate and stale-generation guard as defense in depth:

1. Acquire the refresh writer gate.
2. Capture `RuntimeConfig config = runtimeConfigProvider.GetConfig()`.
3. Build the candidate against `config`.
4. Before publication, verify that `runtimeConfigProvider.GetConfig()` is still the same configuration object.
5. If it changed, discard the candidate without notifying clients.

A callback for the newer configuration will build the latest snapshot. The service tracks only successfully applied configuration references, so a later event can retry after an earlier failure.

The loader gate prevents mixed dependency generations within the normal serialized startup/reload
path. An out-of-band `EnsureInitialized()` can still run after a new configuration is installed but
before that configuration's metadata event. Such a call may publish an intermediate mixed snapshot.
The later ordered MCP event therefore bypasses config-reference idempotency and rebuilds after
metadata and authorization refresh, replacing the intermediate snapshot. The stale guard also
prevents an older, slower registry rebuild initiated outside the file-loader pipeline from
overwriting a newer registry generation. Neither mechanism provides transactional rollback after a
handler failure; that remains separate work.

The two locks protect different invariants. The loader gate spans configuration publication and all
ordered component callbacks. The refresh lock spans only registry candidate creation and
publication, including direct `EnsureInitialized()` calls that do not own the loader gate. Normal
startup and file reload acquire them in loader-then-refresh order. The refresh service never calls
back into an operation that acquires the loader gate while holding its own lock, and transport
notification occurs after the refresh lock is released. This fixed ownership prevents lock-order
cycles while retaining defense against out-of-band callers.

Reference identity is deliberately checked after candidate construction, immediately before
publication. Checking only before construction would not detect a configuration replacement that
occurs while tools and metadata are being materialized.

#### Shutdown contract

Loader shutdown does not wait to acquire this gate. It atomically stops admission, cancels callbacks
waiting to enter the gate, and requests cooperative cancellation of the active generation.
Cancellation is propagated through every DAB-owned ordered handler and metadata database operation,
including connection opening, schema discovery, query execution, access-token acquisition, and the
otherwise synchronous `FillSchema` command. Cancellation is checked before later MCP, GraphQL,
authorization, and logging publications, so a canceled owned generation does not continue through
the remaining pipeline.

The loader tracks each operation that owns the serialization gate and exposes an idempotent
`StopAsync(CancellationToken)` drain. An `IHostedService` invokes that drain during the host stopping
phase, before the root service provider disposes reload subscribers or their dependencies. The
host-supplied token bounds the drain according to `HostOptions.ShutdownTimeout`. Stdio composition,
which constructs but does not start the ASP.NET Core host, explicitly applies the same configured
timeout before disposing its host. Synchronous `FileSystemRuntimeConfigLoader.Dispose()` only stops
admission and requests cancellation; it does not reintroduce an unbounded wait after a host timeout.

.NET cannot forcibly terminate arbitrary synchronous subscriber code. A successful drain guarantees
that no reload operation remains and dependency disposal is safe. If the host timeout expires, the
host stops waiting; DAB-owned subscribers are designed to observe cancellation, while extension
subscribers are contractually required to observe `HotReloadEventArgs.CancellationToken` and avoid
indefinite blocking. Isolating non-cooperative extension callbacks from root-provider lifetime would
require a separately owned dependency container and is outside this feature's scope.

The resulting guarantees are:

| Shutdown path | Guarantee |
|---|---|
| Active DAB-owned handler observes cancellation | `StopAsync()` waits for the gate owner and cancellation callbacks to exit, then dependencies may be safely disposed. |
| Queued reload waiting for the loader gate | The loader token cancels the wait; the callback never becomes an active generation. |
| Extension handler ignores cancellation but exits before the host timeout | The drain still completes and dependency disposal remains ordered after the handler. |
| Extension handler remains blocked when the host timeout expires | `StopAsync()` observes the host token and returns cancellation; the host may continue disposal. No stronger lifetime guarantee is possible for arbitrary in-process synchronous code. |
| Direct synchronous `Dispose()` | New work is rejected and cancellation starts, but no drain is promised. A direct owner requiring dependency ordering must call `StopAsync()` first. |

Cancellation callbacks are also extension points and can block. Shutdown uses `CancelAsync()` and
tracks callback completion rather than running callbacks inline on the host stopping thread. The
tracked callback task participates in a successful drain, while the caller's shutdown token still
bounds the wait.

#### Watcher callback and resource-lifetime decision

`ConfigFileWatcher` invokes the reload entry point synchronously rather than creating a detached
task. This preserves the existing ordered event contract and ensures every admitted reload is
tracked as either a gate waiter or gate owner. File-system implementations may invoke another
callback concurrently; the loader gate serializes those callbacks and shutdown cancels queued
waiters. Introducing another queue would require separate task ownership, ordering, coalescing, and
drain rules without improving registry atomicity.

Watcher callback admission is disabled synchronously under a separate watcher-lifecycle lock.
Potentially blocking operating-system watcher resource disposal is scheduled on a background worker;
this resource cleanup is independent of the reload-operation drain and does not retain access to the
reload subscribers.

### 11. Existing tool-call safety is preserved

After a successful swap:

- Removed custom tools no longer resolve for new calls.
- Renamed custom tools resolve only under the new name.
- Disabled built-in tools remain in the lookup map, preserving current behavior in which execution returns a structured tool-disabled result.

A request that resolved a tool immediately before a swap may finish with that tool instance. `DynamicCustomTool.ExecuteAsync()` still validates the current configuration, entity type, custom-tool enablement, database metadata, and authorization before execution. This makes retirement of old custom tool objects safe without explicit cancellation or disposal.

### 12. Stdio sends tool-list change notifications

Production stdio composition registers a tool-list notifier and advertises
`tools.listChanged = true`. Alternative composition without that notifier advertises `false` so the
initialize response never promises a notification path that is unavailable.

After a successful noninitial refresh, send:

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/tools/list_changed",
  "params": {}
}
```

Notification rules:

- Do not notify for initial registry construction.
- Do not notify before the server successfully completes the `initialize` response and the client
    subsequently sends `notifications/initialized`.
- Do not queue a missed pre-initialization notification; the client has not yet established its cache and will request the initial list.
- Notify only when the advertised tool list or metadata changed.
- Send after atomic publication.
- Invoke transport notifiers after releasing the registry writer lock.
- Check initialization state before enqueueing delivery, then perform potentially blocking stdout
    I/O on a worker so the reload pipeline can continue to later handlers.
- Route the frame through the shared `McpStdoutWriter` so it cannot interleave with responses or logging notifications.
- Notification write failure is logged and does not roll back the registry.

A small stdio notifier service owns initialization state and queued frame writing. Multiple changes
while one stdout write is pending may be coalesced because any delivered invalidation causes the
client to request the latest complete snapshot. `McpStdioServer` tracks successful initialize-response
completion for the connection and marks the notifier initialized only when a subsequent
`notifications/initialized` arrives; an out-of-order notification is ignored. The refresh service
depends on zero or more tool-list notifiers; HTTP mode has no notifier registered in this iteration.
If the thread pool rejects the initial worker request, the notifier retains the pending invalidation
and starts one dedicated background fallback worker. This rare fallback keeps reload callbacks
nonblocking and avoids losing the only invalidation when no later configuration change occurs.
Because an abandoned client can leave stdout blocked indefinitely, `McpStdoutWriter.Dispose()`
first rejects future writes and releases the underlying writer only when its serialization lock is
immediately available. Host disposal therefore does not wait behind a blocked notification; in that
exceptional case the process-owned stdout handle is reclaimed when the process exits.

### 13. HTTP reads are immediately current, but HTTP push is deferred

The HTTP MCP SDK handlers execute against the registry singleton per request. Once the snapshot is swapped, the next `tools/list` and `tools/call` request sees it without rebuilding the MCP server.

HTTP `listChanged` capability is explicitly set to false and must not be advertised as supported
until HTTP notification delivery is implemented.

The installed MCP SDK can send a notification through an individual `McpServer` session, but broadcasting requires tracking all active sessions through an experimental `RunSessionHandler`. Depending on that experimental API is not necessary for registry correctness and is deferred to focused follow-up work.

### 14. Notify only for a semantic discovery change

Every applicable configuration hot-reload rebuilds and publishes a generation so custom tool instances align with the current configuration. However, an unrelated configuration change should not claim that the tool list changed.

The registry compares the previous and candidate advertised metadata in deterministic name order.
Before comparison it canonicalizes serialized JSON recursively by sorting object properties while
preserving array order. The comparison therefore ignores semantically irrelevant object insertion
order while still covering the complete tool metadata, including name, description, input schema,
and any future advertised fields.

Canonical JSON is comparison-only. The separately stored discovery representation preserves nested
object insertion order, including stored-procedure parameter order, so canonicalization does not
introduce a wire-order behavior change for clients that render schema properties in received order.

The swap still occurs when advertised metadata is equal, but `notifications/tools/list_changed` is emitted only when discovery metadata differs.

## Registry API Behavior

The production path changes from incremental registration to bulk replacement.

Conceptual operations are:

```csharp
IReadOnlyList<Tool> GetAdvertisedTools();

bool TryGetTool(string toolName, out IMcpTool? tool);

internal McpToolRegistryUpdateResult ReplaceAll(
    IEnumerable<IMcpTool> tools,
    RuntimeConfig config);
```

`ReplaceAll`:

1. Materializes the input once.
2. Retrieves and validates metadata once per tool.
3. Builds the case-insensitive lookup map.
4. Evaluates `IsEnabled(config)` for advertised metadata.
5. Sorts advertised metadata deterministically.
6. Compares advertised metadata with the current snapshot.
7. Atomically publishes the candidate.
8. Returns the new version and whether discovery metadata changed.

The registry no longer exposes incremental registration or caller-configured filtering. Those
helpers and the former startup initializer were implementation plumbing in the MCP runtime assembly,
not documented APIs from a supported reference package. Removing them leaves one construction model:
the refresh service builds and atomically publishes a complete configuration-aware generation.
Production discovery uses `GetAdvertisedTools()`, whose visibility and metadata were captured with
that same snapshot.

### Intentional cleanup of CLR-public implementation members

This change deliberately removes or narrows members that happened to be declared `public`, including
the `RegisterTool()` overloads, `GetEnabledTools()`, `InitializeAndRegisterTools()`,
`DynamicCustomTool.InitializeMetadata(IServiceProvider)`, and the former
`McpToolRegistryInitializer`. `CustomMcpToolFactory.CreateCustomTools()` now exposes its concrete
generated-tool result type, and bulk candidate construction/publication is `internal`. These are
intentional source/API-surface changes, not accidental compatibility omissions.

Those members were used only by DAB's own startup and test implementation. They were not documented
as extension APIs, and `Azure.DataApiBuilder.Mcp.dll` is runtime payload of the DAB tool rather than a
supported reference package. The supported `Microsoft.DataApiBuilder.Core` embedding package does
not expose the MCP implementation assembly. A consumer that manually referenced the runtime DLL and
called these methods was depending on unsupported internals solely because their CLR visibility was
too broad. Retaining obsolete shims would preserve two conflicting construction models and undermine
the atomic-snapshot invariant, so the implementation-only surface is removed instead.

Cancellation-aware overloads added to the supported `Microsoft.DataApiBuilder.Core` interfaces are
different: they use default interface implementations that check pre-cancellation and delegate to
the established parameterless members. Existing third-party implementations therefore remain
source- and binary-compatible. DAB's built-in implementations override the overloads to propagate
cancellation through database I/O.

### Cancellation API compatibility and legacy behavior

The cancellation additions intentionally extend rather than replace established contracts:

- `IMetadataProviderFactory`, `ISqlMetadataProvider`, and `IQueryExecutor` retain all legacy
    members. Their new token overloads are default interface methods that reject an already canceled
    call and otherwise delegate to the legacy member.
- A legacy third-party implementation therefore compiles and runs unchanged. Once its legacy call
    begins, DAB cannot impose cooperative cancellation on work that does not accept a token; this is
    the compatibility trade-off. DAB-owned implementations override the new members and carry the
    token to connection opening, commands, readers, retries, and access-token acquisition.
- `HotReloadEventArgs` adds a read-only token and a three-argument constructor while retaining the
    original two-argument constructor. Existing compiled and source callers remain valid, while the
    file loader uses the new overload with its owned shutdown token.
- Existing public and protected Config/Core members retain their original signatures and virtual
    slots. `TryLoadConfig()`, `SignalConfigChanged()`, `PopulateTriggerMetadataForTable()`,
    `GenerateAutoentitiesIntoEntities()`, and `QueryAutoentitiesAsync()` delegate to or are invoked
    by explicit token-aware overloads. Existing compiled callers and derived providers therefore do
    not need to recompile or add overrides.
- `IMcpToolRegistryRefreshService` retains parameterless `EnsureInitialized()` and provides the
    token overload through a default implementation. The shared startup path uses the token overload;
    existing test or embedding implementations that only provide the original member continue to
    work.
- The otherwise synchronous provider `FillSchema()` call cannot be made truly asynchronous. DAB
    registers cancellation to invoke `DbCommand.Cancel()` and converts a provider exception observed
    after cancellation into `OperationCanceledException`. This is best-effort and remains dependent
    on the database provider honoring command cancellation.

`FileSystemRuntimeConfigLoader.StopAsync(CancellationToken)` is public because the Service assembly
and direct hosts must coordinate Config-owned work before disposing their own dependency graph. It
is a lifecycle operation, not a replacement for `Dispose()`; repeated calls join the same shutdown
and may use different caller-side timeout tokens.

## Detailed Flows

### Initial HTTP startup

```mermaid
sequenceDiagram
    participant Host
    participant Startup
    participant Refresh as McpToolRegistryRefreshService
    participant Factory as CustomMcpToolFactory
    participant Metadata as IMetadataProviderFactory
    participant Registry as McpToolRegistry

    Host->>Refresh: StartAsync (subscribe only)
    Startup->>Metadata: InitializeAsync(loader cancellation token)
    Metadata-->>Startup: DB metadata ready
    Startup->>Refresh: EnsureInitialized(loader cancellation token)
    Refresh->>Refresh: Capture current RuntimeConfig
    Refresh->>Factory: Create custom tools(config)
    Factory-->>Refresh: Fresh custom tools
    Refresh->>Metadata: Resolve refreshed DB metadata
    Refresh->>Refresh: Initialize custom schemas
    Refresh->>Registry: ReplaceAll(built-ins + custom, config)
    Registry-->>Refresh: Published version
    Note over Refresh,Registry: No list-changed notification on initial construction
```

An invalid or duplicate tool causes startup to fail, preserving current strict startup behavior.

### Initial stdio startup

```mermaid
sequenceDiagram
    participant Helper as McpStdioHelper
    participant Refresh as McpToolRegistryRefreshService
    participant Metadata as IMetadataProviderFactory
    participant Registry as McpToolRegistry
    participant Server as McpStdioServer

    Helper->>Metadata: InitializeAsync(loader cancellation token)
    Metadata-->>Helper: DB metadata ready
    Helper->>Refresh: EnsureInitialized(loader cancellation token)
    Refresh->>Registry: Build and publish initial snapshot
    Helper->>Server: RunAsync()
    Server->>Server: Handle initialize
    Server->>Server: Handle notifications/initialized
    Server->>Server: Mark tool-list notifier initialized
```

### Successful hot-reload

```mermaid
sequenceDiagram
    participant Loader as RuntimeConfigLoader
    participant Metadata as MetadataProviderFactory
    participant Auth as AuthorizationResolver
    participant Refresh as McpToolRegistryRefreshService
    participant Registry as McpToolRegistry
    participant Notifier as Stdio notifier

    Loader->>Metadata: METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED
    Metadata-->>Loader: Metadata refreshed
    Loader->>Auth: AUTHZ_RESOLVER_ON_CONFIG_CHANGED
    Auth-->>Loader: Authorization refreshed
    Loader->>Refresh: MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED
    Refresh->>Refresh: Capture config and build candidate
    Refresh->>Refresh: Verify captured config is still current
    Refresh->>Registry: Atomic ReplaceAll
    Registry-->>Refresh: Published with discovery changes
    Refresh->>Notifier: NotifyToolsListChanged()
```

### Failed hot-reload candidate

```mermaid
sequenceDiagram
    participant Loader as RuntimeConfigLoader
    participant Refresh as McpToolRegistryRefreshService
    participant Registry as McpToolRegistry

    Loader->>Refresh: MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED
    Refresh->>Refresh: Build candidate
    Refresh--xRefresh: Invalid name, collision, or construction failure
    Refresh->>Refresh: Log error with entity/tool context
    Note over Registry: Previous immutable snapshot remains active
    Refresh-->>Loader: Return without throwing
```

## Concurrency Model

### Reads

Registry reads capture one snapshot reference and do not lock. Immutable lookup and metadata collections are safe for concurrent requests.

### Writes

Initialization and refresh operations use one writer gate. Candidate construction happens while registry rebuilds are serialized, but the live snapshot remains available to readers.

### In-flight requests

An in-flight `tools/list` serializes the snapshot it captured. It returns either the complete old list or complete new list.

An in-flight `tools/call` retains the resolved tool instance. A later swap does not invalidate that object. Dynamic custom tools revalidate current configuration and authorization before database execution.

### Multiple configuration changes

The per-loader gate ensures initial metadata and registry construction cannot overlap a file reload,
and that one file generation completes all ordered handlers before the next notification begins
loading. The stale-generation guard additionally prevents publication when the active `RuntimeConfig`
changes during candidate construction through another code path. The latest callback eventually
publishes the latest generation.

Serialization is scoped to each `FileSystemRuntimeConfigLoader`; independent loaders do not block one
another. Transactional rollback is still tracked separately.

## Failure Semantics

| Scenario | Registry result | Client notification | Hot-reload pipeline |
|---|---|---|---|
| Initial construction succeeds | Initial snapshot published | None | Startup continues |
| Initial construction fails | No usable snapshot | None | Startup fails |
| Hot-reload construction succeeds and metadata changes | New snapshot published | Stdio notification after initialization | Continues |
| Hot-reload construction succeeds with equivalent metadata | New snapshot published | None | Continues |
| Custom tool construction fails | Previous snapshot retained | None | Error logged; continues |
| Tool name is invalid or duplicated | Previous snapshot retained | None | Error logged; continues |
| DB metadata is unavailable but config fallback works | New snapshot published with fallback schema | Notify if metadata changed | Warning logged; continues |
| Candidate becomes stale before publication | Candidate discarded | None | Newer callback is expected to refresh |
| Stdio notification write fails | New snapshot remains published | Delivery failed | Error logged; continues |

### Temporary limitation before transactional hot-reload

The current DAB hot-reload pipeline commits the new `RuntimeConfig` before component refresh callbacks complete. If MCP candidate construction fails, DAB can temporarily have a newer runtime configuration and metadata generation with the previous MCP registry snapshot.

This design chooses the safest local behavior:

- Never publish a partial or invalid registry.
- Keep the previous discovery snapshot.
- Let stale custom tools fail safely through current execution-time validation.
- Retry on a later configuration event.
- Log the degraded condition clearly.

Once transactional hot-reload exists, MCP candidate construction should become a transaction participant and reject the complete configuration candidate before any component publishes it.

## Configuration Behavior

### Changes applied live

The following changes are reflected after a successful registry refresh:

- Adding a stored-procedure entity with `mcp.custom-tool: true`.
- Removing such an entity.
- Enabling or disabling `mcp.custom-tool` on a stored-procedure entity.
- Renaming a custom-tool entity and therefore its normalized tool name.
- Changing an entity description.
- Changing configuration-declared stored-procedure parameter metadata.
- Changing DB-discovered stored-procedure parameter metadata.
- Changing global built-in DML tool enablement flags.
- Introducing or resolving a custom/custom or custom/built-in name collision.

### Changes that remain startup-bound

- `runtime.mcp.enabled`.
- `runtime.mcp.path`.
- HTTP route registration.
- Existing session initialization instructions.

The actual route and service registration remain those selected at startup. If these options are modified in a hot-reloaded file, a restart is required for them to take effect consistently.

## Dependency Injection Changes

The intended registrations are:

- `McpToolRegistry`: singleton.
- Built-in `IMcpTool` implementations: singleton.
- Independently registered custom `IMcpTool` implementations: DI-owned and retained across generations.
- `McpToolRegistryRefreshService`: singleton.
- `IHostedService`: resolves the same refresh-service singleton.
- Configuration-generated `DynamicCustomTool` instances: not registered in DI.
- Stdio tool-list notifier: singleton, registered only in stdio mode.

The refresh service preserves every implementation in its DI-provided `IEnumerable<IMcpTool>`. Reflection-based discovery of DAB's own implementations continues to exclude `DynamicCustomTool`; those instances come only from the per-generation factory during normal startup and reload.

## Implementation Map

The implementation is split across the following touchpoints:

### Config project

- [DabConfigEvents.cs](../../src/Config/DabConfigEvents.cs): add the MCP registry event name.
- [HotReloadEventHandler.cs](../../src/Config/HotReloadEventHandler.cs): register the event slot.
- [RuntimeConfigLoader.cs](../../src/Config/RuntimeConfigLoader.cs): raise the event at the agreed position.
- [FileSystemRuntimeConfigLoader.cs](../../src/Config/FileSystemRuntimeConfigLoader.cs): serialize initial dependency construction and complete file-reload pipelines with one async-capable per-loader gate.

### MCP project

- [McpToolRegistry.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpToolRegistry.cs): immutable snapshots, bulk replacement, and atomic reads/publication.
- Remove the former `McpToolRegistryInitializer`; the refresh service owns initialization.
- [McpServiceCollectionExtensions.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpServiceCollectionExtensions.cs): register the shared refresh service and stop registering custom tools in DI.
- [CustomMcpToolFactory.cs](../../src/Azure.DataApiBuilder.Mcp/Core/CustomMcpToolFactory.cs): strict candidate creation.
- [DynamicCustomTool.cs](../../src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs): explicit metadata dependencies and removal of the stale-metadata assumption.
- [McpMetadataHelper.cs](../../src/Azure.DataApiBuilder.Mcp/Utils/McpMetadataHelper.cs): optional explicit metadata-factory overload.
- [McpServerConfiguration.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpServerConfiguration.cs): list directly from one registry snapshot.
- [McpStdioServer.cs](../../src/Azure.DataApiBuilder.Mcp/Core/McpStdioServer.cs): list from the snapshot and mark notification readiness.
- New stdio tool-list notifier type or types.

### Service project

- [RuntimeInitializationHelper.cs](../../src/Service/Utilities/RuntimeInitializationHelper.cs): coordinate serialized configuration validation, metadata initialization, and initial registry publication for both transports.
- [McpStdioHelper.cs](../../src/Service/Utilities/McpStdioHelper.cs): invoke shared idempotent initialization.
- [Program.cs](../../src/Service/Program.cs): register the stdio notifier with the shared stdout writer.

### Tests

- Extend registry, factory, dynamic-tool, stdio, and hot-reload tests.
- Add focused refresh-service and concurrency tests.

## Testing Strategy

### Registry unit tests

1. Bulk replacement publishes unique tools.
2. Names are case-insensitive.
3. Empty and whitespace names reject the candidate.
4. Built-in/custom and custom/custom collisions reject the candidate.
5. A rejected replacement leaves the exact previous snapshot active.
6. Added custom tools become discoverable and callable.
7. Removed custom tools disappear from lookup and discovery.
8. Renamed custom tools remove the old name and add the new name atomically.
9. Built-in visibility is computed from the candidate configuration.
10. Advertised tools have deterministic ordering.
11. Equivalent metadata does not report a discovery change.
12. Name, description, input-schema, addition, removal, and visibility changes report a discovery change.
13. Concurrent readers observe no exceptions or partial snapshots during repeated swaps.
14. Served input-schema properties preserve their source insertion order even though comparison is canonicalized.

### Refresh-service unit tests

1. Initial construction uses all DI-owned tools and newly created configuration-generated tools.
2. Every refresh creates new custom tool instances.
3. Built-in instances are reused.
4. Metadata initialization uses the captured configuration and refreshed metadata factory.
5. A fallback schema is published and logged when DB metadata is unavailable.
6. Construction failure preserves the previous registry.
7. Startup failure propagates.
8. Hot-reload failure is caught and logged.
9. A stale candidate is discarded.
10. Repeated direct initialization for an already successfully applied configuration does not publish duplicate generations unnecessarily.
11. An ordered MCP event rebuilds after refreshed metadata even when an out-of-band initialization already applied the same configuration reference.
12. Notifications occur only after a successful noninitial semantic discovery change.
13. Independently DI-registered custom implementations remain published after refresh.

### Handler and transport tests

1. HTTP `tools/list` reads only registry snapshot metadata.
2. HTTP `tools/call` resolves from the current snapshot.
3. Stdio `tools/list` reads only registry snapshot metadata.
4. Stdio ignores an out-of-order `notifications/initialized` and does not notify before a complete
    successful initialization handshake.
5. Stdio does not notify for initial construction.
6. Stdio emits the exact `notifications/tools/list_changed` frame after an applicable refresh.
7. Stdio serializes notifications through `McpStdoutWriter` without interleaving.
8. Stdio notification failure does not revert the registry.
9. HTTP does not advertise `listChanged` in this iteration.
10. A blocked stdio writer does not block the ordered hot-reload pipeline.
11. The real HTTP handler omits tools disabled in the published registry snapshot.
12. The physical HTTP failure-path test observes completion of the rejected candidate before
    checking retained discovery and writing a recovery configuration.
13. Stdio advertises `listChanged = false` when no stdio notifier is composed.
14. A rejected primary worker schedule retains and delivers the pending notification through the fallback worker.
15. Multiple changes while a write is blocked coalesce to one additional pending notification.
16. Disposing the shared stdout writer while a notification write is blocked returns immediately,
    rejects later writes, and does not corrupt the in-flight frame when the pipe resumes.

### Hot-reload integration tests

1. The MCP registry event runs after metadata and authorization refresh.
2. Enabling a custom tool makes it appear after a real configuration reload.
3. Disabling or removing a custom tool removes it.
4. Description changes are reflected.
5. Stored-procedure input-schema changes are reflected.
6. Built-in DML visibility changes are reflected.
7. A duplicate name leaves the previous registry active.
8. Correcting a failed configuration allows the next refresh to succeed.
9. Rapid successive configurations cannot publish an older registry after a newer one.
10. A reload paused before metadata refresh cannot overlap initial metadata and registry construction;
    the final advertised schema comes from the reload generation's database metadata.
11. A physical stdio config-file write traverses the complete ordered pipeline, emits exactly one
    notification for one net-new file content, and returns updated discovery.
12. Shutdown cancels an active cancellation-aware reload handler, prevents later ordered handlers
    from running, cancels callbacks queued on the loader gate, and waits for the active handler to
    exit before the drain completes.
13. Hosted shutdown drains active reload work before earlier hosted services stop and before the
    root provider disposes reload subscribers or their dependencies.
14. Hosted shutdown observes its supplied cancellation token when a non-cooperative subscriber
    prevents the drain from completing.
15. Potentially blocking operating-system watcher disposal does not delay the reload-operation
    drain after watcher callbacks have been synchronously disabled and detached.

Database-backed schema tests should reuse existing MCP stored-procedure fixtures where database metadata is required. Pure membership, collision, notification, and atomicity behavior should remain unit-testable without a live database.

## Logging and Diagnostics

Use structured logs from the refresh service for:

- Initial registry version and built-in/custom/advertised counts.
- Successful hot-reload version and counts.
- Whether advertised discovery metadata changed.
- Candidate discard because a newer configuration became active.
- Config-schema fallback, including entity name and reason.
- Candidate failure, including entity/tool context and exception.
- Notification delivery failure.
- Primary notification-worker scheduling failure and fallback-worker creation failure.

Do not log connection strings, stored-procedure argument values, or other secrets.

## Security Considerations

The registry refresh does not change authentication or authorization policy.

- Built-in and custom tools continue to authorize at execution time.
- `DynamicCustomTool` continues to validate current entity existence, type, enablement, metadata, and role permissions.
- Publishing a tool in `tools/list` does not bypass execution authorization.
- Retaining an old snapshot after failed refresh does not authorize obsolete execution because current configuration and authorization checks still run.

## Alternatives Considered

### Mutate the existing dictionary in place

Rejected because concurrent reads could observe partial state or race with dictionary mutation. It also makes rollback difficult.

### Rebuild custom tools on every `tools/list`

Rejected because it moves validation and metadata work into request handling, repeats work, makes failures request-time failures, and does not naturally solve `tools/call` lookup consistency.

### Create custom tools lazily on `tools/call`

Rejected because clients still need accurate discovery metadata and collisions should be rejected before invocation.

### Subscribe directly to `RuntimeConfigProvider.GetChangeToken()`

Rejected because that signal occurs before the ordered metadata, engine, and authorization refresh events. The registry could publish schemas derived from stale dependencies.

### Dynamically add and remove DI registrations

Rejected because the built service provider is not a dynamic registry. Rebuilding it would create duplicate singleton graphs and lifecycle problems.

### Rebuild the MCP server or HTTP endpoints

Rejected because handlers already dereference the registry per request. Rebuilding transports and endpoint routing is unnecessary and would complicate active sessions.

### Implement HTTP broadcast notifications now

Deferred because registry correctness does not require it and the current SDK exposes active-session interception through an experimental API.

### Queue file-watcher reloads onto detached tasks

Rejected because the existing ordered pipeline defines completion synchronously. A detached queue
would need a second ordering and coalescing model, explicit task ownership, exception observation,
and another shutdown drain. Keeping callbacks synchronous and serializing them at the loader gate
makes every admitted operation observable to shutdown.

### Wait without a timeout until every reload subscriber exits

Rejected because extension callbacks are arbitrary synchronous code and can block forever. An
unbounded wait would make `HostOptions.ShutdownTimeout` ineffective. The implementation requests
cooperative cancellation and drains normally, but honors the host's timeout when code does not
cooperate.

### Dispose dependencies immediately without draining reload work

Rejected because DAB-owned reload handlers use singleton metadata, query, authorization, and
logging dependencies. Disposing those while a cooperative generation is unwinding creates avoidable
use-after-dispose races. The shutdown hosted service is registered last so it stops first and drains
the loader before earlier hosted services and the root provider stop.

### Isolate reload subscribers in a separately owned service provider

Deferred. A child provider could remain alive after the root host timeout and would provide a
stronger lifetime boundary for non-cooperative extensions, but it would duplicate or proxy a large
singleton graph and change existing hot-reload ownership. That is disproportionate to this registry
feature and does not make arbitrary code forcibly cancelable.

### Run cancellation callbacks inline on the stopping thread

Rejected because token registrations are extension points and may themselves block. `CancelAsync()`
allows cancellation to be requested without trapping the host stopping thread inside a callback,
while callback completion still participates in a successful bounded drain.

### Preserve obsolete public registry methods as compatibility shims

Rejected because `RegisterTool()`, caller-configured filtering, and bulk initialization expose a
second incremental construction path that can violate the atomic-generation invariant. These were
implementation members of the unsupported MCP runtime assembly. Supported Core interface additions
instead use default methods to preserve compatibility.

### Notify for every published generation

Rejected because configuration and metadata generations can change without changing MCP discovery.
Notifications are client cache invalidations, so emitting them for equivalent advertised metadata
causes unnecessary `tools/list` traffic. The implementation still publishes fresh tool instances
but compares canonical discovery metadata before notifying.

### Canonicalize the JSON served to clients

Rejected because recursively sorting schema properties would alter stored-procedure parameter wire
order. The registry stores separate order-preserving serving JSON and canonical comparison JSON so
semantic comparison does not change client-visible ordering.

### Block disposal until an abandoned stdio write completes

Rejected because a client can stop reading while leaving the pipe open, causing the write lock to
remain held indefinitely. Disposal marks the writer closed to new work and releases resources only
when the lock is immediately available; otherwise process teardown reclaims the process-owned
stdout handle.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Candidate built from stale configuration | Capture config, serialize registry rebuilds, and verify reference identity before publication. |
| Candidate uses stale DB metadata | Place MCP event after metadata refresh and inject metadata factory explicitly. |
| Invalid custom tool removes otherwise valid tools | Reject the candidate and retain the complete previous snapshot. |
| Old tool runs after swap | Execution revalidates current configuration and authorization. |
| Unrelated reload sends unnecessary notification | Compare deterministic advertised metadata before notifying. |
| Stdio notification corrupts JSON-RPC output | Use the shared locked `McpStdoutWriter`. |
| HTTP clients cache old list | Every explicit list request is current; HTTP push is tracked as follow-up. |
| Config commits while MCP refresh fails | Preserve valid registry, log degraded state, and rely on future transactional hot-reload work for global rollback. |
| Mutable SDK metadata is changed by a tool or caller | Deep-clone metadata into the candidate and return caller-owned clones from discovery. |
| Raw JSON order creates false discovery changes | Compare separately canonicalized JSON while preserving source property order in served JSON. |
| Startup publishes before DB metadata exists | Hosted service subscribes only; the shared startup helper publishes under the loader gate after metadata initialization. |
| Shutdown cancels only gate waiters but not active database work | Propagate the loader token through DAB-owned handlers, metadata providers, query executors, connections, commands, and token acquisition. |
| Synchronous `FillSchema()` ignores cancellation | Register `DbCommand.Cancel()` as best-effort provider cancellation and translate cancellation-triggered provider failures. |
| Cancellation callback blocks the host stopping thread | Request cancellation with `CancelAsync()` and include callback completion in the bounded drain. |
| Non-cooperative extension prevents shutdown drain | Honor the host timeout and document that extension callbacks must observe the event token; stronger isolation is follow-up architecture. |
| OS watcher disposal blocks behind a callback | Disable and detach admission synchronously, then dispose the watcher independently on a background worker. |
| Stdio client stops reading stdout | Keep notification I/O off the reload path and make writer disposal reject new writes without waiting for a blocked write lock. |
| Notification queue grows while stdout is blocked | Store one pending invalidation bit and coalesce all intermediate changes. |
| New cancellation overloads break third-party Core implementations | Use default interface implementations that precheck cancellation and delegate to legacy members. |

## Acceptance Criteria

The implementation is complete when:

1. HTTP and stdio startup use one shared registry initialization path.
2. Custom tools are no longer DI singletons tied to startup configuration.
3. A successful hot-reload atomically updates custom tool membership and metadata.
4. `tools/list` and `tools/call` never observe a partially rebuilt registry.
5. `tools/list` no longer combines a registry generation with an independently read configuration generation.
6. Duplicate or invalid candidate tools cannot partially update the registry.
7. A hot-reload rebuild failure leaves the previous registry usable and does not stop later hot-reload handlers.
8. A startup registry failure still fails startup.
9. Metadata initialization uses the exact captured configuration and refreshed metadata provider.
10. A stale rebuild cannot overwrite a newer registry generation.
11. An initialized stdio client receives `notifications/tools/list_changed` only after a successful semantic discovery change.
12. HTTP requests immediately observe the new snapshot without experimental session APIs.
13. Existing execution-time configuration and authorization validation remains intact.
14. Unit, concurrency, transport, and hot-reload tests cover the behaviors listed in this document.
15. Independently DI-registered `IMcpTool` extensions survive startup and every refresh.
16. Initial construction and every DAB-owned reload generation observe loader shutdown cancellation.
17. A successful loader drain completes before dependent hosted services and the root provider are disposed.
18. A non-cooperative extension cannot make the host ignore `HostOptions.ShutdownTimeout`.
19. Existing Core interface implementers and legacy `HotReloadEventArgs` construction remain source- and binary-compatible.
20. Blocked watcher or stdio resource cleanup cannot indefinitely delay coordinated host shutdown.

## Follow-Up Work

The following work remains intentionally separate:

- Transactional application-wide hot-reload preparation, commit, and rollback.
- Dynamic MCP endpoint enablement and path changes.
- HTTP session tracking and `notifications/tools/list_changed` broadcast.
- Dynamic initialize instructions for future HTTP sessions, if required.
- Separate dependency ownership or process isolation for non-cooperative extension callbacks, if a
    stronger post-timeout lifetime guarantee becomes a product requirement.
- Truly asynchronous schema-table discovery if database providers add an alternative to synchronous
    `DbDataAdapter.FillSchema()`; command cancellation remains best-effort until then.
