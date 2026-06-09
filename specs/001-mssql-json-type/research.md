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

## R3 — Filter operator allow-list (SUPERSEDED 2026-06-09)

**Status**: **SUPERSEDED** by the 2026-06-09 Clarifications session in
[spec.md](./spec.md). DAB no longer maintains a JSON-specific operator
allow-list. Every `$filter` operator the client submits is forwarded to
SQL Server exactly as for any other string-typed column; SQL Server is
the authority on which operators are supported on the native JSON
type. Operators SQL Server does not support produce a SQL Server error
that DAB surfaces via R4 / FR-007.

**Original decision retained below for audit only — NOT IMPLEMENTED.**

---

### Original decision (not implemented)

Add an operator allow-list check in
[src/Core/Parsers/ODataASTVisitor.cs](src/Core/Parsers/ODataASTVisitor.cs#L35)
inside `Visit(BinaryOperatorNode)`, immediately after the existing
`PopulateDbTypeForProperty(nodeIn)` call. When the `SqlDbType` is
`SqlDbType.Json` and the `BinaryOperatorKind` is not `Equal` or
`NotEqual`, throw `DataApiBuilderException` with
`HttpStatusCode.BadRequest`.

**Why superseded**: PM directive 2026-06-09 — "We allow ALL filter
operations against a JSON field. ALL of them. Then we let SQL fail if
they are not supported. This is IMPORTANT because if SQL adds support
for some operator, we do not need to change DAB." Forward-compatibility
with future SQL Server releases is the primary motivation; the design
also eliminates the maintenance burden of tracking SQL Server's
operator-support matrix in DAB.

**Affected files (no longer touched)**: `src/Core/Parsers/ODataASTVisitor.cs`.

---
---

## R4 — Error mapping for SQL Server JSON validation failure

**Updated 2026-06-09**: This research item now carries the full weight
of the JSON-column error contract because R3 (operator allow-list) was
superseded. The same `BadRequestExceptionCodes` set extension now
produces the 400 response for BOTH (a) malformed-JSON writes and (b)
filter / order-by operators that SQL Server does not support on the
JSON type. Per FR-007, the response body MUST include the SQL Server
error number.

**Decision**: Append SQL Server JSON error numbers (currently
`{13608, 13609, 13610, 13611, 13612, 13613, 13614}`; final set
verified during implementation against the target SQL Server 2025
build) to `MsSqlDbExceptionParser.BadRequestExceptionCodes`. Verify
that the existing error-envelope serialization includes the SQL
Server error number; if it does not, extend the envelope (smallest
possible change). Reuse the existing `GetMessage(DbException)`
developer-mode / production-mode path; do not introduce a new
JSON-specific message template.

**Rationale**:

- `MsSqlDbExceptionParser.GetHttpStatusCodeForException`
  ([src/Core/Resolvers/MsSqlDbExceptionParser.cs](src/Core/Resolvers/MsSqlDbExceptionParser.cs#L67))
  already maps known error numbers to `HttpStatusCode.BadRequest` via
  a hash set. Extending that set is the established DAB pattern.
- The same code numbers cover SQL Server's JSON validation errors
  raised by `OPENJSON`, `JSON_VALUE`, `JSON_QUERY`, and JSON-column
  insert/update validation. Whether the trigger is a malformed-JSON
  write or an unsupported operator on the column, the resulting error
  flows through the same path.
- The GraphQL surface picks up the 400 mapping automatically via the
  existing `DataApiBuilderException` → GraphQL error translation,
  resulting in `extensions.code = "BAD_REQUEST"` (FR-007).
- `GetMessage(DbException)` already gates whether the raw message
  surfaces by `_developerMode`. FR-007's requirement to include the
  SQL Server error number applies in both modes — the number itself
  is not sensitive; only the raw message text is.

**Alternatives considered**:

- **Catch a specific exception type for JSON errors** — SqlClient does
  not expose a distinct exception subclass; pattern-matching on
  `SqlException.Number` is the established approach.
- **Translate to a custom DAB-specific message identifying the field by
  name** — requires plumbing the offending field name back through
  the exception. Out of scope; the SQL error number plus the standard
  error envelope is sufficient per the 2026-06-09 directive ("treat
  it like a string column. Nothing special.").

**Affected files**:

- [src/Core/Resolvers/MsSqlDbExceptionParser.cs](src/Core/Resolvers/MsSqlDbExceptionParser.cs#L20)
  — extend `BadRequestExceptionCodes` by 7 numbers.
- Possibly the error-envelope serializer, if it does not already emit
  the SQL Server error number. Verify during implementation.

---

## R5 — MCP surface: JSON column annotation (SUPERSEDED 2026-06-09)

**Status**: **SUPERSEDED** by the 2026-06-09 Clarifications session in
[spec.md](./spec.md). A JSON column appears in all MCP tool schemas
(`DescribeEntitiesTool`, `DynamicCustomTool`, `CreateRecordTool`,
`UpdateRecordTool`) exactly as a plain string column does. No
JSON-specific `description`, `format`, or other annotation is added.
DAB does not edit any MCP tool source file for this feature.

**Why superseded**: PM directive 2026-06-09 — "We treat it like a string
column. Every time." The MCP annotation introduced JSON-specific
behavior that conflicted with the principle. Integration tests instead
assert the column appears in MCP output with **no** JSON-specific
annotation, guarding against accidental future divergence.

**Original decision retained below for audit only — NOT IMPLEMENTED.**

---

### Original decision (not implemented)

Per FR-017 (original 2026-06-04 wording), surface the JSON-column hint
in two places:

1. **`DescribeEntitiesTool` output** — attach
   `"description": "JSON-encoded string; embed valid JSON text (e.g.,
   a JSON object or array serialized as a string). Do not send a
   nested object or array."` to fields whose underlying
   `columnDefinition.SqlDbType == SqlDbType.Json`.
2. **`DynamicCustomTool.BuildInputSchemaFromDbMetadata`** — same
   description, attached at the per-parameter slot when the
   parameter's `SqlDbType` is JSON.

**Affected files (no longer touched)**:
`src/Azure.DataApiBuilder.Mcp/BuiltInTools/DescribeEntitiesTool.cs`,
`src/Azure.DataApiBuilder.Mcp/Core/DynamicCustomTool.cs`.

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
