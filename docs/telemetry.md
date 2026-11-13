# Engine product telemetry

Engine product telemetry is **off by default in every build**. The current feature
is available only for explicitly enabled synthetic testing, not production collection.
Customer-configured logs, traces, metrics and telemetry destinations are separate.

## What is collected when enabled

- Categorical configuration and bucketed counts/limits, not configuration contents.
- DAB version, coarse OS/runtime information and categorical hosting information.
- Startup/readiness/failure milestones and aggregate usage, outcomes and latency.
- Random event, process-session and API identifiers used to correlate events.

The [complete field and event reference](testing-guide/engine-telemetry.md#collected-fields)
lists the 82 configuration snapshot keys, context properties and usage dimensions.

## Privacy

Product events do not include request/response contents, queries or SQL, URLs,
connection strings, customer/host/database/entity/role names, claims, tokens,
observed IP addresses, exception text, hardware identifiers or request/trace IDs.
Identifiers are pseudonymous, not anonymous. Event delivery is memory-only, with
no event spool or replay after restart; delivery is best-effort.

## Opt out

Set `DAB_TELEMETRY_OPT_OUT=1` or `DAB_TELEMETRY_OPT_OUT=true` before starting DAB.
Values are trimmed and `true` is case-insensitive. Opt-out overrides test enablement
and also suppresses DAB-added database Application Name and Cosmos user-agent segments.
Customer-configured telemetry remains separate. Restart after changing the environment.

A disabled run does not initialize product identities, counters, timers, notices
or sender resources. Opt-out leaves saved identities unchanged and does not delete
previously received events.

## Enable isolated synthetic testing

Use only made-up data in an isolated test process, never real customer traffic.
Test mode does not create fixtures, sanitize requests or anonymize data.
All current product events are marked `dab_is_synthetic=true`.

Set the following in that process's environment **before it starts**:

| Variable | Required value |
| --- | --- |
| `DAB_PRODUCT_TELEMETRY_TEST_MODE` | `1` or `true` |
| `DAB_PRODUCT_TELEMETRY_CONNECTION_STRING` | The selected test Application Insights connection string |
| `APPLICATIONINSIGHTS_STATSBEAT_DISABLED` | Exactly `true` (case-insensitive, no surrounding whitespace) |
| `APPLICATIONINSIGHTS_SDKSTATS_DISABLED` | Exactly `true` (case-insensitive, no surrounding whitespace) |

The opt-out must not be enabled. A destination alone never enables collection.
The destination is selected only through `DAB_PRODUCT_TELEMETRY_CONNECTION_STRING`;
there is no built-in endpoint or fallback to customer telemetry configuration.
Do not commit connection strings. These settings are for synthetic validation only,
not a production rollout. See the [validation guide](testing-guide/engine-telemetry.md#local-validation)
for offline checks and testhost settings; live-batch validation is deferred.

## Protect and reset the API identity

An enabled run can store a random API UUID beside an existing root configuration
in a `<config>.dab-telemetry.json` sidecar. It contains only a format version and ID.
If safe persistence is unavailable, a flagged per-run ephemeral ID is used.

**Exclude `*.dab-telemetry.json` from Git, build artifacts and container images.**
Do not copy it across independent deployments: the same API identity would be reused.
This repository's [.gitignore](../.gitignore) covers Git only; DAB does not automatically
modify customer repositories or artifact/image exclusions.

To reset, stop every process using that configuration and delete only its telemetry
sidecar that you own. The next enabled run generates a new, unrelated random UUID,
not a configuration hash. Reset does not delete previously received telemetry.

## Notice

Only an enabled run prints the following notice to stderr before sending, never
to MCP stdout. It currently repeats for each enabled run.

```text
Telemetry
---------
Data API builder collects usage data to help Microsoft improve the product. It does not include your data, queries, names or connection strings. To opt out, set the DAB_TELEMETRY_OPT_OUT environment variable to 1 or true.
Learn more: https://github.com/Azure/data-api-builder/blob/main/docs/telemetry.md
Microsoft Privacy Statement: https://go.microsoft.com/fwlink/?LinkId=521839
```
