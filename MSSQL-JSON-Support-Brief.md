# MSSQL JSON Data Type Support — Feature Brief

**Audience**: Product Management and Engineering
**Issue**: [Azure/data-api-builder#2768](https://github.com/Azure/data-api-builder/issues/2768)
**Feature branch**: `Usr/sogh/speckit-jsontypesupport`
**Status**: Specification and plan approved (revised 2026-06-09); implementation pending one prerequisite PR
**Scope**: Backend engine change, SQL Server only. No host-specific or platform-specific work.

> **Revision 2026-06-09 — design simplification.** A Product Management
> clarifications session collapsed earlier design decisions to a
> single principle: *DAB treats a SQL Server `JSON` column exactly
> like a string column — no JSON-specific behavior anywhere*. Three
> earlier design elements were dropped: (1) the DAB-side `$filter`
> operator allow-list; every operator is now forwarded to SQL Server
> as the authority; (2) the MCP `description` annotation for JSON
> columns; (3) the DAB-side nested-object rejection path — it is now
> covered naturally by the REST deserializer / GraphQL input
> validator for a string-typed property. The production-code delta
> shrinks to **two file edits** (one dictionary entry plus one
> error-code list extension); the test-side surface remains broadly
> the same. See [`specs/001-mssql-json-type/spec.md`](./specs/001-mssql-json-type/spec.md)
> "Session 2026-06-09" Clarifications for the audit trail.

This document is the single source of truth summary for the feature.
Detailed specifications, requirements, tasks, and contracts live under
[`specs/001-mssql-json-type/`](./specs/001-mssql-json-type/) and are
indexed in the Reference Map (Section 8).

---

## 1. Problem and design choice

### Problem

SQL Server 2025 and Azure SQL Database introduced a native `JSON`
column type, distinct from storing JSON text in an `nvarchar(max)`
column. Today, when a customer exposes a table that uses this new
type through Data API builder (DAB), schema load either fails or the
column is silently dropped from the generated REST, GraphQL, and MCP
surfaces. This blocks the customer scenario reported in the GitHub
issue above.

### Design choice

DAB will treat a native `JSON` column as a JSON-encoded **string** on
the wire, in both read and write directions. SQL Server stores and
validates the JSON; DAB does not parse, reformat, expand, validate,
or pre-filter the value, and DAB does not maintain any
JSON-specific knowledge of which SQL operators are supported.

### Before and after

For the following SQL Server table:

```sql
CREATE TABLE dbo.profiles (
    id       INT IDENTITY(1,1) PRIMARY KEY,
    metadata JSON NULL
);
INSERT INTO dbo.profiles (metadata) VALUES (N'{"role":"admin","tier":3}');
```

| Surface | Today | With this feature |
|---|---|---|
| `GET /api/profile/id/1` | Schema-load error or column missing from response | `{ "value": [ { "id": 1, "metadata": "{\"role\":\"admin\",\"tier\":3}" } ] }` |
| GraphQL `profile_by_pk(id:1)` | Field absent from SDL | Returns `metadata: String` with the JSON text verbatim |
| MCP `describe_entities` | Entity missing the field | Field surfaced as a plain string slot, identical in shape to an `nvarchar(max)` column |

### Rationale for string passthrough over a new JSON type

- **Zero client-side migration.** Existing DAB clients that already
  handle JSON stored in `nvarchar(max)` treat the value as a string.
  Switching to the native type changes nothing for those clients.
- **No new schema surface.** No new GraphQL scalar, no OpenAPI
  extension, no MCP annotation, no DAB-specific schema additions.
  Swagger UI, GraphQL code generators, MCP clients, and Hot
  Chocolate input validation continue to work unchanged.
- **Forward compatibility with future SQL Server releases.** Because
  DAB does not maintain a JSON-specific operator support matrix, any
  filter / order-by / comparison operator SQL Server adds in a future
  release becomes available to DAB customers automatically with no
  DAB code change.
- **Minimal DAB change.** Approximately **two line-level edits**
  across two production files, plus tests. The change reuses DAB's
  existing `nvarchar(max)` / `typeof(string)` pipeline end-to-end.

---

## 2. User-visible behavior

### 2.1 REST (`/api/...`)

**Read.** The column is returned as an escaped JSON string. DAB does
not parse it into a nested object.

```http
GET /api/profile/id/1
200 OK
{
  "value": [
    { "id": 1, "metadata": "{\"role\":\"admin\",\"tier\":3}" }
  ]
}
```

**Write.** The request body must send the value as a string
containing valid JSON text. A non-string token (nested object,
array, number, boolean) is rejected with HTTP 400 by the existing
REST deserializer for a string-typed property; no DAB-specific check
is added.

```http
POST /api/profile
{ "metadata": "{\"role\":\"guest\"}" }      -> 201 Created     (accepted)

POST /api/profile
{ "metadata": { "role": "guest" } }         -> 400 Bad Request (rejected by deserializer)
```

**Malformed JSON text.** If the supplied string is not valid JSON,
SQL Server raises a JSON-validation error (currently in the
13608–13614 range). DAB's existing `MsSqlDbExceptionParser` is
extended to map those error numbers to HTTP 400. The response body
includes the SQL Server error number so the customer can diagnose
the cause.

**Filter.** DAB does not maintain a JSON-specific operator
allow-list. Every `$filter` operator is forwarded to SQL Server.
SQL Server is the authority on which operators succeed against the
native JSON type; the table below records the current state.

| Operator | DAB behavior | Current SQL Server outcome |
|---|---|---|
| `eq`, `ne` | Forwarded | Succeeds: string compare on stored JSON text |
| `eq null`, `ne null` | Forwarded | Succeeds: `IS NULL` / `IS NOT NULL` |
| `contains`, `startswith`, `endswith` | Forwarded | SQL Server raises a JSON error → DAB returns 400 with the SQL error number |
| `gt`, `lt`, `ge`, `le` | Forwarded | SQL Server raises a JSON error → DAB returns 400 with the SQL error number |

If SQL Server adds operator support in a future release, DAB
customers pick it up automatically with no DAB code change.

**Order-by.** `?$orderby=metadata` is forwarded to SQL Server. It
currently succeeds with string-order semantics.

### 2.2 GraphQL (`/graphql`)

The column appears as a nullable `String` on the entity type and in
both `create` and `update` input types. No new scalar is added.

```graphql
type Profile {
  id: Int!
  metadata: String
}

input CreateProfileInput { metadata: String }
input UpdateProfileInput { metadata: String }
```

**Write.** A mutation that supplies the value as a string succeeds.
An object literal is rejected by Hot Chocolate's built-in `String`
input validation before DAB sees the request; no DAB-specific check
is added.

```graphql
mutation { createProfile(item: { metadata: "{\"role\":\"guest\"}" }) { id } }   # accepted
mutation { createProfile(item: { metadata: { role: "guest" } }) { id } }       # rejected by Hot Chocolate
```

**Malformed JSON.** Same as REST: SQL Server's JSON-validation error
number is surfaced through the existing GraphQL error envelope with
`extensions.code = "BAD_REQUEST"`. The error message contains the
SQL Server error number.

**Filter.** Same as REST: every `StringFilterInput` operator is
forwarded to SQL Server. The current pass / fail split mirrors the
REST table in Section 2.1.

### 2.3 MCP (`/mcp`)

MCP is DAB's third API surface, used by LLM agents (for example,
GitHub Copilot). After the 2026-06-09 design simplification, JSON
columns appear in every MCP tool schema **indistinguishably from
plain string columns**. No `description`, no `format`, no `x-dab-*`
extension is added.

| MCP tool | Change |
|---|---|
| `DescribeEntitiesTool` | **No change.** A JSON column's field metadata is identical in shape to a `nvarchar(max)` column. |
| `DynamicCustomTool` | **No change.** A JSON stored-procedure parameter's input schema is identical in shape to a `string`-typed parameter. |
| `CreateRecordTool`, `UpdateRecordTool` | **No change.** Coarse `data: { type: "object" }` schema unchanged. |

The rationale is the same forward-compatibility principle that drove
the filter pass-through change. By not encoding JSON-specific
behavior into MCP schemas, DAB does not need to revisit MCP tool
emission code when SQL Server's JSON support evolves.

Integration tests assert the **absence** of any JSON-specific
annotation on a JSON field in `describe_entities` output. The test
guards against accidental regression of this design.

### 2.4 Null handling (all surfaces)

For a nullable JSON column:

- `null` on insert, PUT, PATCH, or GraphQL mutation persists SQL
  `NULL`.
- Omitting the field entirely on insert persists SQL `NULL`.
- A SQL `NULL` value renders as `null` in the response payload.

### 2.5 SQL Server version handling

DAB does not probe the SQL Server version. If the target server is
older than SQL Server 2025 or current Azure SQL Database, the database
itself returns an error at schema-load or query-execution time, and
that error flows through DAB's existing error pipeline. The minimum
supported version is added to the documentation; there is no DAB-side
startup gate or warning.

---

## 3. Out of scope

- **Other database engines.** PostgreSQL, MySQL, Cosmos DB, and SQL
  DW are out of scope. Their schemas, code, and test fixtures are not
  touched. A regression-guard task asserts zero diff in foreign-engine
  test fixtures.
- **No new GraphQL scalar** named `Json` or `JSON`. The column uses
  the built-in `String` scalar.
- **No changes to the configuration JSON schema**
  (`schemas/dab.draft.schema.json`) or to the `dab` CLI surface.
  Existing configuration files continue to work without modification.
- **No DAB-side JSON-specific behavior.** DAB does not validate,
  normalize, pretty-print, or pre-filter JSON values. It also does
  **not** maintain a JSON-specific operator support matrix; every
  filter / order-by operator is forwarded to SQL Server.
- **No MCP annotation.** JSON columns appear as plain string slots
  in every MCP tool schema. Agents discover JSON shape through the
  same channels they use for any string column.
- **No DAB-side nested-object rejection path.** Non-string tokens on
  a JSON column are rejected naturally by the existing REST
  deserializer / GraphQL input validator.
- **No JSON-path filtering.** Predicates such as
  `JSON_VALUE(metadata, '$.role') eq 'admin'` are not generated by
  DAB's OData translator. They may be a follow-up feature if
  customer demand emerges.
- **No DAB-side SQL Server version probe.** If the target server is
  older than SQL Server 2025 / current Azure SQL DB, SQL Server
  returns an error itself; DAB does not add a startup gate.

---

## 4. Prerequisite dependency

### Why it exists

DAB currently pins `Microsoft.Data.SqlClient` at version **5.2.3**.
The `SqlDbType.Json` enum value that this feature depends on first
appears in version **6.0.0**.

### Why it is a separate PR

The SqlClient major version bump affects every database engine that
goes through SqlClient, not just SQL Server. The bump touches three
non-feature files: `src/Directory.Packages.props`, `external_licenses/`,
and `scripts/notice-generation.ps1`. It must be validated against the
full multi-engine CI matrix (PostgreSQL, MySQL, Cosmos DB, SQL DW) in
isolation. Bundling that risk with a feature PR would make review and
bisecting harder if a regression appears.

### Sequencing

1. Prerequisite PR (SqlClient 6.x bump) is opened, reviewed across all
   engine categories, and merged to `main`.
2. This feature branch rebases on top.
3. This feature's first task (T001) is a read-only pre-flight that
   verifies `Microsoft.Data.SqlClient >= 6.0.0` is in place. If the
   prerequisite is missing, T001 halts implementation with a clear
   message.

This feature's task list explicitly forbids editing the three files
the prerequisite PR owns. The boundary is enforced by the task
definitions themselves.

---

## 5. Quality bar and verification

**Integration-test-first.** DAB's engineering constitution requires
that every behavior change ship with an integration test under the
appropriate `TestCategory`. For this feature, every functional
requirement maps to at least one SQL Server integration test under
`TestCategory=MsSql`. The full requirement-to-task coverage matrix is
maintained in `specs/001-mssql-json-type/tasks.md`.

**Multi-engine regression guard.** Because the underlying SqlClient
upgrade affects more than just SQL Server, the merge gate runs the
PostgreSQL, MySQL, Cosmos DB NoSQL, and SQL DW test categories and
asserts pass/fail counts are identical to the pre-change baseline. A
static `git diff --stat` guard additionally asserts zero lines changed
under foreign-engine fixture files (`DatabaseSchema-PostgreSql.sql`,
`DatabaseSchema-MySql.sql`, `DatabaseSchema-DwSql.sql`, and the
Cosmos DB configuration and schema files).

**Formatting gate.** `dotnet format src/Azure.DataApiBuilder.sln
--verify-no-changes` must pass. This check is enforced in CI.

**No secrets in source.** All connection strings flow through
`@env('VAR_NAME')` references. No credentials, tokens, or sample
passwords are committed.

**SQL Server error number surfaced in error responses.** When SQL
Server rejects a malformed-JSON write or an unsupported filter
operator, the response body includes the SQL Server error number so
the customer can diagnose the cause. (This supersedes an earlier
design that suppressed the SQL error number in production.)

---

## 6. Implementation plan

The work is broken into seven phases. Each phase has a verification
gate; the next phase begins only when the previous phase's tests are
green.

| Phase | Output | Verification gate |
|---|---|---|
| 1. Pre-flight | Read-only check that `Microsoft.Data.SqlClient >= 6.0.0` is present | Halt with a clear message if the prerequisite PR has not been merged |
| 2. Test fixtures | Adds a `profiles` table (`id INT PK`, `metadata JSON NULL`) and five representative seed rows to `DatabaseSchema-MsSql.sql`, plus a `Profile` entity to `dab-config.MsSql.json` | `dab validate` passes; existing SQL Server test suite remains green |
| 3. Type mapping | Single dictionary entry in `TypeHelper.cs` mapping `SqlDbType.Json` to `typeof(string)`. Triggers correct behavior in OpenAPI generation, GraphQL schema, MCP tool emission, and the read pipeline | TypeHelper unit tests plus the read-path integration tests |
| 4. Error-code list extension | Appends the SQL Server JSON-validation error codes (currently 13608–13614) to `MsSqlDbExceptionParser.BadRequestExceptionCodes` so they map to HTTP 400 and GraphQL `BAD_REQUEST`. Covers BOTH malformed writes AND filter operators SQL Server cannot evaluate against a JSON column | Malformed-JSON integration test and filter-pass-through-failure integration test both return 400 with the SQL error number in the body |
| 5. Integration tests | Full REST, GraphQL, and MCP test coverage for every user story and every functional requirement, including the MCP **negative-assertion** test that verifies no JSON-specific annotation is added | Every requirement covered by at least one SQL Server test |
| 6. Regression guard | Runs PostgreSQL, MySQL, Cosmos DB, and SQL DW test categories; static diff guard on foreign fixtures | Pass counts match baseline; zero lines changed in foreign fixtures |
| 7. Polish and release | `dotnet format` clean; minimum-version note added to the documentation; one line added to the release notes | CI fully green |

**Production-code delta**: two files (`src/Core/Services/TypeHelper.cs`,
`src/Core/Resolvers/MsSqlDbExceptionParser.cs`), plus an optional
small touch in the error-envelope serializer if the SQL error number
is not already included — to be verified during implementation per
[`research.md` R4](./specs/001-mssql-json-type/research.md).

**MVP slice.** If a time-boxed delivery is needed, the minimum
end-to-end proof is schema discovery, single-row read, and
malformed-JSON error mapping. Write-path, filter-pass-through, and
MCP negative-assertion test work then follow as additional batches.

---

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| SqlClient 6.x bump destabilizes existing engines | Delivered as a separate prerequisite PR; the full multi-engine CI matrix runs on it in isolation |
| Wrong subset of SQL JSON error codes mapped to HTTP 400 | Integration tests for malformed JSON (write path) AND unsupported filter operators (read path) are the source of truth. The speculative code list (13608–13614) is pruned to the actually-triggered numbers during implementation |
| Accidental edits to foreign-engine fixtures | The regression-guard phase includes an explicit `git diff --stat` static guard plus four engine test runs with baseline-equivalence assertion |
| Future SQL Server changes shift the JSON error code set | The exact code set is verified at implementation time against a live SQL Server 2025+ build. The specification carries an explicit caveat. Note that future operator additions on the JSON type require **no** DAB change, because there is no operator allow-list to update |
| Existing error-envelope serializer omits the SQL error number from the body | Verified during implementation; if the existing serializer suppresses it, a small touch is added to include it. Flagged in [`research.md` R4](./specs/001-mssql-json-type/research.md) |
| Future regression accidentally adds an MCP annotation for JSON | A negative-assertion integration test pins the "no annotation" contract; the test fails if a `description`, `format`, or other JSON-specific extension is introduced |

---

## 8. Reference map

All design artifacts live under [`specs/001-mssql-json-type/`](./specs/001-mssql-json-type/).
Treat the files below as the source of truth; this brief is a
summary view.

| Artifact | Purpose |
|---|---|
| [`spec.md`](./specs/001-mssql-json-type/spec.md) | Feature specification. Defines 17 functional requirements (FR-001 through FR-017), 9 user stories (US1 through US9), 5 success criteria, and the clarifications log (2026-06-04 + 2026-06-09 supersession) |
| [`plan.md`](./specs/001-mssql-json-type/plan.md) | Implementation plan, constitution check (all 7 principles), file touchpoints (only `TypeHelper.cs` and `MsSqlDbExceptionParser.cs`), and prerequisite-dependency declaration |
| [`research.md`](./specs/001-mssql-json-type/research.md) | 11 research items (R1 through R11). R3 (operator gate) and R5 (MCP annotation) are marked SUPERSEDED 2026-06-09 with original decisions retained for audit |
| [`data-model.md`](./specs/001-mssql-json-type/data-model.md) | The `profiles` test fixture (table definition and 5 seed rows) |
| [`contracts/rest-openapi.md`](./specs/001-mssql-json-type/contracts/rest-openapi.md) | REST OpenAPI shape; filter pass-through table; error envelope summary |
| [`contracts/graphql.md`](./specs/001-mssql-json-type/contracts/graphql.md) | GraphQL SDL shape; filter pass-through behavior; error envelope |
| [`contracts/mcp-tools.md`](./specs/001-mssql-json-type/contracts/mcp-tools.md) | MCP "no annotation" contract; negative-assertion test obligation |
| [`quickstart.md`](./specs/001-mssql-json-type/quickstart.md) | Manual end-to-end validation steps a reviewer can run locally |
| [`tasks.md`](./specs/001-mssql-json-type/tasks.md) | The 34 dependency-ordered implementation tasks across 7 phases, with the requirement-to-task coverage matrix |
| [`checklists/requirements.md`](./specs/001-mssql-json-type/checklists/requirements.md) | Review checklist used to validate the spec before implementation; includes a 2026-06-09 supersession update marking obsolete items |

When this brief and the specs disagree, the specs are correct. This
brief is updated when any of the specs change in a way that affects
its summaries.

---

## 9. Open decisions and review asks

### For Product Management

1. Confirm scope: SQL Server only, string passthrough, no new scalar,
   no JSON-path filter. If JSON-path predicates or typed JSON columns
   are needed, they become separate follow-up issues.
2. Confirm the minimum supported version statement for the
   documentation: SQL Server 2025 or later, and current Azure SQL
   Database, with no DAB-side version probe.
3. Confirm the prerequisite-PR sequencing is acceptable: the SqlClient
   6.x bump ships first and merges independently of this feature.

### For Engineering

1. Review the artifacts listed in Section 8, in particular the file
   touchpoints in `plan.md` and the requirement-to-task coverage
   matrix in `tasks.md`.
2. Reviewers needed during PR review, one each for:
   - SQL engine path (Phases 3–4: `TypeHelper`,
     `MsSqlDbExceptionParser`, plus possibly the error-envelope
     serializer per [`research.md` R4](./specs/001-mssql-json-type/research.md))
   - Test fixtures and integration coverage (Phases 2 and 5),
     including the MCP negative-assertion test
   - Documentation and release notes (Phase 7)
3. Confirm the SQL-error-number surfacing in the response body is
   the intended customer-facing diagnostic. (Earlier design hid it;
   the 2026-06-09 design includes it.)
