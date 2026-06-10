# Implementation Plan: MSSQL JSON Data Type Support

**Branch**: `Usr/sogh/speckit-jsontypesupport` | **Date**: 2026-06-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [specs/001-mssql-json-type/spec.md](./spec.md)

## Summary

Add first-class support for the SQL Server 2025+ / Azure SQL DB native
`JSON` column type to DAB. The column surfaces as a `String` in REST
(OpenAPI `type: string`), GraphQL (built-in `String` scalar), and MCP
tool schemas with **no JSON-specific annotation**. DAB treats the
value as an opaque string in both directions and delegates JSON
syntax validation, operator support, and version compatibility to SQL
Server. Scope is **MSSQL only**; other engines are unaffected.

Per the 2026-06-09 Clarifications session, a JSON column is treated
exactly like a string column. DAB does **not** maintain a JSON-specific
operator allow-list, does **not** add JSON-specific input validation,
and does **not** annotate MCP tool schemas. When SQL Server returns an
error on a JSON column (malformed JSON write, unsupported operator on
filter / order-by, etc.) DAB surfaces it as HTTP `400 Bad Request`
(REST) or GraphQL `extensions.code = "BAD_REQUEST"` and includes the
SQL Server error number in the response body.

The implementation reduces to two production code edits:

1. Single dictionary entry in `TypeHelper.cs` mapping
   `SqlDbType.Json` to `typeof(string)`.
2. Append the SQL Server JSON-validation error numbers (currently
   13608–13614) to the existing
   `MsSqlDbExceptionParser.BadRequestExceptionCodes` list, so JSON
   errors map to 400 rather than 500.

The feature **depends on a single, joint prerequisite PR** that
delivers two coupled bumps:

1. **Microsoft.Data.SqlClient `5.2.3 → 6.x`** — adds the SqlClient-side
   JSON wire-protocol support (and the `Microsoft.Data.SqlTypes.SqlJson`
   runtime type).
2. **Target framework `net8.0 → net10.0`** across the solution
   (every `src/**/*.csproj`, the SDK pin in [global.json](../../global.json),
   and the `dotnet-version` in CI workflow files) — required because
   `SqlDbType.Json = 35` is a **BCL enum value** in `System.Data`,
   added in .NET 9 and present in .NET 10. .NET 8's `SqlDbType` enum
   stops at `DateTimeOffset = 34`, so `Enum.TryParse<SqlDbType>("json", …)`
   in `TypeHelper.GetSystemTypeFromSqlDbType` returns `false` regardless
   of which SqlClient version is installed.

Both bumps are delivered as a **single separate prerequisite PR** and
are out of scope for this feature's task list. .NET 10 is the current
Long-Term Support release (chosen over .NET 9 STS, which is EOL May
2026). See [research.md](./research.md) R1 for the full analysis.

### Upstream Dependency (separate PR)

**Prerequisites** (delivered jointly by ONE separate PR):

- `Microsoft.Data.SqlClient >= 6.0.0` in
  [src/Directory.Packages.props](../../src/Directory.Packages.props).
- `<TargetFramework>net10.0</TargetFramework>` in every
  `src/**/*.csproj`.
- `sdk.version: "10.0.x"` in [global.json](../../global.json).
- CI workflow `dotnet-version: "10.0.x"` in `.github/workflows/*.yml`.
- License-notice refresh for SqlClient SNI native asset.

Bundling these in one PR lets the multi-engine CI matrix (MsSql,
PostgreSql, MySql, CosmosDb_NoSql) validate the runtime + driver
upgrade together. This feature's `tasks.md` MUST NOT include any of
these bumps or their companion edits (license URL, NOTICE regeneration,
TFM edits, SDK pin edits).

**Blocking**: This feature's implementation tasks cannot be completed
until the joint dependency PR is merged into the same target branch.
A pre-flight task verifies BOTH the installed SqlClient version AND
the target framework, and fails fast with a clear message if either
prerequisite is missing.

## Technical Context

**Language/Version**: C# 12 / **.NET 10** (post-prerequisite-PR; per
[global.json](../../global.json)). The current main branch ships on
.NET 8; the joint prerequisite PR bumps the SDK pin and every
`src/**/*.csproj` `<TargetFramework>` to `net10.0` to surface the
BCL `SqlDbType.Json` enum value (added in .NET 9, current LTS in
.NET 10).

**Primary Dependencies**: Hot Chocolate (GraphQL),
Microsoft.OData.UriParser (REST filter), Microsoft.Data.SqlClient
**>= 6.0.0** (prerequisite — required for SqlClient-side JSON
support; delivered by the joint .NET 10 + SqlClient 6.x dependency
PR, see Summary and research R1).

**Storage**: SQL Server 2025+ / Azure SQL DB (target). No other engines
in scope.

**Testing**: MSTest under `src/Service.Tests` with `TestCategory=MsSql`
(Constitution Principle II). Test fixtures:
[src/Service.Tests/DatabaseSchema-MsSql.sql](../../src/Service.Tests/DatabaseSchema-MsSql.sql)
and [src/Service.Tests/dab-config.MsSql.json](../../src/Service.Tests/dab-config.MsSql.json).

**Target Platform**: Cross-platform container/host. No platform-specific
behavior introduced.

**Project Type**: Single backend service + tests (existing DAB repo
layout — no new project added).

**Performance Goals**: No new performance budget. JSON column reads and
writes inherit the existing `nvarchar(max)` performance envelope; SQL
Server-side JSON validation cost is borne by SQL Server.

**Constraints**:

- `dotnet format src/Azure.DataApiBuilder.sln --verify-no-changes` MUST
  pass (Principle VI).
- No changes to `schemas/dab.draft.schema.json` (FR-014, R11).
- No changes to non-MSSQL engine code, schemas, or test fixtures
  (FR-012, Principle I).
- No new GraphQL scalar (FR-013).
- No SQL Server version probe (FR-016).
- No secrets committed; connection strings via `@env('VAR_NAME')`
  (Principle V).

**Scale/Scope**: Production code delta < 50 lines across 6 files (see
research.md summary table). Test code delta will dominate the diff and
is expected.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-evaluated after Phase 1 design.*

| Principle | Status | Evidence / Justification |
|-----------|--------|--------------------------|
| **I — Multi-Engine Parity** | ✅ PASS | Spec FR-012 declares MSSQL-only scope with explicit reasoning (only SQL Server has a native `JSON` column type today). All other engines explicitly out of scope; their fixtures and code are untouched. Integration tests added under `TestCategory=MsSql` only — correct per principle. |
| **II — Integration-Test-First** | ✅ PASS | Every functional requirement (FR-001 … FR-017) has a corresponding `TestCategory=MsSql` integration test obligation enumerated in [research.md](./research.md) and operationalized in the upcoming `tasks.md`. Unit tests (e.g., for the operator gate) are **additive**, not substitutive. |
| **III — REST + GraphQL Parity** | ✅ PASS | Spec User Stories 1–9 require behavior in **both** REST and GraphQL; contracts files [rest-openapi.md](./contracts/rest-openapi.md) and [graphql.md](./contracts/graphql.md) pin matching shapes. The MCP surface (also required) is pinned in [mcp-tools.md](./contracts/mcp-tools.md). |
| **IV — Config Schema Discipline** | ✅ PASS | No changes to `schemas/dab.draft.schema.json` (R11). Existing fixtures (`dab-config.MsSql.json`) gain a new entity entry only, validated by `dab validate`. |
| **V — No Secrets in Source** | ✅ PASS | Quickstart and tests use `@env('MSSQL_CONNECTION_STRING')`-style references; no committed secrets. |
| **VI — Formatting & Style** | ✅ PASS | All edits target existing patterns. New SQL in tests will be formatted per copilot-instructions.md (poorsql for MSSQL with trailing commas, 4-space indent). `dotnet format` will be run pre-commit. |
| **VII — Minimal-Surface Changes** | ✅ PASS | Mirrors the existing `nvarchar(max)` / `typeof(string)` flow end-to-end. Production code delta is ~5 line-level edits across 6 files (research summary). No new abstractions, no parallel pipelines, no refactor of `CreateRecordTool` / `UpdateRecordTool` shapes (see R5). |

**Initial gate**: PASS — no violations.

**Post-design re-check (after Phase 1)**: PASS — contract documents
and data model do not introduce any new abstractions or cross-engine
touchpoints. The Microsoft.Data.SqlClient `6.x` upgrade (R1) is
carried by a **separate prerequisite PR** and is therefore not a
decision tracked by this feature's task list; it is recorded here
purely as a dependency note. This feature's diff stays at the
~5-line code edits + tests envelope described in Principle VII.

## Project Structure

### Documentation (this feature)

```text
specs/001-mssql-json-type/
├── plan.md                         # This file
├── spec.md                         # Feature specification
├── research.md                     # Phase 0 — touchpoint analysis, deps
├── data-model.md                   # Phase 1 — test-fixture schema
├── quickstart.md                   # Phase 1 — manual validation guide
├── contracts/
│   ├── rest-openapi.md             # REST/OpenAPI shapes & error map
│   ├── graphql.md                  # GraphQL SDL, queries, mutations
│   └── mcp-tools.md                # describe_entities / dynamic_custom_tool
├── checklists/
│   └── requirements.md             # Spec-quality checklist (resolved)
└── tasks.md                        # Phase 2 — produced by /speckit.tasks
```

### Source Code (existing DAB repo, files this feature touches)

Single project; no new project introduced. Concrete touchpoints
(verified in [research.md](./research.md)):

```text
src/
├── Core/
│   ├── Services/
│   │   └── TypeHelper.cs                          # R1: SqlDbType.Json -> typeof(string) (single dictionary entry)
│   └── Resolvers/
│       └── MsSqlDbExceptionParser.cs              # R4: append JSON validation error numbers (13608-13614) to BadRequestExceptionCodes
└── Service.Tests/
    ├── DatabaseSchema-MsSql.sql                   # R6: profiles table + seed rows
    ├── dab-config.MsSql.json                      # R6: Profile entity entry
    └── ...                                        # New MSSQL integration tests (see tasks.md)
```

**Superseded by 2026-06-09 Clarifications** (no longer touched):

- `src/Core/Parsers/ODataASTVisitor.cs` — R3 operator allow-list gate
  was dropped. DAB forwards every filter operator to SQL; SQL Server
  is the authority on supported operators.
- `src/Azure.DataApiBuilder.Mcp/BuiltInTools/DescribeEntitiesTool.cs`
  and `src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs` — R5
  MCP description hint was dropped. A JSON column appears in MCP tool
  schemas as a plain string with no annotation.

**Delivered by the joint prerequisite dependency PR (NOT in this feature's tasks)**:

- `src/Directory.Packages.props` — Microsoft.Data.SqlClient `5.2.3 → 6.x`
- Every `src/**/*.csproj` — `<TargetFramework>net8.0</TargetFramework> → net10.0`
- [global.json](../../global.json) — SDK pin `8.0.420 → 10.0.x`
- `.github/workflows/*.yml` — `dotnet-version: '8.0.x' → '10.0.x'`
- `external_licenses/` — refreshed SqlClient SNI license file
- `scripts/notice-generation.ps1` — license URL refresh and NOTICE regeneration

**Files explicitly NOT touched** (constitutional guard rails):

- `src/Service.Tests/DatabaseSchema-PostgreSql.sql`,
  `DatabaseSchema-MySql.sql`, `DatabaseSchema-DwSql.sql`, CosmosDB
  schema/config files — Principle I, FR-012.
- `schemas/dab.draft.schema.json` — FR-014, R11.
- `src/Cli/**` — FR-014 (no CLI flag changes).
- `src/Service.GraphQLBuilder/**` — no new scalar; existing `string`
  mapping suffices (R2).
- Any other engine's metadata provider, query builder, or exception
  parser — Principle I.

**Structure Decision**: Modify existing files in place along the
identified touchpoints. No new project, no new top-level directory.
The shape mirrors how prior column-type additions have flowed (R1, R2,
Principle VII).

## Phase 0 — Outline & Research (complete)

See [research.md](./research.md). Eleven research items resolved.
The 2026-06-09 Clarifications session **supersedes R3 and R5** (the
operator gate and MCP annotation are no longer implemented) and
**simplifies R4** to a one-line list extension.

- **R1** — Joint prerequisite (delivered by a separate PR):
  Microsoft.Data.SqlClient `6.x` for SqlClient-side JSON support **AND**
  target framework `net10.0` to surface `SqlDbType.Json = 35` (a BCL
  enum value added in .NET 9, current LTS in .NET 10). This feature
  contributes the single dictionary entry
  `[SqlDbType.Json] = typeof(string)` in `TypeHelper._sqlDbTypeToType`,
  gated by a pre-flight task that verifies BOTH prerequisites are in
  place.
- **R2** — Confirmed downstream pipeline (OpenAPI, GraphQL,
  resolvers, EDM, metadata) handles `typeof(string)` columns without
  further changes.
- **R3** — **Superseded by 2026-06-09 Clarifications.** No operator
  allow-list gate. DAB forwards every filter operator to SQL.
- **R4** — Append JSON validation error numbers (currently 13608–13614,
  verified at implementation time) to
  `MsSqlDbExceptionParser.BadRequestExceptionCodes`. The response body
  contains the SQL Server error number per FR-007.
- **R5** — **Superseded by 2026-06-09 Clarifications.** No MCP
  annotation. JSON columns appear in MCP tools as plain string slots.
- **R6** — `profiles` table in `DatabaseSchema-MsSql.sql`; other engine
  schemas untouched.
- **R7** — No SQL Server version probe (FR-016).
- **R8** — `$orderby` works as-is (string-order).
- **R9** — Aggregation on JSON out-of-scope; existing engine behavior
  preserved.
- **R10** — Format + `dab validate` gates honored.
- **R11** — No `dab.draft.schema.json` change.

All NEEDS CLARIFICATION resolved (none remained from the spec after
the 2026-06-09 Clarifications session).

## Phase 1 — Design & Contracts (complete)

- [data-model.md](./data-model.md) — `profiles` test-fixture table and
  the `Profile` DAB entity (no production data model changes).
- [contracts/rest-openapi.md](./contracts/rest-openapi.md) — exact
  request/response shapes; SQL-Server-driven filter/orderby behavior
  (no DAB-side allow-list); 400 error envelope with the SQL error
  number.
- [contracts/graphql.md](./contracts/graphql.md) — generated SDL,
  query and mutation shapes, introspection contract.
- [contracts/mcp-tools.md](./contracts/mcp-tools.md) — a JSON column
  appears in MCP tools as a plain string slot; no annotation, no
  description, no format.
- [quickstart.md](./quickstart.md) — manual end-to-end validation
  walkthrough mapped to success criteria.
- Agent context — to be refreshed by `speckit.agent-context.update` so
  the SPECKIT block in `.github/copilot-instructions.md` points at this
  plan file.

## Phase 2 — Tasks (not in this command)

`/speckit.tasks` will derive an ordered task list from this plan, the
contracts, and the data model. Expected task families (preview only —
authoritative list is produced by `/speckit.tasks` and lives in
[tasks.md](./tasks.md)):

1. **Pre-flight: verify upstream dependency** — assert
   `Microsoft.Data.SqlClient >= 6.0.0` in
   [src/Directory.Packages.props](../../src/Directory.Packages.props),
   `<TargetFramework>net10.0</TargetFramework>` in every
   `src/**/*.csproj`, and `sdk.version: "10.0.x"` in
   [global.json](../../global.json). Verify `SqlDbType.Json` resolves
   at compile time (BCL enum value, requires .NET 9+). Fail fast with
   a message pointing at the joint prerequisite dependency PR if any
   condition is not met. **Does NOT modify**
   `Directory.Packages.props`, `global.json`, any `.csproj`,
   `external_licenses/`, `scripts/notice-generation.ps1`, or CI
   workflow files — those edits live in the separate prerequisite PR.
2. **Test fixture** (R6, FR-015) — add `profiles` table and seed rows to
   `DatabaseSchema-MsSql.sql`; add `Profile` entity to
   `dab-config.MsSql.json`.
3. **Type-mapping foundation** (R1, R2) — add the single
   `[SqlDbType.Json] = typeof(string)` entry in
   [src/Core/Services/TypeHelper.cs](../../src/Core/Services/TypeHelper.cs).
4. **Error code list extension** (R4, FR-007) — append the SQL Server
   JSON validation error numbers (13608–13614) to
   `MsSqlDbExceptionParser.BadRequestExceptionCodes`.
5. **Integration tests** (FR-001…FR-011, FR-015, FR-017) — REST +
   GraphQL CRUD tests, NULL handling, OpenAPI/GraphQL introspection
   tests, MCP "plain string" negative-assertion test, malformed-JSON
   400 test, and filter/order-by tests confirming that DAB forwards
   operators to SQL and surfaces SQL errors as 400.
6. **Regression guard** — explicit verification that PostgreSQL, MySQL,
   Cosmos, and DwSql categories remain at their pre-change pass counts
   (SC-002), plus a static diff guard on foreign fixtures and
   `schemas/dab.draft.schema.json`.
7. **Docs & release note** — minimum supported SQL Server version in
   the appropriate `docs/` page; release note line for the JSON
   feature.

## Complexity Tracking

*Empty intentionally — no Constitution violations to justify.* The
joint Microsoft.Data.SqlClient `6.x` + .NET 10 runtime upgrade is
delivered by a **separate prerequisite PR** (see Summary → Upstream
Dependency) and is therefore not a decision carried by this feature's
scope.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| *(none)* | — | — |
