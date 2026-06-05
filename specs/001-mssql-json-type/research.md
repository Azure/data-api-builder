# Phase 0 Research: MSSQL JSON Data Type Support

**Feature**: 001-mssql-json-type
**Date**: 2026-06-04
**Status**: Complete — all NEEDS CLARIFICATION resolved

This document records the open implementation questions raised by the spec
and the verified answers from the codebase. Each entry follows the
**Decision / Rationale / Alternatives** triplet.

---

## R1 — Microsoft.Data.SqlClient: does `SqlDbType.Json` exist on our pinned version?

**Decision**: `Microsoft.Data.SqlClient >= 6.0.0` is a **prerequisite**
for this feature and is delivered by a **separate dependency PR**
(out of scope for this feature's `tasks.md`). Once that PR is merged
to the same target branch, this feature wires SQL Server `JSON`
columns through the existing `SqlDbType` → CLR-type → DAB string path
by adding a single entry to `TypeHelper._sqlDbTypeToType`:
`[SqlDbType.Json] = typeof(string)`. A pre-flight task in this
feature's `tasks.md` verifies the dependency is in place and fails
fast (referencing the prerequisite PR) if not.

**Rationale**:

- `src/Directory.Packages.props` pins `Microsoft.Data.SqlClient` at
  `5.2.3` ([src/Directory.Packages.props](src/Directory.Packages.props#L46)).
  `SqlDbType.Json` is a new enum value added in the SqlClient `6.x` line
  alongside the SQL Server 2025 / Azure SQL DB native JSON type.
- The downstream `TypeHelper.GetSystemTypeFromSqlDbType` parses incoming
  column-type strings via `Enum.TryParse<SqlDbType>` and then dictionary-
  looks up the CLR type ([src/Core/Services/TypeHelper.cs](src/Core/Services/TypeHelper.cs#L267)).
  With the dep bump, `"json"` parses cleanly to `SqlDbType.Json`; one
  dictionary entry is the only line of code required to surface JSON
  columns as `typeof(string)` end-to-end.
- After that, the existing `string` path in `MsSqlMetadataProvider`,
  `MsSqlQueryBuilder`, parameter binding, `SchemaConverter`, and
  `OpenApiDocumentor` handles JSON without further plumbing (Principle VII:
  Minimal-Surface Changes).
- The SqlClient bump is a small, behavior-preserving package upgrade
  whose existing test suites (PostgreSQL etc.) remain unaffected because
  no PostgreSQL/MySQL/Cosmos engine code consumes this package.

**Alternatives considered**:

- **No dep bump; add a string-name fallback in `GetSystemTypeFromSqlDbType`**
  (`if (baseType.Equals("json", OrdinalIgnoreCase)) return typeof(string)`)
  and bind writes as `SqlDbType.NVarChar(max)`. SQL Server implicitly
  converts `nvarchar(max)` → `json` and runs JSON validation as part of
  the conversion, so functional behavior matches. **Rejected** because
  it forks the type-resolution path (string-name override instead of enum
  lookup), which is exactly the kind of single-use parallel pipeline
  Principle VII prohibits. Keeping the resolution path enum-based keeps
  parity with the rest of the type table.
- **Map JSON to `SqlDbType.NVarChar` at parameter-binding time even after
  the dep bump.** Functionally equivalent; loses the documentary value of
  `SqlDbType.Json` and complicates a hypothetical future "real JSON
  parameter binding" feature. Rejected for symmetry.

**Affected files** (within the scope of this feature):

- [src/Core/Services/TypeHelper.cs](src/Core/Services/TypeHelper.cs#L88) —
  add `[SqlDbType.Json] = typeof(string)` to `_sqlDbTypeToType`. **One
  line.**

**Files NOT touched by this feature** (delivered by the prerequisite
dependency PR, not by `tasks.md`):

- `src/Directory.Packages.props` — Microsoft.Data.SqlClient `5.2.3 → 6.x`
  version bump (and license-URL comment refresh).
- `external_licenses/` — refreshed SqlClient SNI license file.
- `scripts/notice-generation.ps1` — license URL refresh and NOTICE
  regeneration.

A pre-flight task in this feature's `tasks.md` MUST assert the
prerequisite (`Microsoft.Data.SqlClient >= 6.0.0`) and fail with a
clear message pointing at the dependency PR if absent.

---

## R2 — Does the rest of the pipeline already handle `typeof(string)` columns end-to-end?

**Decision**: Yes. Confirmed for all four surfaces (REST, GraphQL,
OpenAPI, MCP). No additional code beyond R1 is required for read/write
of JSON values along the happy path.

**Rationale**:

- **OpenAPI**: `OpenApiDocumentor` resolves column type via
  `TypeHelper.GetJsonDataTypeFromSystemType(columnDef.SystemType)`
  ([src/Core/Services/OpenAPI/OpenApiDocumentor.cs](src/Core/Services/OpenAPI/OpenApiDocumentor.cs#L1067)),
  and `_systemTypeToJsonDataTypeMap` already maps `typeof(string)` →
  `JsonDataType.String`
  ([src/Core/Services/TypeHelper.cs](src/Core/Services/TypeHelper.cs#L71)).
  No `format` value is attached for strings, satisfying the "no `format`"
  requirement.
- **GraphQL SDL**: `SchemaConverter.GetGraphQLTypeFromSystemType`
  switches on `type.Name` and maps `"String"` → `SupportedHotChocolateTypes.STRING_TYPE`
  ([src/Service.GraphQLBuilder/Sql/SchemaConverter.cs](src/Service.GraphQLBuilder/Sql/SchemaConverter.cs#L542)).
  Both output fields and Create/Update input fields go through the same
  helper, so reuse of the existing `String` scalar is automatic. No new
  scalar — FR-013 honored.
- **Metadata provider**: `MsSqlMetadataProvider.SqlToCLRType` is the only
  caller of `GetSystemTypeFromSqlDbType`
  ([src/Core/Services/MetadataProviders/MsSqlMetadataProvider.cs](src/Core/Services/MetadataProviders/MsSqlMetadataProvider.cs#L58));
  fixing the dictionary in R1 fixes this path.
- **EDM model**: `TypeHelper.GetEdmPrimitiveTypeFromSystemType` already
  handles `"String"` → `EdmPrimitiveTypeKind.String`
  ([src/Core/Services/TypeHelper.cs](src/Core/Services/TypeHelper.cs#L137));
  OData filter parsing therefore sees JSON columns as `Edm.String`.
- **Resolvers** (`SqlQueryEngine`, `SqlMutationEngine`,
  `MsSqlQueryBuilder`): consume `columnDefinition.SystemType` /
  `columnDefinition.DbType` opaquely. No column-type whitelist exists
  that would gate strings.

**Alternatives considered**: None — observed code paths are unconditional
for `string`-typed columns.

---

## R3 — Filter operator allow-list: where does the gate live and how is it enforced?

**Decision**: Add an operator allow-list check in
[src/Core/Parsers/ODataASTVisitor.cs](src/Core/Parsers/ODataASTVisitor.cs#L35)
inside `Visit(BinaryOperatorNode)`, immediately after the existing
`PopulateDbTypeForProperty(nodeIn)` call (the point at which the
visitor already knows the underlying `SqlDbType` of the property side).
When that `SqlDbType` is `SqlDbType.Json` and the `BinaryOperatorKind`
is not `Equal` or `NotEqual` (the only two we allow, with null checks
naturally falling out of `Equal`/`NotEqual` against `NULL`), throw
`DataApiBuilderException` with `HttpStatusCode.BadRequest` and
sub-status `BadRequest`. Throwing from the visitor aborts query-string
parsing before any SQL is generated and surfaces through the existing
REST and GraphQL error pipelines.

**Rationale**:

- The visitor *already* extracts `SqlDbType` for the column at this exact
  point ([ODataASTVisitor.cs](src/Core/Parsers/ODataASTVisitor.cs#L273)),
  so the check is a few lines with zero new wiring.
- Throwing here covers REST `$filter` and GraphQL `filter` arguments
  uniformly — both surfaces parse `$filter` through this visitor.
- The `IsSimpleBinaryExpression` guard ensures we only inspect simple
  `field op constant` (and `constant op field`) expressions; complex
  nested expressions degrade to no-check, which is acceptable because
  every operator branch will recursively visit until it hits a simple
  expression. **Refinement**: We must lift the check above the
  `IsSimpleBinaryExpression` gate (i.e., check before recursion descent
  uses the parameter), OR perform the check at every BinaryOperatorNode
  whose operand is a SingleValuePropertyAccessNode referencing a JSON
  column. The cleanest place is inside `PopulateDbTypeForProperty`
  itself — after it sets `SqlDbType`, validate the operator against the
  type.
- `NullValue` handling: `metadata eq null` and `metadata ne null` flow
  through `CreateNullResult`
  ([ODataASTVisitor.cs](src/Core/Parsers/ODataASTVisitor.cs#L190)) and
  are emitted as `IS NULL` / `IS NOT NULL`. These already use only
  `Equal` / `NotEqual` operators, so they are naturally allowed.

**Alternatives considered**:

- **Reject at SQL generation time** (in `MsSqlQueryBuilder` or similar)
  — gates would scatter across multiple query builders and need
  duplication for joins/projections. Rejected.
- **Reject in a new validation pass before the visitor** — duplicates the
  metadata lookup the visitor already performs. Rejected.
- **Allow all operators and let SQL Server fail** — produces 500-class
  errors when SQL Server can't compare `JSON > something`, violating
  FR-007's "no 5xx" guarantee and SC-004. Rejected.

**Affected files**:

- [src/Core/Parsers/ODataASTVisitor.cs](src/Core/Parsers/ODataASTVisitor.cs)
  — add JSON-operator gate inside `PopulateDbTypeForProperty` (or a
  helper called from it).

---

## R4 — Error mapping for SQL Server JSON validation failure

**Decision**: Add SQL Server error numbers `13608`, `13609`, `13614`
(JSON syntax / JSON value / JSON content errors) to
`MsSqlDbExceptionParser.BadRequestExceptionCodes`. Sanitize the message
via the existing `GetMessage(DbException)` developer-mode/production-mode
path so the raw SQL message is not leaked in production.

**Rationale**:

- `MsSqlDbExceptionParser.GetHttpStatusCodeForException`
  ([src/Core/Resolvers/MsSqlDbExceptionParser.cs](src/Core/Resolvers/MsSqlDbExceptionParser.cs#L67))
  already maps known error numbers to `HttpStatusCode.BadRequest` via a
  hash set; we extend the set rather than introducing new code paths.
- The MSSQL error numbers for JSON validation are in the **13600 range**
  (`Msg 13608: "JSON text is not properly formatted."`, `Msg 13609`,
  `Msg 13614`). The exact set MUST be re-verified against the target
  SQL Server 2025 build during implementation (Phase 2 task). For
  planning purposes, the set is enumerated as
  `{13608, 13609, 13610, 13611, 13612, 13613, 13614}` and pruned by
  test.
- The GraphQL surface picks up the 400 mapping automatically via the
  existing `DataApiBuilderException` → GraphQL error translation,
  resulting in extension `code: "BAD_REQUEST"` (FR-007).
- `GetMessage(DbException)` already gates whether the raw message
  surfaces by `_developerMode`, satisfying "MUST NOT leak the raw SQL
  message" in production.

**Alternatives considered**:

- **Catch a specific exception type for JSON errors** — SqlClient does
  not expose a distinct exception subclass for JSON validation; we'd be
  pattern-matching on `SqlException.Number` either way. Adding numbers
  to the existing set is the established pattern.
- **Translate to a custom DAB-specific message identifying the field by
  name** — requires plumbing the offending field/value back through the
  exception, which is non-trivial. Defer to a follow-up; the SQL
  message in developer mode already names the column, and production
  mode returns a generic 400 with the standard DAB error envelope.

**Affected files**:

- [src/Core/Resolvers/MsSqlDbExceptionParser.cs](src/Core/Resolvers/MsSqlDbExceptionParser.cs#L20)
  — extend `BadRequestExceptionCodes`.

---

## R5 — MCP surface: where does the JSON-column `description` hint live?

**Decision**: Per FR-017, surface the JSON-column hint in two places:

1. **`DescribeEntitiesTool` output** — when emitting per-entity field
   metadata, attach `"description": "JSON-encoded string; embed valid
   JSON text (e.g., a JSON object or array serialized as a string). Do
   not send a nested object or array."` to fields whose underlying
   `columnDefinition.SqlDbType == SqlDbType.Json`.
2. **`DynamicCustomTool.BuildInputSchemaFromDbMetadata`** — same
   description, attached at the per-parameter slot when
   `paramDef.SystemType == typeof(string)` and the parameter's
   `SqlDbType` is JSON
   ([src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs](src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs#L325)).

**Re-scoping note for FR-017** (record in plan, no spec change required):
`CreateRecordTool` and `UpdateRecordTool` emit a coarse fixed input
schema with `data: { type: "object" }` — they do NOT enumerate per-column
slots ([src/Azure.DataApiBuilder.Mcp/BuiltInTools/CreateRecordTool.cs](src/Azure.DataApiBuilder.Mcp/BuiltInTools/CreateRecordTool.cs#L36)).
Per-column descriptions cannot be attached to their input schemas
without a refactor that violates Principle VII. The agent contract is
already that `describe_entities` MUST be called first (the tool
descriptions on both CreateRecord and UpdateRecord explicitly say
"STEP 1: describe_entities -> ... STEP 2: call this tool"); annotating
the column metadata in `describe_entities` therefore reaches the agent
before it constructs the `data` payload, achieving the same correctness
outcome FR-017 requires.

**Rationale**: Single point of truth for column annotations
(`describe_entities`), plus per-parameter slots for SP-backed dynamic
tools where the input schema is column-aware. Touches exactly two files.

**Alternatives considered**:

- **Refactor `CreateRecordTool` / `UpdateRecordTool` to emit per-column
  input schemas.** Substantial change to MCP tool shape and would
  require parallel work on every MCP client. Rejected; out of scope.
- **Add `format: "json"`.** Spec Q5 chose **B** (description only); the
  `format` value is non-standard. Rejected.

**Affected files**:

- [src/Azure.DataApiBuilder.Mcp/BuiltInTools/DescribeEntitiesTool.cs](src/Azure.DataApiBuilder.Mcp/BuiltInTools/DescribeEntitiesTool.cs)
  — when serializing field metadata, attach `description` for JSON
  columns. Locate the field-emission code (search for `fields` /
  `columnDefinition` in this file during implementation).
- [src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs](src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs#L368)
  — extend `BuildInputSchemaFromDbMetadata` to attach the description
  when the parameter's `SqlDbType` is JSON.

---

## R6 — Test fixture placement and schema discipline

**Decision**: Add a single `Profiles` table to
[src/Service.Tests/DatabaseSchema-MsSql.sql](src/Service.Tests/DatabaseSchema-MsSql.sql)
with a `JSON NULL` column. Add a corresponding `Profile` entity entry
to [src/Service.Tests/dab-config.MsSql.json](src/Service.Tests/dab-config.MsSql.json)
exposing it via REST and GraphQL. No other engine schemas
(`DatabaseSchema-PostgreSql.sql`, `DatabaseSchema-MySql.sql`,
`DatabaseSchema-DwSql.sql`) are touched.

**Rationale**: Constitution Principle I (Multi-Engine Parity) and FR-012
require explicit MSSQL-only scope. Schema additions are confined to the
MSSQL fixture; other engine fixtures stay byte-identical so their CI
runs are guaranteed unaffected. Seed rows must cover edge cases
enumerated in the spec: simple object, nested object, array, Unicode
string content, and `NULL`.

**Schema sketch** (formatted per copilot-instructions.md MSSQL rules,
poorsql-style):

```sql
DROP TABLE IF EXISTS profiles;
CREATE TABLE profiles
(
    id INT IDENTITY(1, 1) PRIMARY KEY,
    metadata JSON NULL
);

INSERT INTO profiles (metadata)
VALUES
    ('{"role":"admin","tier":3}'),
    ('{"tags":["a","b","c"]}'),
    ('{"nested":{"key":{"deep":true}}}'),
    ('{"unicode":"\u00e9\u00fc\ud83d\ude00"}'),
    (NULL);
```

(Drop clause placement and ordering within
`DatabaseSchema-MsSql.sql` are decided by implementation per existing
file conventions.)

**Alternatives considered**: Reuse an existing table by adding a JSON
column. Rejected because every existing table is part of multiple test
fixtures with tightly-coupled row counts and column orderings; adding a
nullable column would still risk subtle drift in serialization-order
tests. A dedicated `profiles` table isolates the new behavior.

---

## R7 — DAB does NOT need an SQL Server build-version probe

**Decision**: Per spec Clarification Q4 and FR-016, ship no version
probe. SQL Server returns its native error (e.g., "Column, parameter,
or variable #1: Cannot find data type JSON") at schema-load or query
time. DAB surfaces it through existing pipelines (startup
schema-resolution failure or a runtime DB exception).

**Rationale**: Captured in spec. Implementation note: document the
minimum supported server versions (Azure SQL DB current; SQL Server
2025+) in the feature's README/docs entry once implementation lands.

**Affected files**: None at code level; documentation file to be added
under `docs/` if appropriate (deferred to implementation phase).

---

## R8 — `$orderby` on JSON columns

**Decision**: No special-case code. The existing `$orderby` pipeline
already accepts string columns and emits `ORDER BY <col>` — which on a
JSON column is a string-order sort because SQL Server's JSON type
supports `ORDER BY` and compares as its underlying text representation.
The "documented as string-order, not semantic-JSON order" requirement is
met by documentation only (no rejection, no normalization).

**Rationale**: Spec User Story 9 explicitly permits orderby and pins it
as string-order. Existing behavior matches without code change.

**Alternatives considered**: Reject orderby on JSON columns. Rejected
by the spec.

---

## R9 — Aggregation / GroupBy on JSON columns (out-of-spec confirmation)

**Decision**: Out of scope for this feature; existing behavior is
preserved. JSON columns are not in `SupportedAggregateTypes.NumericAggregateTypes`
([src/Service.GraphQLBuilder/GraphQLTypes/SupportedTypes.cs](src/Service.GraphQLBuilder/GraphQLTypes/SupportedTypes.cs#L55))
and string-typed columns already fall outside numeric aggregations, so
`sum`/`avg`/etc. against JSON are already rejected by existing logic.
`count` / `groupby` against JSON columns will pass through to SQL
Server, which may succeed or fail per its own rules — explicit
rejection of `groupby` on JSON is recorded as **NOT in scope** and
**NOT a regression**.

**Rationale**: Spec Q10 was rated low-impact; no spec requirement
mandates DAB-side rejection. The existing engine behavior is acceptable.

---

## R10 — Validation tooling guarantees

- `dotnet format src/Azure.DataApiBuilder.sln --verify-no-changes`
  required (Constitution Principle VI). The minimal diffs proposed
  (~5–10 added lines across 5 files) will not perturb formatting if
  authored to existing patterns.
- `dab validate` against
  [src/Service.Tests/dab-config.MsSql.json](src/Service.Tests/dab-config.MsSql.json)
  must pass; since no `schemas/dab.draft.schema.json` changes are
  needed (R11 below), this is automatic.

---

## R11 — `schemas/dab.draft.schema.json` impact

**Decision**: No change required. JSON support is purely runtime
type-mapping; the runtime config shape is unchanged. Entity column
declarations in config already accept any database type opaquely.

**Rationale**: Confirms spec FR-014 (no new CLI flags) and the
"explicitly out of scope: dab-config / CLI changes" clause.

---

## Summary: total touchpoint count

**This feature's scope:**

| File | Lines changed (estimate) |
|------|--------------------------|
| `src/Core/Services/TypeHelper.cs` | 1 (dictionary entry) |
| `src/Core/Resolvers/MsSqlDbExceptionParser.cs` | 1–7 (add JSON error numbers) |
| `src/Core/Parsers/ODataASTVisitor.cs` | ~10 (operator gate) |
| `src/Azure.DataApiBuilder.Mcp/BuiltInTools/DescribeEntitiesTool.cs` | ~5 (description hint) |
| `src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs` | ~5 (description hint) |
| `src/Service.Tests/DatabaseSchema-MsSql.sql` | ~12 (table + seed) |
| `src/Service.Tests/dab-config.MsSql.json` | ~15 (Profile entity) |
| `src/Service.Tests/...` integration tests (new) | several hundred (test code) |

Production code delta is small and surgical, in keeping with
Constitution Principle VII.

**Delivered by the prerequisite dependency PR (NOT in this feature):**

| File | Lines changed (estimate) |
|------|--------------------------|
| `src/Directory.Packages.props` | 1 (Microsoft.Data.SqlClient 5.2.3 → 6.x) |
| `external_licenses/Microsoft.Data.SqlClient.SNI.*.License.txt` | refresh |
| `scripts/notice-generation.ps1` | 1 (license URL) + regenerate NOTICE |
