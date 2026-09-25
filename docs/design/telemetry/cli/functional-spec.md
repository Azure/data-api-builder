# CLI telemetry functional specification

CLI product telemetry records installation-profile initialization, completed command invocations, and explicit handoff to an engine run. It does not record API traffic or substitute for engine readiness and successful-use events.

The [shared contract](../shared-contract.md) supplies enablement, notice, privacy, common identities, delivery, and launch linkage. This CLI feature is **not implemented in the current Engine baseline**. Development and tests remain default-off with explicit synthetic validation; this specification does not activate production collection.

## Context and allowed inputs

| Information | Required representation |
| --- | --- |
| Product | DAB version/channel, distribution, packaging, and detectable installation channel. |
| Environment | OS family/coarse version, architecture, and normalized runtime version. |
| Command | Registered command/subcommand names from a closed vocabulary; unrecognized input is `unknown`. |
| Options | Presence of allowlisted registered options, not values and not presence inferred from resolved defaults. |
| Result | Closed outcome and sanitized failure category; elapsed duration from a monotonic clock. |

Unknown installation/packaging evidence remains unknown. Do not inspect unrelated package managers, enumerate process command lines, or perform network discovery to fill those fields.

Never emit raw arguments, invalid tokens, paths, filenames, entity/source/role names, connection strings, URLs, credentials, exception text, or command output. Option aliases normalize to the same approved option identity. Positional values are not telemetry. The name of a registered option may be recorded as presence; its potentially sensitive value may not.

## Events

| Event | Boundary | Event-specific information |
| --- | --- | --- |
| `dab.cli.first_run` | First enabled installation-profile initialization. | Available installation identity/stability, installation channel, and CLI context. |
| `dab.cli.command` | Once per completed top-level invocation. | Command/subcommand, option presence, outcome, duration, sanitized failure category, and API ID only for an unambiguous applicable configuration. |
| `dab.cli.engine_launch` | An actual explicit handoff to start an engine run. | Available installation/API IDs, parent CLI session, intended engine-session ID, and closed launch source. |

Each invocation has one CLI session, independent of any engine sessions it starts. Helper calls or retries inside a command are not additional top-level invocations. A completed invocation produces at most one command event even if several paths observe its completion. Abrupt process termination can leave no completion event; best-effort delivery can lose any event.

Distinguish success, parse/validation failure, execution failure, cancellation, and unknown. Classify parser/control cases according to actual behavior: successfully handled help/version output is not a failure merely because the parser represents it on an error path. Preserve existing exit codes, stdout/stderr behavior, exception propagation, and cancellation semantics.

Measure command duration until that invocation actually finishes. In the current in-process `start` path, this can include the engine's entire lifetime; it is **not** startup duration. The launch event is emitted at the handoff, without waiting for engine exit. It proves intent, not actual engine initialization, readiness, or successful data activity.

## Installation-profile identity and first run

- Use one persistent random installation ID per local OS user profile. It is neither a person nor a machine identifier, and is not scoped to a configuration or CLI executable version.
- Reuse valid state across ordinary invocations and upgrades. Independent fresh profiles get independent IDs; copying profile state does not prove two distinct installations.
- If safe persistence is impossible, omit the installation ID and explicitly mark it unavailable. Do not substitute a temporary installation ID or disable useful command telemetry solely because it is unavailable.
- First-run evidence must correspond to observed first enabled profile initialization. Concurrent creators must not independently claim repeated initialization of the same saved profile. Unavailable or corrupt state must not be presented as proof of a new installation on every invocation.
- Identity/notice persistence failures never fail a command. Opt-out performs no product-state I/O and leaves saved state untouched. Reset creates unrelated state only on a later enabled invocation, without old-to-new linkage or replay of lost events.

## Configuration identity

A successful enabled configuration-creation command allocates or reuses the random API ID for the configuration it actually wrote **before** recording command completion. Creating an in-memory configuration candidate is not sufficient; failed creation or write must not allocate API identity.

Later commands reuse available identity for their unambiguous resolved root configuration. Default filenames, environment-selected files, explicit paths, merged configurations, and multiple source files must follow the same root-identity rule as the engine. Names and paths are local resolution inputs only, never event properties.

An invocation unrelated to a configuration, or one with an ambiguous/unresolved target, omits API linkage. A failed command against an already identified configuration is distinct from failed creation of a new configuration. Configurations created outside the CLI obtain identity at their first eligible engine start; inspecting a file is not proof of a deployment or authority to mint a different CLI-only identity.

API persistence failure must not invalidate an available installation ID or fail the command. API identity may be flagged ephemeral for one run, but separate invocations cannot infer continuity without shared saved state. Different configurations must not merge merely because they share the same user profile or identical contents.

## Engine integration

Use the [explicit launch boundary](../shared-contract.md#cli-to-engine-launch-boundary). CLI and engine session IDs remain distinct, including in one OS process. The engine's first event must use the intended launched-engine ID and actual parent CLI session supplied at handoff. Direct engine starts omit CLI linkage.

Carry the same available configuration identity through successful `init`, later `add`, and an actual engine launch. A shared installation ID alone cannot link these stages. Missing identity or a launch that never starts the engine must not appear as a completed workflow. First served and first successful requests come from the engine, never inferred from CLI command success.

The current implementation can start the service from `export` as well as `start`. Such helper execution is not another CLI command invocation. Its launch source, lifetime, and any bridge evidence must describe what actually happened, without claiming user data activity from schema discovery.

## Failure and delivery behavior

Collection is separate from CLI/application logging and customer observability. Apply the umbrella opt-out before identity, notice, counters, or sender work. Keep telemetry notice/diagnostics off MCP stdout. An enabled CLI may make small best-effort state accesses, but command execution must not wait for telemetry network delivery.

Queue only bounded immutable events, retry within bounds, and allow a short graceful flush. No disk event spool, replay after restart, additional database queries, or custom ingestion protocol is permitted. Disable discards pending records. Completing the CLI collector must not dispose an active engine collector's sender, and vice versa.

## Functional acceptance

| Scenario | Expected result |
| --- | --- |
| Opt-out, missing synthetic opt-in, or invalid destination | No product state, notice, exporter, or transmission; command behavior unchanged. |
| New profile, repeat invocation, upgrade, reset, and concurrent creators | Correct identity reuse/new initialization with no fabricated identity or repeated first-run claim. |
| Missing permissions, malformed/future state, or unavailable profile | Installation identity unavailable; command remains usable; no overwrite/repair of owner state. |
| Recognized/unknown commands; aliases; explicit/defaulted options; sensitive or malformed values | Only approved command categories and true option-presence flags; no raw values in records or sender diagnostics. |
| Success, validation rejection, execution failure, cancellation, help/version, or exception | One observed completion at most; truthful outcome and monotonic duration; original return/output/throw behavior preserved. |
| Successful `init`, failed `init`, and failed configuration write | API identity allocated only after successful creation; available linkage present before its completion event. |
| Multiple configs, merged roots, source files, and commands without a target | Correct same-root identity; unrelated configurations never linked through installation identity alone. |
| `start` web/stdio, startup failure, direct engine start, and concurrent launches | Explicit and isolated parent/child IDs; launch intent is not readiness; CLI completion is not premature. |
| Export helper launch and repeated command execution in one process | No nested command double count, identity crossover, accidental activity claim, or sender ownership leak. |
| Offline/slow SDK, oversized records, overflow, disable, and forced termination | Bounded memory/retries/exit delay, no event disk backlog, possible loss acknowledged, application unaffected. |
