# Specification Quality Checklist: MSSQL JSON Data Type Support

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-06-04
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Validation pass 1: all items satisfied; no [NEEDS CLARIFICATION] markers
  were needed because the user-provided description was unusually concrete
  (explicit user stories, explicit out-of-scope list, explicit acceptance
  criteria for error and null handling).
- Validation pass 2 (post-`/speckit.clarify`, 2026-06-04): 5 clarifications
  recorded; all 16 checklist items remain passing. FR-004/005 (strict
  string write), FR-007 (400 / `BAD_REQUEST`), FR-009 (operator allow-list),
  FR-016 (no version probe), and FR-017 (MCP description hint) are now
  fully pinned. No residual ambiguity blocks `/speckit.plan`.
- Spec references "REST", "GraphQL", "OpenAPI", "SDL", "MCP", and "SQL
  Server JSON column type" — these are part of DAB's product surface and
  the feature's intrinsic domain, not implementation leakage.

---

## Pre-Tasks Quality Gate (2026-06-05)

**Purpose**: Validate that [spec.md](../spec.md) (with [plan.md](../plan.md),
[research.md](../research.md), [data-model.md](../data-model.md), and
[contracts/](../contracts/) as supporting context) is testable,
unambiguous, and complete enough to hand to `/speckit.tasks`.

**Scope dimensions**: coverage (FR-001…FR-017), clarity, boundaries,
cross-artifact consistency, test obligation, dependency hygiene,
constitution alignment. Each item is answerable Yes/No against the
listed artifacts.

### Coverage — every FR has an observable assertion

- [ ] CHK017 - Does FR-001 specify observable signals in BOTH [contracts/rest-openapi.md](../contracts/rest-openapi.md) (`type: string`, `nullable: true`) AND [contracts/graphql.md](../contracts/graphql.md) (`String` scalar) that an integration test can assert? [Coverage, Spec §FR-001]
- [ ] CHK018 - Does FR-002 (REST JSON-string rendering) have a concrete escaped-string shape example in [contracts/rest-openapi.md](../contracts/rest-openapi.md) so a test can string-equal-compare the response? [Coverage, Spec §FR-002]
- [ ] CHK019 - Does FR-003 (GraphQL `String` rendering) have a concrete escaped-string shape example in [contracts/graphql.md](../contracts/graphql.md)? [Coverage, Spec §FR-003]
- [ ] CHK020 - Does FR-004 specify the exact rejection signal (HTTP 400 + error-body field identifier) for a nested-object REST input so the test can assert both? [Clarity, Spec §FR-004, contracts/rest-openapi.md]
- [ ] CHK021 - Does FR-005 specify HOW GraphQL rejects nested-object input (parser/validator vs DAB-side gate) so the test target is unambiguous? [Clarity, Spec §FR-005, contracts/graphql.md]
- [ ] CHK022 - Does FR-006 ("no DAB-side JSON-schema validation") have a distinct test obligation separate from FR-007's malformed-input test, or is it documented as covered transitively? [Coverage, Spec §FR-006, Gap]
- [ ] CHK023 - Does FR-007 cite the exact HTTP status (`400`) AND GraphQL extension code (`BAD_REQUEST`) verbatim, matching both [contracts/rest-openapi.md](../contracts/rest-openapi.md) and [contracts/graphql.md](../contracts/graphql.md)? [Consistency, Spec §FR-007]
- [ ] CHK024 - Does FR-008 enumerate every null-pathway (POST `null`, POST omitted, PATCH `null`, PUT `null`, read `null`) so each is independently testable? [Completeness, Spec §FR-008, US 8]
- [ ] CHK025 - Does FR-009 enumerate the rejected operators verbatim, matching both the REST filter table in [contracts/rest-openapi.md](../contracts/rest-openapi.md) and the GraphQL StringFilterInput-rejection list in [contracts/graphql.md](../contracts/graphql.md)? [Consistency, Spec §FR-009]
- [ ] CHK026 - Does FR-010 define what "string-order" means for assertion purposes (e.g., reference to the seed-row expected order in [data-model.md](../data-model.md))? [Measurability, Spec §FR-010]
- [ ] CHK027 - Does FR-011 (delete unchanged) have a documented test obligation (existing-row delete on a JSON-bearing row), or is it asserted as a "no regression" note only? [Coverage, Spec §FR-011]
- [ ] CHK028 - Does FR-012 name every excluded engine (PostgreSQL, MySQL, CosmosDB NoSQL, SQLDW) so a tester can verify each suite's count is unchanged per SC-002? [Completeness, Spec §FR-012]
- [ ] CHK029 - Does FR-013 specify an observable check (no new scalar in `__schema.types`) that a test can issue against the live GraphQL endpoint? [Measurability, Spec §FR-013, contracts/graphql.md]
- [ ] CHK030 - Does FR-014 specify which CLI commands (`dab init`, `dab add`) are in scope so a tester knows the surface to verify is unchanged? [Clarity, Spec §FR-014]
- [ ] CHK031 - Does FR-015 commit to a specific table name and column name traceable to [data-model.md](../data-model.md) (`profiles.metadata`) so tasks.md can pin the fixture? [Completeness, Spec §FR-015, data-model.md]
- [ ] CHK032 - Does FR-016 specify the user-observable behavior on unsupported servers (startup-time vs request-time error path) so error-path tests have a defined target? [Clarity, Spec §FR-016, research.md §R7]
- [ ] CHK033 - Does FR-017 commit to a verifiable description SUBSTANCE (assertable via "contains" check) that does NOT pin exact wording, so the implementation can evolve the string? [Measurability, Spec §FR-017]

### Clarity — no ambiguous verbs; every MUST is measurable

- [ ] CHK034 - Are all "MUST" statements in FR-001…FR-017 paired with an externally observable signal (HTTP status, field shape, error code, response substring, schema element)? [Clarity, Spec §Requirements]
- [ ] CHK035 - Are vague verbs ("handle", "support", "manage", "process", "work correctly") absent from FR-001…FR-017 and the Acceptance Scenarios? [Ambiguity, Spec §Requirements, §User Scenarios]
- [ ] CHK036 - Is "preserved escaping, no re-formatting" (User Story 2) defined in terms of `SELECT metadata`-equivalence rather than client-byte-equality, so round-trip tests have a deterministic oracle? [Clarity, Spec §Edge Cases]
- [ ] CHK037 - Is the "raw SQL error MUST NOT be leaked" rule in FR-007 qualified by mode (developer vs production), matching [research.md](../research.md) §R4 `GetMessage(DbException)`? [Clarity, Spec §FR-007, research.md §R4]

### Boundaries — out-of-scope ↔ non-goal matched

- [ ] CHK038 - Does [spec.md](../spec.md) include an explicit "Out of Scope" / "Non-Goals" section, or is the scope boundary only implicit via FR-012/FR-013/FR-014/FR-016 and the Assumptions section? [Gap, Spec]
- [ ] CHK039 - Is the `Microsoft.Data.SqlClient 5.2.3 → 6.x` package bump declared as a non-goal of THIS feature's `tasks.md` in [plan.md](../plan.md) / [research.md](../research.md) §R1, so the generated tasks.md will not include it? [Boundaries, plan.md, research.md §R1]
- [ ] CHK040 - Is aggregation / group-by on JSON columns explicitly recorded as out-of-scope in [research.md](../research.md) §R9 so reviewers don't add aggregate-rejection logic? [Boundaries, research.md §R9]
- [ ] CHK041 - Is the explicit absence of a SQL Server version probe (FR-016) restated in [plan.md](../plan.md) so a reviewer cannot quietly add one during implementation? [Consistency, plan.md, Spec §FR-016]
- [ ] CHK042 - Is the refusal to refactor `CreateRecordTool` / `UpdateRecordTool` input schemas to add per-column slots documented as a non-goal in [contracts/mcp-tools.md](../contracts/mcp-tools.md) and [research.md](../research.md) §R5, so per-column annotations on those tools are not silently added? [Boundaries, research.md §R5, contracts/mcp-tools.md]

### Cross-artifact consistency

- [ ] CHK043 - Does the FR-009 operator allow-list in [spec.md](../spec.md) (`eq`, `ne`, null checks only) match the "Allowed/Rejected" table in [contracts/rest-openapi.md](../contracts/rest-openapi.md) verbatim? [Consistency, Spec §FR-009, contracts/rest-openapi.md]
- [ ] CHK044 - Does the FR-009 rejection set match the GraphQL StringFilterInput-rejection list in [contracts/graphql.md](../contracts/graphql.md) (`contains`, `startsWith`, `endsWith`, `gt`, `lt`, `gte`, `lte`)? [Consistency, Spec §FR-009, contracts/graphql.md]
- [ ] CHK045 - Does the FR-007 error mapping (`400` / `BAD_REQUEST`) align with the SQL error code set in [research.md](../research.md) §R4 (`13608`, `13609`, `13614`, optionally `13610`–`13613`), and is the "exact set MUST be re-verified at implementation" caveat carried forward? [Consistency, Spec §FR-007, research.md §R4]
- [x] CHK046 - RESOLVED: Canonical description string in [contracts/mcp-tools.md](../contracts/mcp-tools.md) updated to "Do not send a nested object or array" to match FR-017 wording. [Consistency, Spec §FR-017, contracts/mcp-tools.md]
- [x] CHK047 - RESOLVED: Spec FR-017 tightened to match R5's design — annotation surface is `DescribeEntitiesTool` (per-field) + `DynamicCustomTool` (per-SP parameter). `CreateRecordTool` and `UpdateRecordTool` input schemas remain unmodified; agents discover JSON columns via `DescribeEntitiesTool` (existing tool descriptions already mandate it as STEP 1). [Consistency, Spec §FR-017, research.md §R5, contracts/mcp-tools.md]
- [x] CHK048 - RESOLVED: Spec FR-017 now explicitly requires integration tests to assert the description on (i) at least one JSON field in `DescribeEntitiesTool` output AND (ii) at least one JSON parameter in a `DynamicCustomTool` per-SP input schema. [Coverage, Spec §FR-017]
- [ ] CHK049 - Does the [data-model.md](../data-model.md) `profiles` table definition match the schema sketch in [research.md](../research.md) §R6 (column name `metadata`, type `JSON NULL`, `IDENTITY(1,1)` primary key, same 5 seed rows)? [Consistency, data-model.md, research.md §R6]
- [ ] CHK050 - Does User Story 1 (REST OpenAPI shape) align with [contracts/rest-openapi.md](../contracts/rest-openapi.md) `components.schemas.Profile` (no `format`, no `pattern`, no `x-dab-` extension)? [Consistency, Spec §US1, contracts/rest-openapi.md]
- [ ] CHK051 - Does the GraphQL introspection contract in [contracts/graphql.md](../contracts/graphql.md) ("`__type(name: "Profile")` MUST report `metadata` as `SCALAR`/`String`") explicitly back FR-013's "no new scalar" requirement? [Consistency, contracts/graphql.md, Spec §FR-013]
- [ ] CHK052 - Does the spec.md Clarifications session Q1 (strict string write) match FR-004 and FR-005 exactly (both REST and GraphQL paths)? [Consistency, Spec §Clarifications, §FR-004, §FR-005]

### Test obligation — `TestCategory=MsSql` binding

- [ ] CHK053 - Is the `TestCategory=MsSql`-only obligation stated in spec (FR-015) AND re-stated in [plan.md](../plan.md) Constitution Check (Principle II row)? [Traceability, Spec §FR-015, plan.md]
- [ ] CHK054 - For each FR (FR-001…FR-017), is there a traceable test obligation — an Acceptance Scenario, an Edge Case bullet, or a contract assertion — that a tasks.md author can convert into a single MSSQL integration test? [Coverage, Spec §Requirements]
- [ ] CHK055 - Is "0 regressions in other engine categories" pinned as a measurable check (SC-002) so reviewers know to look for accidental edits to `DatabaseSchema-PostgreSql.sql`, `DatabaseSchema-MySql.sql`, `DatabaseSchema-DwSql.sql`, and Cosmos fixtures? [Measurability, Spec §SC-002, plan.md]
- [ ] CHK056 - Are the Edge Cases in [spec.md](../spec.md) (empty string `""`, very large payload, Unicode + `\uXXXX`, whitespace/key-order, concurrent updates, column-level permissions) each tied to either a documented test obligation OR an explicit "not separately tested" note? [Coverage, Spec §Edge Cases]
- [ ] CHK057 - Does FR-015 require integration tests under `TestCategory=MsSql` ONLY, and does the plan.md "Files explicitly NOT touched" list forbid edits to other-engine fixtures? [Boundaries, Spec §FR-015, plan.md]

### Dependency hygiene — Microsoft.Data.SqlClient as a separate PR

- [ ] CHK058 - Does [plan.md](../plan.md) and/or [research.md](../research.md) §R1 state explicitly that `src/Directory.Packages.props` MUST NOT be modified by this feature's `tasks.md`? [Boundaries, plan.md, research.md §R1]
- [ ] CHK059 - Does [research.md](../research.md) §R1 prescribe a pre-flight task in `tasks.md` that fails fast (with a pointer to the prerequisite PR) if `Microsoft.Data.SqlClient < 6.0.0` is detected? [Completeness, research.md §R1]
- [ ] CHK060 - Are the files attributed to the prerequisite dependency PR (`src/Directory.Packages.props`, `external_licenses/`, `scripts/notice-generation.ps1`) consistent between [research.md](../research.md) §R1 and [plan.md](../plan.md) "Files explicitly NOT touched"? [Consistency, plan.md, research.md §R1]
- [ ] CHK061 - Does the dependency hygiene constraint propagate to MCP tooling — i.e., is there any FR or task obligation that would FORCE a SqlClient API surface only available in 6.x (e.g., `SqlDbType.Json` literal usage) into a code path that this feature's tasks.md does not touch? [Boundaries, research.md §R1, §R2]

### Constitution alignment — Principles I–VII

- [ ] CHK062 - Is Principle I (Multi-Engine Parity) satisfied with an explicit scope-out reason for non-MSSQL engines (FR-012) and recorded in [plan.md](../plan.md) Constitution Check? [Constitution §I, Spec §FR-012, plan.md]
- [ ] CHK063 - Is Principle II (Integration-Test-First) satisfied — every behavior change (FR-001…FR-017) has an explicit MSSQL integration test obligation? [Constitution §II, Spec §FR-015, plan.md]
- [ ] CHK064 - Is Principle III (REST + GraphQL Parity) satisfied — every user story (US1…US9) covers BOTH REST and GraphQL (with MCP coverage where applicable for US1 and FR-017)? [Constitution §III, Spec §User Scenarios, plan.md]
- [ ] CHK065 - Is Principle IV (Config Schema Discipline) satisfied — confirmed no `schemas/dab.draft.schema.json` changes per [research.md](../research.md) §R11 and [plan.md](../plan.md) Constitution Check? [Constitution §IV, research.md §R11, plan.md]
- [ ] CHK066 - Is Principle V (No Secrets) satisfied — does [quickstart.md](../quickstart.md) use `@env('VAR_NAME')`-style references for connection strings (no literal passwords beyond the `<your-test-password>` placeholder)? [Constitution §V, quickstart.md, plan.md]
- [ ] CHK067 - Is Principle VI (Formatting & Style) satisfied — does [plan.md](../plan.md) name `dotnet format … --verify-no-changes` as a gate, and does [research.md](../research.md) §R10 confirm the diff is small enough to not perturb formatting? [Constitution §VI, plan.md, research.md §R10]
- [ ] CHK068 - Is Principle VII (Minimal-Surface Changes) satisfied — does [research.md](../research.md) bound production-code delta (~5 line edits across ≤6 files) and reject parallel pipelines (R1 alternatives, R5 CreateRecord refactor)? [Constitution §VII, research.md §R1, §R5, plan.md]

---

## 2026-06-09 Supersession Update

The 2026-06-09 Clarifications session in [spec.md](../spec.md) changed
the design so that DAB treats a SQL Server `JSON` column exactly like
a string column. This update reclassifies the checklist items that no
longer apply, and revises the items whose target text has moved.

**Obsolete — n/a after 2026-06-09**

- CHK020 — FR-004 no longer specifies a DAB-side rejection signal for a nested-object REST input; rejection is delegated to the REST deserializer for a string-typed property. No DAB-specific assertion is required.
- CHK021 — FR-005 no longer specifies a DAB-side rejection for nested-object GraphQL input; rejection is delegated to Hot Chocolate's built-in `String` input validation.
- CHK033 — FR-017 no longer specifies a description SUBSTANCE; the description is intentionally absent.
- CHK042 — Still valid in spirit: the `CreateRecordTool` / `UpdateRecordTool` input schemas remain unchanged. But [contracts/mcp-tools.md](../contracts/mcp-tools.md) was rewritten to drop ALL MCP-side JSON-specific annotation, so the "no per-column slot" non-goal is now a corollary of the broader "no annotation anywhere" rule.
- CHK043, CHK044 — FR-009 no longer maintains an operator allow-list. Every operator is forwarded to SQL Server; SQL Server is authoritative. The new contract tables in [contracts/rest-openapi.md](../contracts/rest-openapi.md) and [contracts/graphql.md](../contracts/graphql.md) document pass-through behavior.
- CHK046, CHK047, CHK048 — The canonical MCP description string is deleted. There is no per-field or per-parameter annotation in MCP. The negative-assertion test obligation replaces the earlier presence-assertion obligation.

**Revised — items still apply, with new wording / targets**

- CHK022 — FR-006 ("no DAB-side JSON-schema validation") now follows naturally from the broader 2026-06-09 rule that DAB has no JSON-specific code path. Covered by the type-mapping unit test (Phase 3 of [tasks.md](../tasks.md)) and the integration tests in Phase 5.
- CHK023 — FR-007 still cites `400` (REST) and `BAD_REQUEST` (GraphQL). NEW obligation: the response body MUST include the SQL Server error number that was raised. The contracts have been updated to reflect this; CHK023 should be re-verified against the new contract text.
- CHK037 — Earlier guidance "raw SQL error MUST NOT be leaked in production" is **superseded**. After 2026-06-09 the SQL Server error number is INCLUDED in the response body to support diagnosis. CHK037 should be marked n/a or rewritten to reflect the new contract.
- CHK045 — Error-code set in [research.md §R4](../research.md) (13608–13614) now carries the FULL JSON-column error contract — both malformed writes AND filter operators SQL Server cannot evaluate. The "verify exact set at implementation" caveat remains and is reinforced in T006 / T020 / T024 of [tasks.md](../tasks.md).
- CHK052 — Spec.md Clarifications Q1 (strict string write) is itself superseded by the 2026-06-09 session. CHK052 should now compare FR-004 / FR-005 against the 2026-06-09 Q&A block, not the 2026-06-04 block.
- CHK068 — Production-code delta is now **smaller** than the original ~5-line / ≤6-file estimate. The current bound is 2 production edits across 2 files (`TypeHelper.cs`, `MsSqlDbExceptionParser.cs`) plus the optional error-envelope serializer touch flagged in R4.

