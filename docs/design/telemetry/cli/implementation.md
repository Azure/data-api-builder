# CLI telemetry implementation

## Boundaries

This implementation adds CLI collection to the existing engine telemetry foundation, preserving the supported Azure Monitor exporter dependency, destination gate, privacy guard, API identity format and finite delivery behavior. Production collection remains off. The public [field and validation guide](../../../telemetry.md) defines the emitted contract.

| Boundary | Owner |
| --- | --- |
| Enablement | `ProductTelemetryBootstrap` checks umbrella veto, synthetic opt-in, both SDK-statistics opt-outs and explicit valid product routing before identity/notice/sender work. |
| Invocation | CLI `Main` owns one `CliTelemetrySession`; the internal `Execute` wrapper completes it once around the real parse and handler. Public `Execute` does not implicitly create a CLI session. |
| Grammar | `CliTelemetryCommand` uses an explicit reviewed command/option allowlist and the pinned parser's token rules. The actual parse determines control requests and success/failure. No raw argument survives inspection. |
| Installation | `CliTelemetryInstallationStore` safely persists a per-user-profile random UUID. Only the atomic publication winner records first-run initialization. Unavailable storage omits identity, not useful command telemetry. |
| Configuration | Shared `EngineTelemetryIdentityStore.Lookup` cannot create state. Successful `init` calls `Resolve` only after writing. Later commands observe the exact root already selected by their normal work; no extra configuration discovery. |
| Handoff | `ProductTelemetryLaunchContext` carries separate parent/engine session IDs, available installation/API identities, and a closed launch source. It contains no path, arguments, callbacks or mutable global state. |
| Delivery | CLI and engine use the same generic bounded delivery algorithm and lease a serialized, process-owned SDK sender by normalized destination. No second ingestion protocol or customer logger is used. |

## Ordering and lifecycle

1. Gate collection before profile state, notice and sender work. Show the notice on stderr before any send.
2. Initialize/reuse installation state. Emit `dab.cli.first_run` only for newly saved state.
3. Parse once and dispatch with an explicit invocation-owned session. Capture only approved option presence, never resolved defaults or values.
4. After successful configuration creation, allocate/reuse its API sidecar before `dab.cli.command`. Failed writes allocate none. Read-only/other commands perform lookup only.
5. At actual engine handoff, emit `dab.cli.engine_launch` and pass the selected engine ID before `dab.engine.process_started`. Same-run ephemeral API identity is preserved without claiming cross-invocation continuity.
6. Record command completion when the handler returns or an exception/cancellation is observed. Preserve return codes, output and thrown exceptions. A typed engine-startup-failure observation distinguishes the web host's legacy normal return after `StopApplication` from actual successful initialization. Export carries a typed terminal result separately from recoverable attempt failures: no schema is a telemetry failure even when the legacy exit code is zero, while a successful retry is still success.
7. Gracefully drain only the owning queue, bounded to two seconds. Explicit disable discards without a final send.

Asynchronous export helpers own one-shot launch reservations. Reserving creates no identity, event, worker or sender. Actual preflight-approved handoff may occur after ordinary command completion/disposal, using its original parent session and a later sequence. It owns a separate capacity-one queue; neither export nor the engine waits for telemetry. Explicit disable revokes all such reservations. Cancellation before the helper runs, or failed preflight, never emits intent. No nested `start` command event is manufactured. Existing export serving/retry/cancellation semantics are unchanged.

The accepted configuration uses the actual CLI-selected root, including a generated merged root. There is no hash, child-source identity, or automatic join between independent root sidecars. Ambiguous command targets omit API linkage. The engine rechecks root consistency when accepting configuration; direct starts remain unlinked.

## Requirements mapping

| Requirement | Implementation / evidence |
| --- | --- |
| First run and repeat use | Strict versioned profile state, safe concurrent publication, random identity reuse/reset and unavailable-state tests. |
| One command event | Single real-parser wrapper; completion idempotence, help/version, unknown input, typed failures, cancellation and original-output parity tests. |
| No argument values | Fixed grammar registry; sentinel tests for aliases, assignments, option-looking values, malformed input and positional parameters. |
| Installation/API separation | Profile state and compatible API sidecars are separate. Successful creation, lookup-only later commands and multiple/merged-root cases are tested. |
| Sequential launch/usage linkage | Explicit handoff before the engine's first event. Real CLI web/stdio workflows exercise metadata, serving, first success, failure and shutdown; launch intent is not readiness. |
| Export lifetime | Real helper/introspection and delayed-preflight tests verify one command, correct source, no premature `start` completion, and independent sender ownership. |
| Privacy and routing | Common product gate, SDK fake-HTTP envelopes, constant source-IP suppression, ambient enrichment rejection, no customer destination fallback or process-global SDK mutation. |
| Bounded reliability | Main queue 256, three attempts, one-second SDK attempt, two-second drain; at most 16 launch reservations, eight retained destinations, no event spool. |
| Current main compatibility | Retains ordered MCP metadata/authorization/registry publication, reload serialization, notifications and shutdown. Swallowed registry failures do not claim accepted telemetry epochs. |

The phase-1 refinements are deliberate: synthetic validation rather than production activation, per-profile rather than hardware identity, and creation-time API identity after successful `init`. Public documentation and tests describe those semantics without treating random IDs as anonymous or copied state as evidence of distinct installations.

## Validation limits

- `CliTelemetry` covers parser/handler behavior and shared offline lifecycle, persistence, delivery and SDK serialization. Platform capability skips are not evidence of that platform's behavior.
- `CliTelemetryLocalDb` exercises real `Program.Execute` through service bootstrap with owned LocalDB data, real REST/MCP tools and GraphQL introspection. It substitutes only the sender, installation identity and local transports/lifetime controls.
- Existing `EngineTelemetry`, `EngineTelemetryLocalDb` and current-main MCP hot-reload tests remain separate regression suites.
- Local fake-HTTP tests do not prove Application Insights receipt or stored enrichment. Any live validation must use the selected private test destination, exact built artifacts and synthetic activity. No production collection or live cloud test is implied by this implementation.
- SDK-cached cloud-role/component overrides deliberately prevent delivery rather than leak values. Foreign same-destination exporters and arbitrary embedded coexistence remain unsupported in this validation phase.