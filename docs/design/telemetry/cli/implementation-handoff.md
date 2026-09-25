# CLI telemetry implementation handoff

Implement the [CLI functional specification](functional-spec.md) together with the [shared contract](../shared-contract.md). The three CLI events, installation-profile store, and explicit launch bridge are new work; the Engine's existing telemetry does not provide them automatically.

## Required baseline

The integration reference is [e85f3338](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a) on `dev/aaronburtle/engine-telemetry-phase1`, or a descendant containing its Engine telemetry. The older code on `dev/aaronburtle/engine-telemetry` does not acquire those components just by receiving these documents.

The [Engine handoff](../engine/implementation-handoff.md) documents reusable components, resource limits, SDK prerequisites, and current coverage limits. Keep existing Engine event meanings and tests intact. This work adds CLI collection and the minimal explicit integration boundary; it is not a second engine instrumentation implementation or production-enablement change.

## Existing command and launch paths

| Boundary | Current code and implication |
| --- | --- |
| Process entry and dispatcher | [Cli.Program.Main / Execute](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/Program.cs): `.env` loading and early output flags precede `Execute`; `ParseArguments(...).MapResult(...)` dispatches one invocation. |
| Registered names and options | [CLI command types](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/Commands) and [Options](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/Options.cs): approved metadata, not raw argument strings, defines the vocabulary. |
| Parser failures and control requests | [DabCliParserErrorHandler.ProcessErrorsAndReturnExitCode](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/DabCliParserErrorHandler.cs): help/version use the parser error path but return success; other parser errors return failure. |
| Successful configuration creation | [ConfigGenerator.TryGenerateConfig](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/ConfigGenerator.cs#L44-L82): resolves explicit/default/environment-selected filename, creates the model, then returns `WriteRuntimeConfigToFile(...)`. The identity boundary is the successful write, not `TryCreateRuntimeConfig`. |
| Engine handoff | [ConfigGenerator.TryStartEngineWithOptions](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/ConfigGenerator.cs#L3033-L3156): validates and resolves runtime configuration, then calls `Service.Program.StartEngine` directly in the same process. |
| Engine lifetime | [Service.Program.StartEngine / StartEngineCore](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Program.cs#L95-L190): web `host.Run()` blocks until shutdown; MCP stdio runs its serving loop before returning. `start` command completion therefore follows that call, not readiness. |
| Export helper | [Exporter.ExportGraphQL](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli/Exporter.cs#L96-L145): the nongeneration path starts the service using `Task.Run` and the same helper, not an OS subprocess. It remains one top-level `export` invocation. |

The registered verb set at this baseline is `init`, `add`, `update`, `start`, `validate`, `export`, `add-telemetry`, `configure`, `auto-config`, `auto-config-simulate`, and `appname`. `auto-config-simulate` is one verb, not a guessed two-word command. `add-telemetry` configures customer observability; its existence is not permission to enable product telemetry.

## Invocation instrumentation

Establish one optional CLI scope around top-level parsing/dispatch and complete it exactly once. `Main` and `Execute` must not independently create duplicate scopes for the same invocation. Direct `Execute` calls used by tests or embedded callers need a deliberate disabled/injected policy rather than unintended environment-driven collection.

- Capture monotonic start time, a fresh CLI session, and closed context only after enablement checks. Repeated invocations in one process have distinct sessions.
- Map the selected registered verb/type to a fixed command category. Use explicit approved option identifiers with normalized aliases and a bounded representation of supplied presence; property defaults are not presence.
- Do not collect argument values by serializing the options object, joining raw args, reading parser error text, or copying logger messages. A grammar-aware presence adapter must distinguish an actual option from a value resembling an option, and honor aliases, separators, and malformed input without emitting that input.
- Preserve parser/handler results, exceptions, cancellation, and normal output. Do not reinterpret successful help/version as parse failure. Unknown input remains a closed unknown category.
- The returned integer alone cannot distinguish validation failure from execution failure when both share a return code. Add narrow, typed outcome information at established boundaries where necessary; do not infer a category from customer-facing messages or change command semantics to obtain it.
- Record command completion when the command actually returns or its failure/cancellation is observed. The launch event must be produced earlier at an actual engine handoff; otherwise a long-running `start` would hide its launch until shutdown.

## Profile identity, configuration identity, and notice

Introduce a safe per-user-profile installation store; no such store exists at the baseline. Installation persistence failure means **unavailable**, not a new random temporary installation ID. Keep the identity stable across upgrades, handle concurrent initialization, and document reset. Bound state size and validate file ownership/type/version; do not reuse an unsafe file or repair owner state. Store no pending events in that file.

The existing [EngineTelemetryIdentityStore](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Core/Telemetry/Product/EngineTelemetryIdentityStore.cs) is an API-configuration store, not an installation store. Preserve its sidecar suffix, version-1 `version`/`apiId` payload, random UUID identity, atomic publication, and safe-storage behavior so an engine reuses the CLI-created API identity.

Two different operations are needed:

1. **Successful configuration creation:** use the exact resolved filename after `WriteRuntimeConfigToFile` succeeds, allocate/reuse the API identity, then complete the CLI event. Do not allocate on parsing, in-memory model creation, failed writes, or an already-existing target that `init` rejected.
2. **Later configuration-aware commands:** look up available identity for the actual root/merged configuration. Do not blindly invoke a create-capable resolver on every `validate`, `add`, or unrelated command: `EngineTelemetryIdentityStore.Resolve` can create missing state. Preserve the contract for configurations created outside the CLI and omit linkage when the target is unavailable or ambiguous.

Factor a narrow shared product-identity boundary if necessary; do not copy a second incompatible API store or grant broad access merely to manipulate private fields. CLI installation storage and API sidecars remain separate. Shared-profile identity never joins unrelated configurations. An ephemeral API ID cannot bridge separate invocations without saved state.

Show the notice before transmission on stderr without disturbing help output or MCP JSON-RPC. The current Engine cannot persist notice state, so repeated notices are supported; adding profile notice persistence must not add a disabled-path filesystem read or hide a required notice when saving fails. Exactly-once delivery of first-run events is not promised by file persistence.

## Explicit launch context

The existing Engine factory creates its own session ID and accepts no CLI parent context. A launch event alone therefore cannot implement linkage.

Add a minimal typed handoff through the actual CLI-to-service call boundary. It must carry a newly selected engine-run ID, actual CLI session, available configuration identity/stability, available installation identity as applicable, and a closed launch-source category. No paths, argument arrays, roles, connection strings, or exception objects belong in the telemetry context.

- Emit launch intent only after CLI preflight accepts the handoff. The engine must adopt the intended session ID before `process_started`, including initialization-failure paths.
- Preserve existing direct `StartEngine` overloads and behavior. Calls without a bridge continue to create independent engine sessions and omit parent/installation linkage.
- For same-process web/stdio launches, pass an explicit object rather than mutable process-global environment markers. Scope it to the invocation/launch; concurrent launches and export helper tasks must not share stale state.
- Resolve which root configuration the service actually accepts, including CLI environment/merge selection. If CLI and engine would use different sidecars, fix the shared resolution/identity boundary rather than exporting paths, hashing content, or inventing linkage.
- A helper engine started by `export` needs an accurate launch source and lifetime. It is not a second `start` command event, and schema discovery does not establish successful API usage. Do not change the exporter command's serving behavior as a side effect of instrumentation.

Separate invocations of `init` and `add` can correlate only through valid available configuration identity. Launch context binds one actual engine run; it is not a stored replay token, proof of readiness, or proof of successful usage.

## Event model and sender reuse

The Engine event model is engine-shaped: for example, [ApplicationInsightsEventAttributes](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service/Telemetry/ApplicationInsightsEventAttributes.cs) always maps an engine configuration epoch. Do not manufacture engine epochs or configuration snapshots merely to fit CLI events into it. Define the small CLI event contract and applicable envelope explicitly; preserve existing Engine wire meanings if common plumbing is factored.

Reuse the supported Azure Monitor custom-event path and bounded delivery implementation where compatible, not customer logging, an extra telemetry SDK, or a handwritten protocol. Keep product destination selection independent of `add-telemetry` and customer observability settings. A valid destination alone cannot enable collection.

The Engine baseline requires explicit synthetic mode, valid product routing, no umbrella veto, and both SDK statistics opt-outs before SDK initialization. CLI tests and any deliberate live validation must obey the same prerequisites. Product code must not set global environment variables or `AppContext` flags to satisfy them.

SDK 1.9.0 caches transmitters by connection string. Creating a CLI exporter beside an Engine exporter can share transport/lifetime even when their logger factories differ. Establish and test explicit ownership for the product sender across overlapping CLI/engine lifetimes; closing one collector must not dispose another's resources, and queues/flush budgets must remain bounded. This is especially relevant to the in-process `start` and asynchronous export helper paths. Arbitrary customer-exporter coexistence is not solved by this work automatically.

Preserve no-disk-buffering, finite retries, immutable event IDs, source-IP suppression, payload bounds, cancellation, and diagnostic-field guards. Use no telemetry network wait in parsing/command execution; only the bounded final drain may await delivery. Do not serialize a raw error or option value to explain a telemetry failure.

## Concrete contracts to finish during implementation

These are required implementation details, not reasons to reopen the functional scope:

- Versioned CLI event/property names and types, fixed command/option-presence registry, parser/control-request mapping, failure categories, and field-length/count limits.
- Profile identity/notice file location and version, safe read/write/reset behavior on supported platforms, first-run race handling, and unavailable-state semantics.
- Separate lookup-only versus create-after-success API identity operations, including actual root-config resolution, same-run ephemeral linkage, and no configuration I/O added solely to infer an otherwise unknown target.
- Typed launch-context API and sender ownership across CLI and Engine, with compatible direct-service entry points and no new required customer configuration.
- Bounded queue/retry/drain settings and test injection seams. Reuse the Engine's established limits where applicable; document and validate any different CLI limit rather than silently using SDK defaults.

## Required validation

Extend [CLI tests](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Cli.Tests), including parser/handler and configuration-write paths, and preserve [Engine telemetry tests](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/src/Service.Tests/Telemetry).

| Area | Evidence required |
| --- | --- |
| Gate and noninterference | Disabled/opted-out paths do no product state/sender work; enabled and disabled commands preserve exit codes, command output, exception and cancellation behavior, except for the prescribed enabled-only stderr notice. MCP stdout is unchanged. |
| Privacy and presence | Sentinel values in paths, credentials, names, parser errors, positional values, aliases, `--option=value`, option-like values, and `--` never reach events or sender diagnostics; defaults do not appear as supplied flags. |
| Profile state | New/reused/reset/concurrent/unsafe/malformed/future-version states; no ephemeral installation ID and no repeated first-run claim from an unavailable store. |
| Configuration workflow | Successful `init` identity precedes completion; failed write creates none; subsequent `add` and engine resolve the same ID; different configs remain separate; source-file/merged-root cases are explicit. |
| Launch and lifetime | Real web and MCP stdio entry paths use matching intended/actual engine IDs, distinct CLI sessions, correct startup-failure linkage, nonpremature CLI completion, safe concurrent launches, and no fabricated parent on direct starts. |
| Export helper | One top-level command despite helper starts/retries; accurate launch source; no schema-discovery usage claim; collector shutdown cannot terminate another owner. |
| Delivery | Actual SDK-envelope validation with fake HTTP, bounded retries/flush, same-destination ownership, cancellation/disable, no disk event files, and no mutation of host-global settings. |

A fake exporter proves local event construction, not backend receipt. Any separately enabled live test must verify the exact CLI/engine artifacts, chosen destination, correlated event IDs, and stored privacy fields. No live cloud activity or production activation is required merely to implement these documents.
