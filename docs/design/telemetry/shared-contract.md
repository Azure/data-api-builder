# Shared telemetry contract

This contract applies to the [CLI](cli/functional-spec.md) and [Engine](engine/functional-spec.md). Each owns its events and collection lifecycle; shared identities do not make their sessions or counting units interchangeable.

## Enablement and notice

The current implementation remains **off by default in every build**. Deliberate validation uses synthetic activity and an explicitly selected test destination. A destination alone must never enable collection.

The target distribution policy, after privacy/security approval, is:

| Execution context | Policy |
| --- | --- |
| Official CLI, standalone engine, and container distributions | Default-on only after the approved policy is deliberately implemented. |
| Source/debug builds and automated tests | Default-off; explicit synthetic validation only. |
| Embedded engine | Off until deliberately enabled by the host, which owns disclosure. |
| Managed hosting | An explicitly reviewed hosting policy. |

Development host mode alone does not identify a source/debug build.

- `DAB_TELEMETRY_OPT_OUT=1` or `true`, trimmed and case-insensitive, overrides all product enablement. Evaluate the veto before product identity I/O, counters, notice state, or sender initialization.
- Disabled collection sends no events or product notice and leaves saved identities untouched. Normal command/configuration work continues unchanged.
- The veto also removes the entire DAB-added database Application Name segment, including its version marker, while preserving customer content. The legacy `DAB_TELEMETRY_APPNAME_OPT_OUT` used alone retains its existing behavior. Do not modify connection strings per request.
- Customer-configured logs, traces, metrics, and destinations remain independent.
- Show a noninteractive notice before the first enabled transmission, identifying the purpose, opt-out, and public field documentation. Use stderr or an appropriate host diagnostic channel, never MCP stdout. If notice persistence is unavailable, repetition on later runs is acceptable.
- A supported in-process disable stops recording and discards pending records without a final send. Environment changes apply to newly initialized collection lifetimes. Neither disabling nor resetting identity deletes previously received events.

## Identity and event envelope

| Field or concept | Meaning and applicability |
| --- | --- |
| `dab_event_id` | Random GUID per immutable event; retries retain it. |
| `dab_process_session_id` | Random ID per CLI invocation or engine run. They remain distinct even when both execute in one OS process. |
| `dab_installation_id` | Persistent random ID per local OS user profile, not a person, machine, binary, or configuration. Omit it and mark it unavailable when safe persistence is impossible; do not invent an ephemeral installation identity. |
| `dab_api_id` | Random identity intended for one logical API deployment using its root/merged configuration, not each process, entity, protocol, or data source. |
| Identity stability | Separate installation/API states distinguishing newly saved, reused, ephemeral, and unavailable where applicable. Ephemeral applies to API identity, not installation identity. |
| `dab_parent_cli_session_id` | The actual launching CLI invocation on explicitly linked engine events. Omit it for a direct engine run. |
| `dab_launched_engine_session_id` | The intended engine-run ID recorded by the CLI launch event; it must match the engine session that actually starts. |
| `dab_config_epoch` | Accepted-configuration sequence within an engine run; not a hash or cross-run identity. It is not a CLI invocation counter. |

Also include schema version, DAB version, UTC occurrence time, within-session sequence, and explicit synthetic/test classification. Measure durations with a monotonic clock. Occurrence time is not upload/receipt time. Include only applicable fields; direct engines do not manufacture CLI installation or parent-session identity.

Saved IDs are automatically reused across ordinary runs, upgrades, and configuration changes. Independent fresh state generates independent random IDs even for identical configurations. Identity persistence is best effort: API storage failure uses a flagged per-run ID without failing DAB, and does not invalidate an independently available installation ID. Do not infer replica grouping or cross-run continuity from missing/ephemeral state.

Concurrent creators must safely converge on valid saved state without overwriting someone else's file. Do not repair malformed, unreadable, or unsupported state by deleting it. No required customer IDs, mounts, permission changes, configuration edits, or coordination service are introduced.

There is no scheduled rotation in the initial contract. Local reset is documented removal of the appropriate saved identity while affected processes are stopped. The next enabled run creates an unrelated ID, without reconstructing or linking the old one. Opt-out is not reset; later permitted enablement reuses valid saved state. Copying or roaming identity state must not be described as proof of a distinct installation or deployment.

## CLI-to-engine launch boundary

This is a required integration contract, **not an implemented bridge in the current Engine baseline**.

1. A successful enabled CLI configuration-creation command allocates or reuses that configuration's API identity before recording command completion. Failed creation allocates no API ID.
2. Later configuration-aware commands reuse available identity for their unambiguous resolved root configuration. Sharing an installation profile does not identify which configuration was used.
3. At an actual engine handoff, record a CLI launch event with the available installation/API identities, parent CLI session, newly selected intended engine session, and a closed launch-source category. Do not emit launch intent for validation that prevented the handoff.
4. Pass that context explicitly to the engine bootstrap. The engine adopts the intended engine-session ID before its first event and retains it on startup failure as well as success. CLI and engine sequences remain separate.
5. Keep configuration/API identity consistent across the bridge. A missing or mismatched configuration identity cannot be recovered by hashing paths or contents. Ephemeral identity establishes no relationship between independent invocations; any same-run linkage must come from the explicit handoff.
6. Direct engine starts continue to work with their own session and API identity and no fabricated CLI linkage. Concurrent launches must not borrow another invocation's context.

The launch event proves intent to hand off, not readiness or successful data use. Link `init` -> `add` -> engine start -> first served request only through actual same-configuration identity and the explicit bridge. First successful use remains distinct from first served use. Missing linkage remains unavailable.

## Privacy

Collect allowlisted categories, states, option-presence flags, bucketed scale, and aggregate measurements only. Registered CLI command names are allowed; raw command lines are not.

Never emit credentials, connection strings, URLs, filesystem paths, customer/host/database/entity/role names, observed IPs, claims, raw argument values or invalid tokens, SQL/query text, request/response contents, prompts/vectors, exception text, hardware identifiers, request/trace IDs, or configuration hashes. Apply these restrictions to automatic SDK fields and sender diagnostics as well as explicit event properties.

Persistent IDs must be random rather than derived from customer, machine, or configuration data. Random identifiers remain potentially pseudonymous, not anonymous. Do not run extra database queries or network discovery solely for telemetry. A synthetic marker classifies the workload; it neither anonymizes its contents nor authenticates its sender.

## Delivery and failure behavior

- Send product records to an explicitly selected DAB-owned Application Insights destination, never to customer-configured observability or by forwarding customer diagnostics.
- Use a supported SDK and a product-owned delivery boundary. Do not implement a second ingestion protocol or reuse application logging as the telemetry event source.
- Keep queues, aggregation, retries, and payload sizes bounded. Events are memory-only: no disk spool or restart replay. Small identity/notice state is a separate concern.
- CLI execution, engine startup, and request processing must neither fail nor wait for telemetry network delivery. A short bounded graceful flush may discard leftovers. Optional telemetry failures must not replace application exceptions or exit codes.
- Retries reuse immutable events. New summaries do not repeat earlier counts. Counters saturate rather than wrap; expose measurable local loss/capping without claiming complete delivery accounting.
- Disable cancels owned pending delivery without a final flush. Already transmitted data cannot be retracted. Outages, crashes, overflow, retries, and ambiguous acknowledgements can lose or duplicate events.
- CLI and engine collection in one process must have explicit sender ownership and shutdown semantics. Finishing one session must not terminate another session's sender or reset process-global SDK settings.

The current Engine adapter's numeric limits and temporary SDK prerequisites are described in the [Engine handoff](engine/implementation-handoff.md). Reuse must preserve those guarantees, not assume a second exporter instance provides isolation.
