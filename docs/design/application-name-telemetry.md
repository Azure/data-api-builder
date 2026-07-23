# Application Name Telemetry

> **Status:** Implemented &middot; **Tracking issue:** [#3216](https://github.com/Azure/data-api-builder/issues/3216)

## Summary

Data API builder (DAB) embeds a compact, anonymous **usage-telemetry token** into the `Application Name` property of the connection strings it uses to reach SQL Server, Azure SQL, Azure SQL Data Warehouse (DWSQL), and PostgreSQL. Because `Application Name` is surfaced on the database side (`sys.dm_exec_sessions.program_name` on SQL Server, `pg_stat_activity.application_name` on PostgreSQL), this lets the team understand &mdash; **in aggregate and without any per-customer identifiers** &mdash; which DAB version is running and which features are enabled, using telemetry the database already collects.

The token has the shape:

```text
dab_oss_<version>+<context>|<runtime>|<entity>+
```

Example (an MSSQL pool, REST + GraphQL on, Static Web Apps auth):

```text
dab_oss_2.0.0+XXSX|110000M1M000MMMMMWMM|100?111001110?+
```

It is opt-out (`DAB_TELEMETRY_APPNAME_OPT_OUT=1`), carries no secrets or identifiers, and is purely additive to the existing `Application Name` value.

## Motivation

DAB ships as an open-source container that customers run anywhere. We have very little visibility into how it is configured or which features are exercised. The connection's `Application Name` is a standard, low-cost signal that the database side already records, so encoding a small feature fingerprint there gives us aggregate usage insight with:

- **no new endpoints, services, or network calls,**
- **no per-customer data,** and
- **a single, easy place to query** on the database side.

### Goals

- Encode the DAB **version** and a **feature fingerprint** of a deployment into `Application Name`.
- Make the token **queryable and decodable** so the data can be aggregated and read back.
- Be **safe by construction**: no secrets, no identifiers, easy to opt out of, and additive to any user-supplied `Application Name`.
- Avoid changing **connection-pool behavior** (see [Pooling model](#pooling-model-why-some-fields-are-x)).

### Non-goals

- **Per-request** telemetry (which API was called, which entity, which role). Those facets are not knowable when a pooled connection opens and belong in DAB's request-level telemetry (OpenTelemetry / Application Insights), not the `Application Name`.
- Telemetry for **MySQL** and **Cosmos DB**. MySQL does not get a payload, and Cosmos connection strings are left untouched.

## Background: `Application Name` and connection pooling

`Application Name` is a first-class keyword in both the SQL Server and Npgsql connection-string builders, and it is part of the **connection-pool key**. Two connection strings that differ only in `Application Name` produce two separate pools.

Two consequences shape the design:

1. The token is computed **once per data source at configuration load** and is constant for the lifetime of that data source, so embedding it does **not** create additional pools per request &mdash; every request to a data source reuses the same `Application Name`.
2. Each data source already has its own connection string (its own pool), so the token is naturally emitted **per pool**.

DAB already appended a plain `dab_oss_<version>` user agent to the `Application Name`; this feature replaces that plain value with the richer, decodable token (while preserving any user-supplied `Application Name` as a comma-separated prefix).

## The token format

```text
dab_oss_<version>+<context>|<runtime>|<entity>+
```

- `dab_oss_` &mdash; a fixed marker (`ProductInfo.DAB_USER_AGENT_MARKER`) used to locate the token and to decode it.
- `<version>` &mdash; the product version `Major.Minor.Patch` (from `ProductInfo.DAB_USER_AGENT`). The telemetry is always based on the product version, so it is independent of any host label (see [Hosted label](#hosted-label-dab_app_name_env)).
- The payload is wrapped in `+ ... +` and split into **three `|`-delimited sections**: `context`, `runtime`, and `entity`.

> **Note on the issue's example.** The original issue's example string listed a fourth `general` segment, but the issue only defined three settings tables (Context, Runtime, Entity). `general` was never defined, so the implementation uses the three authoritative sections only.

Each position in a section is a single character drawn from a small alphabet. The shared sentinel values are:

| Char | Meaning |
| --- | --- |
| `0` | feature present and off / false |
| `1` | feature present and on / true |
| `M` | **missing** &mdash; the config section that would answer this is absent |
| `X` | **not applicable** &mdash; not knowable when the pool opens (per-request fields) |
| `?` | **not supported** &mdash; the concept is not yet modeled in DAB |

A few positions use field-specific letters instead (Source and Auth provider), described below.

### Context section (4 characters)

Identifies *what kind of connection* this is. Only `Source` is knowable when a pooled connection opens; the rest are per-request and therefore `X` (see [Pooling model](#pooling-model-why-some-fields-are-x)).

| Pos | Field | Encoding |
| --- | --- | --- |
| 1 | Protocol | always `X` (per-request: REST / GraphQL / MCP) |
| 2 | Object | always `X` (per-request: table / view / stored-proc / document) |
| 3 | Source | the database engine of this data source (see table) |
| 4 | Role | always `X` (per-request: anonymous / authenticated / custom) |

**Source map:** `MSSQL -> S`, `DWSQL -> D`, `PostgreSQL -> P`, `MySQL -> M`, `Cosmos -> C`, and `X` when there is no live data source (for example the CLI, which has no open connection).

### Runtime section (20 characters)

A fingerprint of the **global** `runtime` configuration. Each position is `0` / `1` / `M` unless noted.

| Pos | Setting |
| --- | --- |
| 1 | `runtime.rest.enabled` |
| 2 | `runtime.graphql.enabled` |
| 3 | `runtime.mcp.enabled` |
| 4 | `runtime.host.mode` (`0` = Development, `1` = Production, `M` = missing) |
| 5 | `data-source-files` present (multi-database) |
| 6 | `azure-key-vault` configured |
| 7 | `runtime.health.enabled` |
| 8 | `runtime.cache.enabled` |
| 9 | `runtime.cache.level-2.enabled` |
| 10 | data source uses on-behalf-of (OBO) auth |
| 11 | auto-entities present |
| 12 | `runtime.rest.request-body-strict` |
| 13 | `runtime.graphql.multiple-mutations.create.enabled` |
| 14 | `runtime.telemetry.open-telemetry.enabled` |
| 15 | `runtime.telemetry.application-insights.enabled` |
| 16 | `runtime.telemetry.azure-log-analytics.enabled` |
| 17 | `runtime.telemetry.file.enabled` (file sink) |
| 18 | `runtime.host.authentication.provider` (letter, see below) |
| 19 | embeddings enabled |
| 20 | embeddings endpoint configured |

**Auth provider letters (position 18):** `U` = Unauthenticated/Simulator-disabled, `S` = Simulator, `W` = StaticWebApps, `A` = AppService, `E` = EntraID / AzureAD, `C` = a custom JWT provider, `M` = no authentication section. The single-letter mapping is a DAB-chosen convention (the issue gave the alphabet without a legend); it is trivial to adjust because encode and decode share one table.

### Entity section (14 characters)

An "**is any entity using X?**" fingerprint computed across the (merged) entity set. Each position is `0` / `1` / `M`; positions 4 and 14 may also be `?` because they are not yet modeled.

| Pos | "Any entity &hellip;" |
| --- | --- |
| 1 | is a table |
| 2 | is a view |
| 3 | is a stored procedure |
| 4 | is an MCP persisted document (`?` &mdash; not modeled) |
| 5 | has caching enabled |
| 6 | has REST enabled |
| 7 | has GraphQL enabled |
| 8 | exposes MCP DML tools |
| 9 | exposes an MCP custom tool |
| 10 | uses a custom role (not `anonymous` / `authenticated`) |
| 11 | uses an item-level policy |
| 12 | has a description |
| 13 | has relationships |
| 14 | uses parameter embedding (`?` &mdash; not modeled) |

`M` here means "no entities at all," distinguishing an empty deployment from one whose entities simply do not use a feature.

## Design

### Encoder / decoder

A single class, `ApplicationNameTelemetry` (in `Azure.DataApiBuilder.Config.Telemetry`), owns the format:

- `EncodeTelemetryString(config, liveDataSource)` produces the pure `dab_oss_<version>+...+` token. It is **independent of the opt-out switch and of any host label** &mdash; it always emits the full payload, which is why the CLI uses it for inspection.
- `BuildApplicationNameSegment(config, liveDataSource)` produces what is actually embedded: it honors the opt-out switch and prepends any host label.
- `Decode(applicationName)` turns a token back into human-readable lines and is tolerant of truncation, a missing trailing delimiter, an absent payload, and extra (newer) flags.

Encode and decode are driven by **one ordered list of settings per section** (`_contextSettings`, `_runtimeSettings`, `_entitySettings`). Each setting knows how to encode itself and how to describe a decoded character, so the two directions can never drift apart, and adding a flag is a one-line, append-only change.

### Where and when the token is embedded

The token is woven into connection strings at **configuration load time**, never per request.

- **File / standard load.** `RuntimeConfigLoader.TryParseConfig` post-processes the parsed config and, for every MSSQL / DWSQL / PostgreSQL data source, replaces the `Application Name` with the embedded token. A single public dispatcher, `GetConnectionStringWithApplicationName(connectionString, config, dataSource)`, selects the engine-specific builder (`SqlConnectionStringBuilder` vs `NpgsqlConnectionStringBuilder`); engines without telemetry support return the connection string unchanged.
- **Hosted / late-config.** The `POST /configuration` endpoint supplies configuration after startup with environment-variable replacement disabled, which bypasses the file-load post-processing. `RuntimeConfigProvider.Initialize` therefore embeds the token itself for every data source after the config is materialized, so hosted deployments &mdash; exactly where the `dab_hosted` label matters most &mdash; are covered for both the single-connection-string and merged-config endpoint variants.

### Pooling model: why some fields are `X`

The `Application Name` is the pool key. If `Protocol`, `Object`, and `Role` were encoded per request, DAB would need a distinct pool per `(protocol, object, role)` combination (3 &times; 4 &times; 3 = 36 per data source, multiplied again per user under OBO), exploding the pool count and harming performance. We therefore adopt **Model A**: encode only what is fixed when the pool opens (`Source`) and emit `X` for the per-request facets. Those per-request dimensions, when needed, belong in DAB's request-level telemetry, not the connection's `Application Name`.

### Global telemetry per pool (multi-database)

In a multi-database deployment each data source is its own pool, so the token is embedded into each. We deliberately encode the **global** runtime and the **complete (merged) entity set** at every pool, rather than scoping the entity fingerprint to the entities of that specific data source. The decisive reason is that **the token carries no deployment-correlation identifier**, so the consumer cannot stitch per-pool slices back into one deployment. Encoding the global picture at every pool means **any single sampled connection is sufficient** to know the deployment's full feature profile &mdash; robust to sampling and to rarely-opened pools. (The `Source` character still differs per pool, so the engine mix is preserved.)

### Idempotency

Embedding is idempotent: the engine-specific helpers parse the existing `Application Name` and **skip** if it already contains the `dab_oss_` marker. This guarantees a value can never accumulate a duplicated payload (`...+...+,dab_oss_...+...+`) even if the embed path runs more than once (for example loader post-processing followed by the late-config provider). A user-supplied `Application Name` with no marker is preserved and the token is appended after a comma.

### Opt-out (`DAB_TELEMETRY_APPNAME_OPT_OUT`)

Setting `DAB_TELEMETRY_APPNAME_OPT_OUT=1` reduces the embedded value to **version only** (`dab_oss_<version>`, no payload). Any other value (or unset) leaves telemetry on. The marker is preserved even when opted out so the version remains decodable.

### Hosted label (`DAB_APP_NAME_ENV`)

When `DAB_APP_NAME_ENV` is set (DAB's hosted offering sets it to `dab_hosted`), its value is preserved as a **comma prefix**: `dab_hosted,dab_oss_<version>+...+`. Telemetry is always computed from the product version, so the host label **never suppresses** the token and the `dab_oss_` marker stays intact for decoding.

### CLI: `dab appname`

A new offline command supports inspection without a database:

- `dab appname --config <file>` parses the config and prints the token. Context is emitted as placeholders (no live connection), so the `Source` is `X`. This command performs **no validation and opens no connection** &mdash; it is a static inspection tool, and it intentionally always shows the full encoding regardless of the opt-out switch.
- `dab appname --decode "<token>"` prints a human-readable legend, tolerant of truncation.
- `-o, --output <file>` writes the result to a file instead of stdout.

### Diagnostic logging

When the token is computed, DAB emits a single Debug log of the **token only** (never the full connection string, which can contain secrets). Because the log level may not be known when connection strings are first computed, the entry is buffered in a shared `LogBuffer` and flushed once the logger is available (at startup, on hot reload, and on the hosted late-config path). The buffer is **bounded** (drop-oldest beyond a cap) so it cannot grow without limit if it is ever left undrained.

## Privacy and security

- The token contains **no connection-string contents, secrets, server names, database names, or customer identifiers** &mdash; only the DAB version and boolean/categorical feature flags.
- It is **opt-out** via `DAB_TELEMETRY_APPNAME_OPT_OUT=1`.
- The diagnostic log emits the **token**, never the connection string.

## Scope

| Engine | Telemetry token | Notes |
| --- | --- | --- |
| SQL Server / Azure SQL (`MSSQL`) | Yes (`Source = S`) | |
| SQL Data Warehouse (`DWSQL`) | Yes (`Source = D`) | shares the SQL Server builder |
| PostgreSQL | Yes (`Source = P`) | |
| MySQL | No | connection string left unchanged |
| Cosmos DB | No | connection string left unchanged |

## Decoding and observing in production

The token surfaces server-side as the connection's application name; the exact surface is engine-specific.

- **SQL Server / DWSQL** — `sys.dm_exec_sessions.program_name`:

```sql
SELECT program_name FROM sys.dm_exec_sessions
WHERE program_name LIKE 'dab_oss%' OR program_name LIKE '%,dab_oss%';
```

- **PostgreSQL** — `pg_stat_activity.application_name` (PostgreSQL truncates this to 63 bytes; the decoder tolerates truncation):

```sql
SELECT application_name FROM pg_stat_activity
WHERE application_name LIKE 'dab_oss%' OR application_name LIKE '%,dab_oss%';
```

A captured token can be decoded back to a legend with `dab appname --decode "<token>"`.

## Testing

- **Encoder / decoder unit tests** for token shape, each section's flag mapping, the Source and auth-provider maps, opt-out, the host-label prefix, and round-trip / truncation-tolerant decoding.
- **Connection-string injection tests** for MSSQL, DWSQL, and PostgreSQL (including the user-supplied `Application Name` prefix case), and the no-op cases for MySQL / Cosmos.
- **Multi-database tests** asserting child data sources encode the global runtime and merged entities, and a heterogeneous (MSSQL + PostgreSQL) case asserting the per-pool `Source` character.
- **Hosted / late-config tests** asserting telemetry is embedded through `RuntimeConfigProvider.Initialize` for the single-source and multi-database cases, plus end-to-end `/configuration` and `/configuration/v2` endpoint tests.
- **Idempotency test** asserting a re-embed is a no-op (exactly one marker).
- **`LogBuffer` tests** asserting the bounded drop-oldest behavior and flush-and-drain.
- **CLI tests** for encode (to file and stdout), decode, the config-not-found error path, and opt-out independence.

## Extensibility

- **Adding a flag.** Append a `Setting` to the relevant section list; encode and decode update together. Sentinels (`M`, `?`, `X`) keep older decoders forward-compatible.
- **Adding an engine.** Implement an engine-specific `Get...ConnectionStringWithApplicationName`, add it to the dispatcher's switch, and map the engine to a `Source` character. The injection sites (file load, hosted, multi-database) are already engine-agnostic.


## References

- Tracking issue: [#3216](https://github.com/Azure/data-api-builder/issues/3216)
- Key types: `ApplicationNameTelemetry` (`src/Config/Telemetry/`), `RuntimeConfigLoader` / `FileSystemRuntimeConfigLoader` (`src/Config/`), `RuntimeConfigProvider` (`src/Core/Configurations/`), `LogBuffer` (`src/Config/`), `AppNameOptions` (`src/Cli/Commands/`).
