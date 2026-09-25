# Product telemetry: Engine and CLI

The Engine and CLI have separate functional specifications and implementation handoffs. The shared contract defines their common privacy, identity, delivery, and launch-linkage requirements.

| Workstream | Functional requirements | Implementation handoff |
| --- | --- | --- |
| CLI | [CLI functional specification](cli/functional-spec.md) | [CLI implementation handoff](cli/implementation-handoff.md) |
| Engine | [Engine functional specification](engine/functional-spec.md) | [Engine implementation handoff](engine/implementation-handoff.md) |
| Both | [Shared contract](shared-contract.md) | Required for either workstream, particularly the CLI-to-engine launch bridge. |

## Implementation baseline

- These documents are maintained on `dev/aaronburtle/engine-telemetry`. Updating this documentation branch does not merge implementation code into it.
- The Engine implementation baseline is [e85f3338](https://github.com/Azure/data-api-builder/tree/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a), available on `dev/aaronburtle/engine-telemetry-phase1`. Source links in the handoffs are pinned to that revision.
- CLI command events, CLI installation identity, and the explicit CLI-to-engine session bridge are **not implemented in that baseline**. CLI integration needs that Engine baseline or a descendant containing the same components, rather than the older code on this documentation branch alone.
- The functional specifications describe required behavior. The handoffs distinguish existing behavior from work still to implement; a documented requirement is not evidence that it already runs.

## Collection boundary

Product telemetry is separate from customer-configured observability. The current implementation is default-off and supports explicit synthetic validation only. Publication of these documents does not enable production collection. New CLI collection must preserve that boundary until the approved distribution policy is implemented.

The [Engine validation guide at the baseline](https://github.com/Azure/data-api-builder/blob/e85f3338d5b6ff543b0cd6c5686c53eeb0899e8a/docs/telemetry.md) describes the current destination settings, fields, SDK restrictions, and validation procedures. The specifications here do not require another backend, reporting system, or telemetry migration.
