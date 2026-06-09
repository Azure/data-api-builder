# Tasks: MSSQL JSON Data Type Support

**Feature**: 001-mssql-json-type | **Branch**: `Usr/sogh/speckit-jsontypesupport`
**Input**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

> **Revision 2026-06-09**: This tasks file was regenerated to reflect
> the 2026-06-09 Clarifications session in [spec.md](./spec.md). Per
> that session, DAB treats a SQL Server `JSON` column exactly like a
> string column. The previous task list (50 tasks across 9 phases)
> included an OData operator allow-list gate and MCP description
> annotations; both were dropped. The current task list is 22 tasks
> across 7 phases.
>
> The audit trail for the supersession lives in
> [research.md §R3 and §R5](./research.md).

**Tests obligation**: Constitution Principle II requires integration
tests under `TestCategory=MsSql` for every behavior change. Tests
below are therefore **required**, not optional.

## Format

`- [ ] [TaskID] [P?] [Story?] Description with file path`

- **[P]** — parallelizable: distinct files, no unfinished dependency.
- **[USn]** — user-story tag for tasks that implement / verify user
  stories US1–US9. Foundational, regression-guard, and polish tasks
  carry no story tag.

## Upstream Dependency — **NOT in this task list**

`Microsoft.Data.SqlClient >= 6.0.0` (and its companion edits to
[src/Directory.Packages.props](../../src/Directory.Packages.props),
`external_licenses/`, and
[scripts/notice-generation.ps1](../../scripts/notice-generation.ps1))
is delivered by a **separate prerequisite PR**. Tasks in this file
MUST NOT touch those three files. T001 verifies the prerequisite is
in place and fails fast if not.

---

## Phase 1: Pre-flight (Blocking)

**Purpose**: Confirm the upstream prerequisite PR has landed before
any code in this feature is written. Read-only — modifies no source
files.

- [ ] T001 Verify `Microsoft.Data.SqlClient >= 6.0.0` in [src/Directory.Packages.props](../../src/Directory.Packages.props) (read-only assertion; grep for `<PackageVersion Include="Microsoft.Data.SqlClient"` and confirm version >= 6.0.0). Verify `SqlDbType.Json` resolves at compile time by adding a one-line scratch build probe (then removing it) or by inspecting the SqlClient assembly via `dotnet list package`. **If absent, halt and link to the prerequisite PR.** MUST NOT modify [src/Directory.Packages.props](../../src/Directory.Packages.props), `external_licenses/`, or [scripts/notice-generation.ps1](../../scripts/notice-generation.ps1).

**Checkpoint**: Prerequisite confirmed. Implementation may begin.

---

## Phase 2: Foundational test fixtures (Blocking)

**Purpose**: Create the `Profile` / `profiles` test fixture so
integration tests have a database table and DAB entity to bind to.

- [ ] T002 Add the `profiles` table and the 5 seed rows defined in [data-model.md](./data-model.md) §Table Definition to [src/Service.Tests/DatabaseSchema-MsSql.sql](../../src/Service.Tests/DatabaseSchema-MsSql.sql). Format per copilot-instructions.md MSSQL rules (poorsql, 4-space indent, trailing commas). **MUST NOT touch** `DatabaseSchema-PostgreSql.sql`, `DatabaseSchema-MySql.sql`, `DatabaseSchema-DwSql.sql`, or any CosmosDB schema file (FR-012, Principle I).
- [ ] T003 Add the `Profile` entity entry to [src/Service.Tests/dab-config.MsSql.json](../../src/Service.Tests/dab-config.MsSql.json) (source object `dbo.profiles`, anonymous read role for tests as per existing fixture patterns), per [data-model.md](./data-model.md) §DAB Entity. Run `dab validate` against the updated config (no [schemas/dab.draft.schema.json](../../schemas/dab.draft.schema.json) change expected per FR-014 / R11).

**Checkpoint**: Test fixture exists; integration tests added in Phase 5 will resolve their entity references. No production behavior change yet.

---

## Phase 3: Type-mapping foundation

**Purpose**: Map `SqlDbType.Json` through the existing string pipeline
(R1, R2). Single-line dictionary edit; once landed, JSON columns flow
through every read / write path that already supports `string`.

- [ ] T004 Add `[SqlDbType.Json] = typeof(string)` to `TypeHelper._sqlDbTypeToType` in [src/Core/Services/TypeHelper.cs](../../src/Core/Services/TypeHelper.cs) (R1). Position the entry adjacent to the existing `NVarChar` / `Text` entries to keep the dictionary readable.
- [ ] T005 [P] Unit test: extend the `TypeHelper` test suite (under [src/Service.Tests](../../src/Service.Tests)) to assert `GetSystemTypeFromSqlDbType("json")` returns `typeof(string)` and that `GetJsonDataTypeFromSystemType(typeof(string))` continues to return `JsonDataType.String` (regression check for R2).

**Checkpoint**: Once T004 is merged, `MsSqlMetadataProvider.SqlToCLRType` resolves JSON columns; `OpenApiDocumentor` and `SchemaConverter` emit `string` / `String` automatically. Phase 5 US1 integration tests transition from red to green.

---

## Phase 4: Error-code list extension (SQL JSON errors → 400)

**Purpose**: Append SQL Server's JSON-validation error numbers to the
existing `MsSqlDbExceptionParser` "BadRequest" list so JSON-related
SQL errors surface as `400` instead of `500`. Covers BOTH malformed
JSON on write AND filter operators SQL Server cannot evaluate
against a JSON column (FR-007; R4 as elevated 2026-06-09).

- [ ] T006 Extend `MsSqlDbExceptionParser.BadRequestExceptionCodes` in [src/Core/Resolvers/MsSqlDbExceptionParser.cs](../../src/Core/Resolvers/MsSqlDbExceptionParser.cs) to include the SQL Server JSON-validation error numbers (R4 lists 13608–13614 as the starting set). PRUNE the speculative set to the actually-triggered numbers using T013 / T014 / T017 / T018 during implementation. Verify that the response body includes the SQL Server error number (R4 notes the existing error envelope serializer may need a small adjustment — verify and amend in this same task or call out a follow-up).
- [ ] T007 [P] Unit test: add a `MsSqlDbExceptionParserTests` case (under [src/Service.Tests](../../src/Service.Tests)) asserting that a `SqlException` carrying each of the appended error numbers maps to `HttpStatusCode.BadRequest` via `GetHttpStatusCodeForException`, while a still-unknown number maps to its prior status (regression). Uses the existing `MakeSqlException`-style helper if present, else mocks `SqlException` via reflection per existing test patterns.

**Checkpoint**: Once T006 lands, every SQL Server JSON error surfaces as 400 / `BAD_REQUEST` with the SQL error number in the response body. T013 / T014 (malformed write) and T017 / T018 (unsupported filter operator) go green.

---

## Phase 5: Integration tests (REST + GraphQL + MCP) — `TestCategory=MsSql`

**Purpose**: Pin every functional requirement (FR-001 … FR-017) with
at least one MSSQL integration test under [src/Service.Tests](../../src/Service.Tests).
Add new test classes / files where the existing layout lacks an
obvious home; otherwise extend the closest existing test file (e.g.,
`MsSqlRestApiTests`, `MsSqlGraphQLMutationTests`,
`MsSqlGraphQLQueryTests`, `OpenApiDocumentMsSqlTests`,
`McpToolRegistryTests`).

All tests carry `[TestCategory(TestCategoryConstants.MSSQL)]` (or
equivalent) and use the Profile fixture from Phase 2. Reference
shapes: [contracts/rest-openapi.md](./contracts/rest-openapi.md),
[contracts/graphql.md](./contracts/graphql.md),
[contracts/mcp-tools.md](./contracts/mcp-tools.md).

### US1 — Schema discovery surface

- [ ] T008 [P] [US1] REST OpenAPI test in [src/Service.Tests](../../src/Service.Tests) (extend or add a `MsSqlOpenApiJsonColumnTests.cs` near existing OpenAPI MSSQL tests): GET `/api/openapi`; assert `components.schemas.Profile.properties.metadata` has `type: string`, `nullable: true`, and **no** `format`, **no** `pattern`, **no** `x-dab-*` extension. Cites **FR-001, FR-014, FR-013**.
- [ ] T009 [P] [US1] GraphQL introspection test in [src/Service.Tests](../../src/Service.Tests) (extend `MsSqlGraphQLQueryTests` or add `MsSqlGraphQLJsonColumnIntrospectionTests.cs`): introspect `__type(name:"Profile")` and assert `metadata` is `{kind: SCALAR, name: "String"}`; introspect `CreateProfileInput` / `UpdateProfileInput` and assert `metadata` is nullable `String`; introspect `__schema.types` and assert **no** new scalar named `Json` / `JSON` is registered. Cites **FR-001, FR-003, FR-005, FR-013**.
- [ ] T010 [P] [US1] MCP `describe_entities` **negative-assertion** test in [src/Service.Tests/Mcp/McpToolRegistryTests.cs](../../src/Service.Tests/Mcp/McpToolRegistryTests.cs) (or a new sibling `Mcp/McpDescribeEntitiesJsonColumnTests.cs`): invoke `describe_entities` for the `Profile` entity; assert the `metadata` field metadata is **identical in shape** to a plain string column — **no** `description`, **no** `format`, **no** JSON-specific extension. Guards against accidental regression of the 2026-06-09 design. Cites **FR-017**, [contracts/mcp-tools.md](./contracts/mcp-tools.md) §Governing principle.

### US2 — Read single row

- [ ] T011 [US2] REST GET single test (extend existing MSSQL REST GET test class): `GET /api/Profile/id/1` → assert response `value[0].metadata` is a JSON string equal to the seed text from [data-model.md](./data-model.md) for row 1 (verbatim `SELECT metadata`-equivalence, not byte-equality vs the original insert literal — per spec Edge Cases). Cites **FR-002**.
- [ ] T012 [US2] GraphQL single-item test (extend `MsSqlGraphQLQueryTests`): `query { profile_by_pk(id: 1) { id metadata } }` → assert `metadata` returned as `String` equal to the same value asserted in T011. Cites **FR-003**.

### US3 — Read collection

- [ ] T013 [P] [US3] REST list test: `GET /api/Profile` → assert each of the 5 seeded rows renders `metadata` either as the expected JSON string or `null` (row 5). Run again with `$select=metadata` and assert the field is still a JSON string (no expansion). Cites **FR-002, FR-008**.
- [ ] T014 [P] [US3] GraphQL list test: `query { profiles { items { id metadata } } }` → assert same five-row outcome. Cites **FR-003, FR-008**.

### US4 — Insert via REST & GraphQL

- [ ] T015 [US4] REST insert happy-path test: `POST /api/Profile` with `{ "metadata": "{\"role\":\"guest\"}" }` → assert 201, response `metadata` echoes the input string verbatim, and a follow-up `GET` returns the same. Cites **FR-004, FR-006**.
- [ ] T016 [US4] GraphQL create happy-path test: `mutation { createProfile(item: { metadata: "{\"role\":\"guest\"}" }) { id metadata } }` → assert success with echoed metadata. Also exercise an object literal for `metadata` and assert the request is rejected by Hot Chocolate's input validator (no DAB code runs, no row created). Cites **FR-005**.

### US5 — Update via PUT, PATCH, GraphQL

- [ ] T017 [US5] REST PUT and PATCH tests: PUT then PATCH `metadata` to a new valid JSON string on an existing row; assert response and database both reflect the new string; assert PATCH leaves untouched columns unchanged. Cites **FR-004**.
- [ ] T018 [US5] GraphQL `updateProfile` test: mutate `metadata` to a new string; assert success and echoed value. Cites **FR-005**.

### US6 — Delete

- [ ] T019 [US6] REST DELETE and GraphQL `deleteProfile` smoke tests on a row whose `metadata` is non-null; assert success and absence of the row in a follow-up GET. Cites **FR-011**.

### US7 — SQL Server JSON errors surface as 400

**Note**: After the 2026-06-09 supersession, this story covers BOTH
malformed JSON on write AND filter operators SQL Server cannot
evaluate against a JSON column. Each test below MUST assert that the
response body contains the SQL Server error number (R4).

- [ ] T020 [US7] REST malformed-JSON write test: `POST /api/Profile` with `{ "metadata": "{not valid json" }` → assert HTTP `400`, no row persisted, error body contains the SQL Server error number that was raised. Use this test (together with T021, T022, T023) to PRUNE the speculative error-code set added in T006 down to the actually-triggered numbers. Cites **FR-007, SC-004**.
- [ ] T021 [US7] GraphQL malformed-JSON write test: equivalent `createProfile` mutation; assert GraphQL error with `extensions.code == "BAD_REQUEST"`, no row created, message text contains the SQL Server error number. Cites **FR-007**.

### US8 — Null handling

- [ ] T022 [US8] REST null-handling tests: `POST /api/Profile { "metadata": null }` and `POST` with `metadata` omitted → both persist SQL `NULL`; `PATCH /api/Profile/id/1 { "metadata": null }` → clears existing value; `GET` renders `"metadata": null`. Plus matching GraphQL `createProfile(item: { metadata: null })` and `updateProfile(... item: { metadata: null })` round-trips. Cites **FR-008**.

### US9 — Filter & orderby pass-through

**Note**: After the 2026-06-09 supersession there is no DAB operator
allow-list. Every operator the OData / GraphQL filter layer accepts
is forwarded to SQL Server. The tests below pin the pass-through
behavior in both the success and failure directions.

- [ ] T023 [P] [US9] REST `$filter` pass-through (success) tests: `metadata eq '<exact-string>'`, `metadata ne '<...>'`, `metadata eq null`, `metadata ne null` — each returns the expected subset of rows (SQL Server currently supports these on the JSON type). Cites **FR-009**.
- [ ] T024 [P] [US9] REST `$filter` pass-through (SQL-rejected) tests: `contains(metadata,'admin')`, `startswith`, `endswith`, `gt`, `lt`, `ge`, `le` against `metadata` — assert each request is **forwarded** to SQL Server (no DAB pre-rejection), SQL Server raises a JSON error, and DAB surfaces `400` with the SQL Server error number in the response body. The test asserts behavior, not a specific operator-support matrix — if SQL Server adds operator support in a future release, the test for that operator should move to T023. Cites **FR-009, FR-007**.
- [ ] T025 [P] [US9] REST `$orderby` test: `$orderby=metadata` and `$orderby=metadata desc` → assert order matches a plain string sort of the seeded `metadata` values (NULL placement per existing DAB convention). Cites **FR-010**.
- [ ] T026 [P] [US9] GraphQL `StringFilterInput` pass-through tests mirroring T023 and T024 on the `metadata` filter input. Same pass / fail split, same SQL-error-number assertion on the failure cases. Cites **FR-009, FR-007**.
- [ ] T027 [P] [US9] GraphQL `orderBy: { metadata: ASC }` test mirroring T025. Cites **FR-010**.

### Edge-case coverage

- [ ] T028 [P] Unicode round-trip test (REST + GraphQL): insert and read back row 4 (`{"unicode":"éü😀"}`) — assert text identical. Cites **FR-002, FR-003** and Edge Cases.
- [ ] T029 [P] Nested-object seed read test: `GET /api/Profile/id/3` → assert `metadata` is the deeply-nested JSON string preserved verbatim. Cites **FR-002**.
- [ ] T030 [P] Array-payload seed read test: `GET /api/Profile/id/2` → assert `metadata` is the array-bearing JSON string preserved verbatim. Cites **FR-002**.

### Server-version pass-through (FR-016) — documentation-only

- [ ] T031 Documentation cross-check: add a short note in a test-doc comment (or a small README under [src/Service.Tests](../../src/Service.Tests)) that the JSON tests REQUIRE SQL Server 2025+ / Azure SQL DB current and that no DAB-side version probe exists. No code probe is added (FR-016, R7). Cites **FR-016**.

**Checkpoint**: All MSSQL integration tests for US1–US9 + FR-017 pass on a SQL Server 2025+ build (SC-001).

---

## Phase 6: Regression guard (Principle I, FR-012, SC-002)

**Purpose**: Verify NON-MSSQL engines were not perturbed, even
inadvertently.

- [ ] T032 Run `dotnet test src/Service.Tests --filter "TestCategory=PostgreSql"`, `"TestCategory=MySql"`, and `"TestCategory=CosmosDb_NoSql"` and confirm the pass / fail counts are identical to the pre-change baseline (capture baseline from `main` or the latest tagged green run). Record the counts in the PR description. Sanity-check by `grep`-ing the diff for accidental edits to `DatabaseSchema-PostgreSql.sql`, `DatabaseSchema-MySql.sql`, `DatabaseSchema-DwSql.sql`, `dab-config.{PostgreSql,MySql,DwSql,CosmosDb_NoSql}.json`, and any Cosmos / Postgres / MySql / DwSql resolver source file.

---

## Phase 7: Polish

- [ ] T033 Run `dotnet format src/Azure.DataApiBuilder.sln` (per copilot-instructions.md) and address any drift introduced by the edits in this feature.
- [ ] T034 Manual smoke from [quickstart.md](./quickstart.md): walk Steps 1–10 against a real SQL Server 2025+ instance and confirm the expected responses for each step. Note any drift between quickstart text and observed behavior in a follow-up PR description bullet.

---

## Dependency graph (informational)

```
T001 (pre-flight)
  └── T002, T003 (fixtures)
         ├── T004 ──► T005, T008, T009, T010, T011, T012, T013, T014, T015, T016, T017, T018, T019, T022, T023, T025, T026, T027, T028, T029, T030
         └── T006 ──► T007, T020, T021, T024, T026 (failure half)
T008…T030 ──► T032 (regression baseline diff) ──► T033, T034 (polish)
```

## Cross-reference index

| Requirement | Covered by |
|-------------|------------|
| FR-001 | T008, T009 |
| FR-002 | T011, T013, T028, T029, T030 |
| FR-003 | T012, T014, T028 |
| FR-004 | T015, T017 |
| FR-005 | T016, T018 |
| FR-006 | T015 |
| FR-007 | T006, T007, T020, T021, T024, T026 |
| FR-008 | T013, T014, T022 |
| FR-009 | T023, T024, T026 |
| FR-010 | T025, T027 |
| FR-011 | T019 |
| FR-012 | T002, T003, T032 |
| FR-013 | T008, T009 |
| FR-014 | T003, T008 |
| FR-015 | (regression — covered by US2–US6 happy-path tests touching reads / writes) |
| FR-016 | T031 |
| FR-017 | T010 |
| SC-001 | Phase 5 checkpoint |
| SC-002 | T032 |
| SC-003 | T008, T009 |
| SC-004 | T006, T007, T020, T021, T024, T026 |
