# Implementation Plan: MSSQL JSON Data Type Support

**Branch**: `Usr/sogh/speckit-jsontypesupport` | **Date**: 2026-06-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [specs/001-mssql-json-type/spec.md](./spec.md)

## Summary

Add first-class support for the SQL Server 2025+ / Azure SQL DB native
`JSON` column type to DAB. The column surfaces as a String in REST
(OpenAPI), GraphQL (built-in `String` scalar), and MCP tool schemas;
DAB treats the value as an opaque JSON-encoded string in both
directions and delegates JSON syntax validation to SQL Server. Scope
is **MSSQL only**; other engines are unaffected.

The implementation is intentionally small-surface. It **depends on** a
Microsoft.Data.SqlClient upgrade to a `6.x` line that exposes
`SqlDbType.Json`; that upgrade is delivered as a **separate prerequisite
PR** (see "Upstream Dependency" below) and is **out of scope** for this
feature's task list. Once the dependency is in place, this feature
adds five surgical edits (TypeHelper dictionary entry, MsSql
exception-parser error-code list, OData visitor operator gate, two MCP
description hints) plus the test fixture additions. See
[research.md](./research.md) for the per-file analysis.

### Upstream Dependency (separate PR)

**Prerequisite**: `Microsoft.Data.SqlClient >= 6.0.0` in
[src/Directory.Packages.props](../../src/Directory.Packages.props). This
bump (and its license-notice refresh) is delivered by a **separate,
behavior-preserving dependency PR** so it can be reviewed, validated
against the full multi-engine CI matrix, and merged independently of
this feature. This PR's `tasks.md` MUST NOT include the package bump
or any of its companion edits (license URL, NOTICE regeneration).

**Blocking**: This feature's implementation tasks cannot be completed
until the dependency PR is merged into the same target branch. A
pre-flight task verifies the installed version and fails fast with a
clear message if the prerequisite is missing.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (per [global.json](../../global.json)).

**Primary Dependencies**: Hot Chocolate (GraphQL),
Microsoft.OData.UriParser (REST filter), Microsoft.Data.SqlClient
**>= 6.0.0** (prerequisite — required for `SqlDbType.Json`; delivered
by a separate dependency PR, see Summary and research R1).

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
│   ├── Resolvers/
│   │   └── MsSqlDbExceptionParser.cs              # R4: add JSON error codes to BadRequest set
│   └── Parsers/
│       └── ODataASTVisitor.cs                     # R3: operator allow-list gate for JSON columns
├── Azure.DataApiBuilder.Mcp/
│   ├── BuiltInTools/
│   │   └── DescribeEntitiesTool.cs                # R5/FR-017: JSON column description
│   └── Core/
│       └── DynamicCustomTool.cs                   # R5/FR-017: JSON SP-parameter description
└── Service.Tests/
    ├── DatabaseSchema-MsSql.sql                   # R6: profiles table + seed rows
    ├── dab-config.MsSql.json                      # R6: Profile entity entry
    └── ...                                        # New MSSQL integration tests (see tasks.md)
```

**Delivered by the prerequisite dependency PR (NOT in this feature's tasks)**:

- `src/Directory.Packages.props` — Microsoft.Data.SqlClient `5.2.3 → 6.x`
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

See [research.md](./research.md). Eleven research items resolved:

- **R1** — `Microsoft.Data.SqlClient >= 6.0.0` (prerequisite; delivered
  by a separate PR) unlocks `SqlDbType.Json`. This feature contributes
  the single dictionary entry `[SqlDbType.Json] = typeof(string)` in
  `TypeHelper._sqlDbTypeToType`, gated by a pre-flight task that
  verifies the dependency is in place.
- **R2** — Confirmed downstream pipeline (OpenAPI, GraphQL,
  resolvers, EDM, metadata) handles `typeof(string)` columns without
  further changes.
- **R3** — Operator allow-list gate in `ODataASTVisitor.PopulateDbTypeForProperty`.
- **R4** — JSON validation error codes (`13608`–`13614` range) into
  `MsSqlDbExceptionParser.BadRequestExceptionCodes`.
- **R5** — MCP description hint in `DescribeEntitiesTool` and
  `DynamicCustomTool.BuildInputSchemaFromDbMetadata`; CreateRecord/
  UpdateRecord input schemas are intentionally not refactored.
- **R6** — `profiles` table in `DatabaseSchema-MsSql.sql`; other engine
  schemas untouched.
- **R7** — No SQL Server version probe (FR-016).
- **R8** — `$orderby` works as-is (string-order).
- **R9** — Aggregation on JSON out-of-scope; existing engine behavior
  preserved.
- **R10** — Format + `dab validate` gates honored.
- **R11** — No `dab.draft.schema.json` change.

All NEEDS CLARIFICATION resolved (none remained from the spec after
`/speckit.clarify`).

## Phase 1 — Design & Contracts (complete)

- [data-model.md](./data-model.md) — `profiles` test-fixture table and
  the `Profile` DAB entity (no production data model changes).
- [contracts/rest-openapi.md](./contracts/rest-openapi.md) — exact
  request/response shapes, filter/orderby allow-list, error envelope.
- [contracts/graphql.md](./contracts/graphql.md) — generated SDL,
  query and mutation shapes, introspection contract.
- [contracts/mcp-tools.md](./contracts/mcp-tools.md) — JSON-column
  description annotation in `describe_entities` and `dynamic_custom_tool`.
- [quickstart.md](./quickstart.md) — manual end-to-end validation
  walkthrough mapped to success criteria.
- Agent context — to be refreshed by `speckit.agent-context.update` so
  the SPECKIT block in `.github/copilot-instructions.md` points at this
  plan file.

## Phase 2 — Tasks (not in this command)

`/speckit.tasks` will derive an ordered task list from this plan, the
contracts, and the data model. Expected task families (preview only —
authoritative list is produced by `/speckit.tasks`):

1. **Pre-flight: verify upstream dependency** — assert
   `Microsoft.Data.SqlClient >= 6.0.0` in
   [src/Directory.Packages.props](../../src/Directory.Packages.props)
   and that `SqlDbType.Json` resolves at compile time. Fail fast with
   a message pointing at the prerequisite dependency PR if not met.
   **Does NOT modify** `Directory.Packages.props`, `external_licenses/`,
   or `scripts/notice-generation.ps1` — those edits live in the
   separate prerequisite PR.
2. **Type-mapping foundation** (R1, R2) — add the single
   `[SqlDbType.Json] = typeof(string)` entry in
   [src/Core/Services/TypeHelper.cs](../../src/Core/Services/TypeHelper.cs).
3. **Operator allow-list gate** (R3) — code + unit tests + MSSQL
   integration tests for FR-009 + REST/GraphQL parity tests.
4. **Error mapping** (R4) — code + integration tests for FR-007 (REST
   400 + GraphQL `BAD_REQUEST`).
5. **MCP description hints** (R5/FR-017) — code + integration tests
   asserting the description string in `describe_entities` and
   `dynamic_custom_tool` output.
6. **Test fixture & happy-path integration tests** (R6, FR-001…FR-008)
   — schema additions, `Profile` entity, REST + GraphQL CRUD tests,
   NULL handling, OpenAPI/GraphQL introspection tests.
7. **Regression guard** — explicit verification (e.g., row-count
   smoke tests or noted CI check) that PostgreSQL, MySQL, Cosmos, and
   DwSql categories remain at their pre-change pass counts (SC-002).
8. **Docs & release note** — note minimum supported SQL Server version
   in the appropriate `docs/` page; release note line for the JSON
   feature.

## Complexity Tracking

*Empty intentionally — no Constitution violations to justify.* The
Microsoft.Data.SqlClient `6.x` upgrade is delivered by a **separate
prerequisite PR** (see Summary → Upstream Dependency) and is therefore
not a decision carried by this feature's scope.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| *(none)* | — | — |
