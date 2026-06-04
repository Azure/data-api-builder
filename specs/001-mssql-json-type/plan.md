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

The implementation is intentionally small-surface: a Microsoft.Data.SqlClient
package bump unlocks `SqlDbType.Json`, after which the column flows
through the existing string-type path with five surgical edits
(TypeHelper dictionary entry, MsSql exception-parser error-code list,
OData visitor operator gate, two MCP description hints) plus the test
fixture additions. See [research.md](./research.md) for the per-file
analysis.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (per [global.json](../../global.json)).

**Primary Dependencies**: Hot Chocolate (GraphQL), Microsoft.OData.UriParser
(REST filter), Microsoft.Data.SqlClient → upgrade from `5.2.3` to
`6.x` (research R1; required for `SqlDbType.Json`).

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
touchpoints. The decision to bump Microsoft.Data.SqlClient (R1) is the
only non-trivial decision; rationale is recorded in research.md and is
narrower than the rejected alternatives.

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
├── Directory.Packages.props                       # R1: bump Microsoft.Data.SqlClient
├── Core/
│   ├── Services/
│   │   └── TypeHelper.cs                          # R1: SqlDbType.Json -> typeof(string)
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

scripts/
└── notice-generation.ps1                          # R1: update SqlClient license URL
```

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

- **R1** — Bump `Microsoft.Data.SqlClient` `5.2.3 → 6.x` to unlock
  `SqlDbType.Json`; add one dictionary entry.
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

1. **Dependency bump & type-mapping foundation** (R1, R2).
2. **Operator allow-list gate** (R3) — code + unit tests + MSSQL
   integration tests for FR-009 + REST/GraphQL parity tests.
3. **Error mapping** (R4) — code + integration tests for FR-007 (REST
   400 + GraphQL `BAD_REQUEST`).
4. **MCP description hints** (R5/FR-017) — code + integration tests
   asserting the description string in `describe_entities` and
   `dynamic_custom_tool` output.
5. **Test fixture & happy-path integration tests** (R6, FR-001…FR-008)
   — schema additions, `Profile` entity, REST + GraphQL CRUD tests,
   NULL handling, OpenAPI/GraphQL introspection tests.
6. **Regression guard** — explicit verification (e.g., row-count
   smoke tests or noted CI check) that PostgreSQL, MySQL, Cosmos, and
   DwSql categories remain at their pre-change pass counts (SC-002).
7. **Docs & release note** — note minimum supported SQL Server version
   in the appropriate `docs/` page; release note line for the JSON
   feature.

## Complexity Tracking

*Empty intentionally — no Constitution violations to justify.* The
single notable decision (Microsoft.Data.SqlClient version bump) is
recorded in research.md R1 with alternatives; it is not a Principle
violation but a deliberate, narrower-than-alternatives choice.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| *(none)* | — | — |
