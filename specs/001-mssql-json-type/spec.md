# Feature Specification: MSSQL JSON Data Type Support

**Feature Branch**: `Usr/sogh/speckit-jsontypesupport`

**Created**: 2026-06-04

**Status**: Draft

**Input**: User description: "Implement MSSQL JSON data type support per https://github.com/Azure/data-api-builder/issues/2768. Treat JSON as a string in both directions (read/write); MSSQL only."

## Clarifications

### Session 2026-06-09 (supersedes 2026-06-04 answers below)

Product Management consolidated the following directive, which **supersedes** the 2026-06-04 answers wherever the two conflict. The governing principle is: **a JSON column is treated exactly like a string column. DAB adds no JSON-specific behavior, gating, or annotation. If SQL Server rejects something, DAB surfaces the SQL Server error code through the existing error pipeline as HTTP 400.** Rationale: when SQL Server adds support for additional operators on JSON columns in future releases, DAB inherits that support with zero code changes.

- Q: How does DAB validate the payload of a write to a JSON column? → A: It does not validate at all. DAB binds the incoming value via the existing string-column path. Hot Chocolate enforces the `String` input type for GraphQL; the REST deserializer rejects non-string JSON tokens for a string-typed property. No DAB-specific JSON rejection code is added.
- Q: What `$filter` operators are permitted on a JSON column? → A: **All of them.** DAB does not gate operators. Whatever the customer supplies is translated to SQL exactly as for any other string-typed column. SQL Server rejects operators it does not support, and the resulting error flows through the standard DAB error pipeline.
- Q: When SQL Server returns an error on a JSON column write, filter, or order-by, what does DAB return? → A: HTTP `400 Bad Request` (REST) / GraphQL error with `extensions.code = "BAD_REQUEST"`. The response body MUST include the SQL Server error number so the customer can diagnose the cause. This is achieved by appending the SQL Server JSON-validation error codes (13608–13614) to the existing `MsSqlDbExceptionParser.BadRequestExceptionCodes` list — the only DAB code change related to errors.
- Q: Are MCP tool schemas annotated for JSON columns? → A: **No.** A JSON column appears in `DescribeEntitiesTool`, `DynamicCustomTool`, `CreateRecordTool`, and `UpdateRecordTool` exactly as a plain string column does. No `description`, no `format`, no MCP-specific hint.
- Q: How does DAB behave against a SQL Server build that does not support the native JSON type? → A: No version probe. SQL Server returns its own error; DAB surfaces it through the existing pipeline. (Unchanged from 2026-06-04.)

### Session 2026-06-04 (historical — several answers superseded above)

Answers below are preserved for audit. Where they conflict with the 2026-06-09 session, the 2026-06-09 answer governs.

- Q: When a client sends a write (REST POST/PUT/PATCH or GraphQL mutation) for a JSON column, what payload shape does DAB accept? → A: Strict string only. The field MUST be a JSON string; sending a nested object or array MUST be rejected with a 4xx error (REST) / GraphQL input-validation error before reaching SQL Server.
- Q: What HTTP status code does DAB return on the REST surface when SQL Server rejects a write for invalid JSON syntax? → A: 400 Bad Request (GraphQL: error extension `code: "BAD_REQUEST"`), matching DAB's existing column-type / parse-failure mapping.
- Q: Which `$filter` operators does DAB permit against a JSON column? → A: `eq`, `ne`, and null checks (`metadata eq null` / `metadata ne null`). All other operators (`startswith`, `contains`, `endswith`, `gt`, `lt`, JSON-path predicates) MUST be rejected with a 400 identifying that the operator is not supported on JSON columns.
- Q: How does DAB behave when an entity declares a JSON column but the target SQL Server build is older than the minimum supported version (Azure SQL DB current / SQL Server 2025+)? → A: No special detection. DAB relies on SQL Server's native error at schema-load or query-execution time, surfaced through the existing error pipeline. No startup version probe; no DAB-side JSON-version warning.
- Q: Should DAB annotate JSON columns in MCP tool schemas (DescribeEntitiesTool, CreateRecordTool, UpdateRecordTool, DynamicCustomTool) so LLM agents know to embed JSON text rather than a nested object or a double-stringified value? → A: Yes — add a `description` only (no non-standard `format` value). The description MUST instruct agents that the value is a JSON-encoded string containing valid JSON text and that nested objects/arrays are not accepted.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - JSON column appears as String in API schemas (Priority: P1)

When an entity backed by a SQL Server table that has a `JSON`-typed column is
exposed through DAB, the column is surfaced in both the REST OpenAPI document
and the GraphQL SDL as a String-typed field (no new scalar). Tooling such as
Swagger UI, GraphQL clients, and code generators see a familiar `string` /
`String` type without DAB-specific extensions.

**Why this priority**: This is the discovery contract. Without it, no other
read or write story is observable to a client; every other story depends on
the schema being correct.

**Independent Test**: Spin up DAB against a SQL Server database containing a
`Profiles` table with a `JSON` column `metadata`. Fetch `/api/openapi` and the
GraphQL schema via introspection. Verify that `metadata` is described as a
nullable string in OpenAPI and as `String` in the SDL.

**Acceptance Scenarios**:

1. **Given** a `Profiles` entity with a `metadata JSON NULL` column,
   **When** the client retrieves the REST OpenAPI document,
   **Then** `metadata` is listed as `type: string`, `nullable: true`, with
   no schema-level JSON validation attached.
2. **Given** the same entity,
   **When** the client introspects the GraphQL schema,
   **Then** the `metadata` field is typed `String` (nullable) on the entity
   type and as `String` in `create`/`update` input types.
3. **Given** the same entity exposed via MCP,
   **When** an MCP client invokes `DescribeEntitiesTool` for the
   entity (or inspects the input schema of any other MCP tool that
   surfaces the column, such as `DynamicCustomTool`,
   `CreateRecordTool`, or `UpdateRecordTool`),
   **Then** the JSON field / parameter appears as a plain `type:
   string` slot with no JSON-specific `description`, `format`, or
   other extension. The column is indistinguishable from an
   ordinary string column at the MCP layer.
---

### User Story 2 - Read a single row by primary key (Priority: P1)

A client issues `GET /api/profiles/id/1` (REST) or the GraphQL equivalent
single-item query. DAB returns the row, and the `metadata` field is delivered
as a JSON-encoded *string* (i.e., the raw JSON text the database stored,
serialized as a JSON string literal in the response payload). DAB does not
parse the JSON into a nested object.

**Why this priority**: Single-item read is the canonical happy-path read and
the simplest demonstration of the round-trip behavior.

**Independent Test**: Insert a row with
`metadata = '{"role":"admin","tier":3}'` via SQL. Call
`GET /api/profiles/id/1`. Confirm the response payload contains
`"metadata": "{\"role\":\"admin\",\"tier\":3}"` (a JSON string whose
characters are the original JSON text).

**Acceptance Scenarios**:

1. **Given** a `Profiles` row with a non-null `metadata` JSON value,
   **When** the client GETs the row by primary key,
   **Then** `metadata` in the response is a JSON string containing the
   original JSON text verbatim (preserved escaping, no re-formatting,
   no nested object expansion).
2. **Given** the same row,
   **When** the client requests the row via a GraphQL single-item query,
   **Then** `metadata` is returned as a `String` whose value is the original
   JSON text.

---

### User Story 3 - Read a collection (Priority: P1)

`GET /api/profiles` (REST) and the GraphQL list query return multiple rows,
each with its `metadata` field rendered as a JSON-encoded string, identical
in shape to the single-row behavior. Pagination, projection ($select), and
the standard envelope are unaffected.

**Why this priority**: Collection reads are the most common production
workload; parity with single-item reads must be guaranteed.

**Independent Test**: Insert three rows with different JSON payloads
(including one `NULL`). Call `GET /api/profiles` and the GraphQL list query.
Confirm each row's `metadata` is either a string of the original JSON text
or `null`.

**Acceptance Scenarios**:

1. **Given** a `Profiles` table with multiple rows whose `metadata` values
   differ in shape (object, array, nested, `NULL`),
   **When** the client lists the entity,
   **Then** each row's `metadata` is a JSON string of the stored text, or
   `null` for null rows.
2. **Given** a `$select=metadata` projection,
   **When** the client requests only that field,
   **Then** the field is still rendered as a JSON string, not expanded.

---

### User Story 4 - Insert via REST and GraphQL (Priority: P1)

A client creates a new row by sending the JSON column value as a string in
the request body. SQL Server validates the JSON syntax on write; DAB
performs no additional JSON-schema validation.

**Why this priority**: Write support is required to make the feature
end-to-end useful; without it, the type is read-only and the feature is
incomplete.

**Independent Test**: `POST /api/profiles` with body
`{ "id": 2, "metadata": "{\"role\":\"guest\"}" }`. Verify a 201 response, a
matching row exists in SQL Server, and `SELECT metadata FROM Profiles WHERE
id = 2` returns the JSON text. Repeat via a GraphQL `createProfile` mutation.

**Acceptance Scenarios**:

1. **Given** a `Profiles` entity with create permissions,
   **When** the client POSTs `{ "id": 2, "metadata": "{\"role\":\"guest\"}" }`,
   **Then** the response is 201, the persisted `metadata` column equals
   `{"role":"guest"}`, and a subsequent GET returns the same string.
2. **Given** the same entity,
   **When** the client issues a GraphQL `createProfile` mutation with the
   `metadata` argument set to a JSON-string,
   **Then** the mutation succeeds and the returned `metadata` echoes the
   stored string.

---

### User Story 5 - Update via PUT, PATCH, and GraphQL (Priority: P1)

The client replaces or partially updates the JSON value with another valid
JSON string. PUT replaces the full row, PATCH updates only the supplied
fields, and the GraphQL `update` mutation has equivalent semantics.

**Why this priority**: Write parity for update is as important as for
insert; many real workloads update the JSON payload more often than they
insert.

**Independent Test**: For an existing row, send
`PUT /api/profiles/id/1` with `{ "metadata": "{\"role\":\"owner\"}" }` and
verify the column value changes. Repeat with PATCH and GraphQL.

**Acceptance Scenarios**:

1. **Given** an existing `Profiles` row,
   **When** the client PUTs a body containing a new valid JSON string for
   `metadata`,
   **Then** the persisted value is replaced and the response reflects the
   new string.
2. **Given** an existing row,
   **When** the client PATCHes only the `metadata` field,
   **Then** the response and database row contain the new JSON string and
   all other columns are unchanged.
3. **Given** an existing row,
   **When** the client issues a GraphQL `updateProfile` mutation with a new
   `metadata` string,
   **Then** the mutation succeeds with the new value echoed in the result.

---

### User Story 6 - Delete (Priority: P2)

`DELETE /api/profiles/id/1` and the GraphQL delete mutation behave
identically to delete on any other entity; the presence of a JSON column
imposes no special behavior.

**Why this priority**: Required for CRUD completeness but inherits all
behavior from existing delete paths; low implementation risk.

**Independent Test**: Delete a row with a non-null `metadata`. Verify the
row is removed; ensure no error is raised and other rows are unaffected.

**Acceptance Scenarios**:

1. **Given** a `Profiles` row containing a JSON `metadata` value,
   **When** the client deletes it via REST or GraphQL,
   **Then** the response is the standard success status and the row is
   gone from the table.

---

### User Story 7 - Malformed JSON on write surfaces a 400 with the SQL error code (Priority: P1)

When a client submits a `metadata` value that is *not* valid JSON text, SQL
Server rejects the insert/update with one of its JSON-validation error
numbers (13608–13614 in current SQL Server builds). DAB surfaces the
rejection as HTTP `400 Bad Request` (REST) or a GraphQL error with
`extensions.code = "BAD_REQUEST"`, and the response body MUST include the
SQL Server error number so the customer can identify the cause.

**Why this priority**: Without a clean 400 response, malformed input
causes 5xx failures and a poor developer experience. The mapping reuses
DAB's existing parse-failure error pipeline; the only addition is
appending the JSON error numbers to the existing
`MsSqlDbExceptionParser.BadRequestExceptionCodes` list.

**Independent Test**: `POST /api/profiles` with body
`{ "id": 3, "metadata": "{not valid json" }`. Verify the response is
`400 Bad Request` and the response body contains the SQL Server error
number that was raised. Repeat via GraphQL and verify a GraphQL error
of equivalent specificity.

**Acceptance Scenarios**:

1. **Given** the `Profiles` entity,
   **When** the client POSTs a body whose `metadata` value is not valid
   JSON text,
   **Then** the response status is `400 Bad Request`, the row is not
   persisted, and the error body contains the SQL Server error number
   raised by the failed insert.
2. **Given** the same entity,
   **When** the client issues a GraphQL mutation with an invalid JSON
   string for `metadata`,
   **Then** the mutation returns a GraphQL error with
   `extensions.code = "BAD_REQUEST"` whose message likewise contains
   the SQL Server error number, and no row is persisted.

---

### User Story 8 - Null handling (Priority: P2)

A JSON column declared as nullable accepts `NULL` on insert, update, and is
returned as `null` on read. Omitting the field on insert (when the column
is nullable) inserts `NULL`. PATCH with `metadata: null` sets the column to
NULL.

**Why this priority**: Required for correctness but is mechanically simple
once the type plumbing is in place.

**Independent Test**: Insert with `metadata = null` (and with the field
omitted); verify the database column is `NULL` in both cases. PATCH a
non-null row with `metadata: null`; verify the column becomes `NULL`. Read
the rows and verify the JSON response renders `null`.

**Acceptance Scenarios**:

1. **Given** a nullable `metadata` column,
   **When** the client POSTs a body with `metadata: null` or omits the
   field,
   **Then** the persisted column is `NULL` and reads return `null`.
2. **Given** an existing row with a non-null `metadata`,
   **When** the client PATCHes `metadata: null`,
   **Then** the column becomes `NULL` and the response reflects `null`.

---

### User Story 9 - Filter and orderby behavior on a JSON column (Priority: P2)

DAB exposes the JSON column to `$filter` and `$orderby` identically to
any other string-typed column. **Every** OData operator that DAB supports
on string columns is permitted; DAB performs no JSON-specific gating.
SQL Server is the authority on which operators are valid against the
native `JSON` type and returns its own error when an operator is not
supported — that error flows through the standard DAB pipeline as HTTP
`400 Bad Request` (REST) or `extensions.code = "BAD_REQUEST"` (GraphQL)
with the SQL Server error number included in the body.

- **Filter**: `eq`, `ne`, null checks, and any other operator the client
  submits are translated to SQL and forwarded. Operators that SQL Server
  supports (currently `eq`, `ne`, null comparison against the stored
  JSON text as a string) succeed; operators SQL Server does not support
  (currently `startswith`, `contains`, `endswith`, `gt`, `lt`, `ge`,
  `le`, JSON-path predicates) cause a SQL Server error that DAB surfaces
  as 400.
- **OrderBy**: forwarded to SQL unchanged. Ordering succeeds if SQL
  Server supports it on the column.

This design intentionally avoids hard-coding the SQL Server operator
support matrix in DAB. When SQL Server extends operator support in a
future release, DAB inherits the new behavior with no code change.

**Why this priority**: Filter and order-by support must work, but the
implementation requires no DAB code beyond the type mapping in User
Story 1. Test coverage pins the documented behavior so future
regressions are caught.

**Independent Test**: Issue `GET /api/profiles?$filter=metadata eq
'{"role":"admin"}'` and verify only the matching rows are returned.
Issue `GET /api/profiles?$filter=metadata eq null` and verify rows whose
column is `NULL` are returned. Issue `GET
/api/profiles?$orderby=metadata` and verify the ordering matches the
string sort SQL Server applies. Issue a `$filter` using an operator SQL
Server does not support on the JSON type (for example,
`contains(metadata,'x')`) and verify the response is `400 Bad Request`
whose body includes the SQL Server error number.

**Acceptance Scenarios**:

1. **Given** a `Profiles` collection,
   **When** the client filters `metadata eq '<exact-json-string>'`,
   **Then** only rows whose stored JSON text equals that string are
   returned.
2. **Given** the same collection,
   **When** the client filters `metadata eq null` or `metadata ne null`,
   **Then** rows are filtered by SQL `NULL` semantics on the JSON column.
3. **Given** the same collection,
   **When** the client orders by `metadata`,
   **Then** results are sorted by the string value of the column as SQL
   Server orders it.
4. **Given** the same collection,
   **When** the client uses an operator SQL Server does not support on
   the JSON type (for example `contains`, `startswith`, `gt`),
   **Then** the response is a 400 error whose body contains the SQL
   Server error number for the rejected operation, and the request
   reaches the database (DAB does not pre-filter).

---

### Edge Cases

- **Empty JSON string `""`**: SQL Server rejects an empty string as invalid
  JSON; behavior is covered by User Story 7 (4xx with clear message).
- **Very large JSON payload**: Must be accepted up to the existing DAB
  request-size limit and existing SQL Server column / row size limits; no
  special truncation by DAB.
- **Non-ASCII characters and Unicode escapes**: JSON text containing
  Unicode characters and `\uXXXX` escapes must round-trip unchanged on
  read after write.
- **JSON text containing surrounding whitespace or differing key ordering
  vs. the input**: DAB does not normalize; the stored text is whatever SQL
  Server persists. Tests assert the round-trip is "string-equal to what
  SQL Server returns from a `SELECT metadata`," not "byte-equal to what
  the client sent."
- **Concurrent updates** to the same JSON column behave like any other
  scalar column update (no special merging).
- **Permissions**: column-level read/write permissions apply to the JSON
  column identically to other scalar columns; no special policy.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: DAB MUST recognize the SQL Server `JSON` column type during
  schema discovery on Azure SQL DB and SQL Server 2025+, and MUST map it
  to the same surface type used for ordinary text columns in both REST
  (OpenAPI `type: string`) and GraphQL (`String`).
- **FR-002**: REST responses MUST render a JSON column's value as a JSON
  string whose content is the exact JSON text returned by SQL Server, with
  no parsing into a nested object or array.
- **FR-003**: GraphQL responses MUST render a JSON column's value as a
  `String` whose content is the exact JSON text returned by SQL Server.
- **FR-004**: REST write operations (POST/PUT/PATCH) MUST accept a JSON
  column value through the existing string-column write path and pass
  the value to SQL Server unchanged. DAB MUST NOT add JSON-specific
  validation, rejection, or transformation logic. If the request body
  supplies a non-string JSON token (object, array, number, boolean)
  where a string is expected, the existing JSON deserializer for
  string-typed properties rejects the request — DAB inherits that
  behavior; it does not add a parallel check.
- **FR-005**: GraphQL mutations MUST accept a JSON column value as a
  `String` argument and pass the value to SQL Server unchanged. The
  GraphQL input type for the field MUST be `String` so that nested
  object inputs are rejected by Hot Chocolate's built-in input
  validation. DAB MUST NOT add JSON-specific input validation.
- **FR-006**: DAB MUST NOT perform JSON-schema validation on inbound
  values; SQL Server is the authority on JSON syntax validity.
- **FR-007**: When SQL Server returns a JSON-related error on a JSON
  column (for example a JSON-syntax validation error on write, or an
  unsupported-operator error on filter / order-by), DAB MUST return
  HTTP `400 Bad Request` (REST) or a GraphQL error with
  `extensions.code = "BAD_REQUEST"` (GraphQL). The response body MUST
  include the SQL Server error number so the customer can diagnose the
  cause. This is achieved by appending the SQL Server JSON-validation
  error codes (currently 13608–13614; final list verified against the
  target SQL Server 2025 build during implementation) to the existing
  `MsSqlDbExceptionParser.BadRequestExceptionCodes` list. No other
  changes to the error pipeline are made.
- **FR-008**: A nullable JSON column MUST accept `null` on insert, update
  (PUT/PATCH), and GraphQL mutations; reads MUST render `null` for `NULL`
  database values.
- **FR-009**: `$filter` against a JSON column MUST be processed by DAB
  exactly as for any other string-typed column. DAB MUST NOT maintain
  a JSON-specific operator allow-list or rejection list. Every operator
  the client submits is translated to SQL and forwarded. Operators SQL
  Server supports succeed; operators SQL Server does not support cause
  a SQL Server error that DAB surfaces via FR-007. Rationale: when SQL
  Server extends operator support on the native JSON type in a future
  release, DAB inherits that support with no code change.
- **FR-010**: `$orderby` against a JSON column MUST be permitted and MUST
  order by the underlying string representation.
- **FR-011**: DELETE on a row containing a JSON column MUST behave
  identically to DELETE on any other row; no special handling.
- **FR-012**: The feature MUST be scoped to MSSQL only. PostgreSQL, MySQL,
  CosmosDB NoSQL, and SQLDW behaviors MUST be unchanged by this work, and
  their integration test suites MUST continue to pass without
  modifications.
- **FR-013**: DAB MUST NOT introduce a new GraphQL scalar type for JSON;
  the existing `String` scalar is reused.
- **FR-014**: The CLI surface (`dab init`, `dab add`) MUST NOT require new
  flags to support JSON columns; column type detection is automatic from
  the database schema.
- **FR-015**: Integration tests under `src/Service.Tests` with
  `TestCategory=MsSql` MUST cover User Stories 1–9 and the edge cases
  above. `src/Service.Tests/DatabaseSchema-MsSql.sql` MUST add a table
  with at least one JSON column to support these tests.
- **FR-016**: DAB MUST NOT perform a SQL Server build-version probe for
  JSON support. When an entity declares a JSON column against a server
  build that does not support the native `JSON` type, the resulting
  SQL Server error MUST be surfaced through DAB's existing error
  pipeline (schema-load failure at startup, or a request-time error)
  without DAB-specific translation. Documentation MUST state the
  minimum supported server versions (Azure SQL DB current; SQL Server
  2025+).
- **FR-017**: MCP tool schemas MUST surface a SQL Server `JSON` column
  identically to a string column. No JSON-specific `description`,
  `format`, or other MCP annotation is added by this feature.
  `DescribeEntitiesTool`, `DynamicCustomTool`, `CreateRecordTool`, and
  `UpdateRecordTool` are all unchanged for JSON columns. Integration
  tests under `TestCategory=MsSql` MUST assert that the column appears
  in `DescribeEntitiesTool` output without any JSON-specific
  annotation, to guard against accidental future divergence.

### Key Entities

- **Profile (test fixture)**: A row in the `Profiles` table created in
  `DatabaseSchema-MsSql.sql`. Attributes: an integer primary key (`id`)
  and a nullable `JSON` column (`metadata`). Used by integration tests to
  exercise read, write, delete, filter, orderby, and null handling for
  the JSON type. No business meaning beyond test fixture.
- **JSON column type**: A SQL Server column whose declared type is `JSON`
  (introduced in Azure SQL DB and SQL Server 2025). From DAB's
  perspective, an opaque string carrier whose validation is delegated to
  the database engine.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the integration test scenarios derived from User
  Stories 1–9 pass under `TestCategory=MsSql` on a supported SQL Server
  build.
- **SC-002**: 0 regressions in the PostgreSQL, MySQL, CosmosDB NoSQL, and
  SQLDW integration test suites attributable to this change (existing
  test counts pass unchanged).
- **SC-003**: A round-trip write→read of a JSON value preserves the exact
  text returned by `SELECT` against SQL Server in 100% of test cases,
  across object, array, nested, and Unicode payloads.
- **SC-004**: Any write, filter, or order-by request that SQL Server
  rejects with a JSON-validation or JSON-unsupported-operator error
  number returns HTTP `400 Bad Request` (REST) or GraphQL
  `extensions.code: "BAD_REQUEST"` (GraphQL), with the SQL Server
  error number included in the response body, in 100% of integration
  test cases. No 5xx surfaces for these error paths.
- **SC-005**: A client developer can, given only the published OpenAPI
  document and GraphQL SDL, correctly identify how to send and receive
  JSON-typed columns without consulting DAB-specific documentation
  (validated by the fact that the column appears as a standard string
  type with no DAB extensions).

## Assumptions

- The target SQL Server build supports the native `JSON` column type
  (Azure SQL DB current, SQL Server 2025+). Tests are exercised against
  an environment where this type is available.
- The existing string-handling pipeline (parameter binding, response
  serialization) in `Config → Resolvers → GraphQL SDL` for `varchar(max)`
  is the model to mirror per the constitution's Minimal-Surface
  Changes principle.
- Column-level authorization, request size limits, and existing error
  middleware are reused as-is; no new error-mapping infrastructure is
  introduced solely for this feature. The single concession is
  appending JSON-validation SQL error numbers (currently 13608–13614)
  to `MsSqlDbExceptionParser.BadRequestExceptionCodes`, per FR-007.
- DAB does **not** maintain any JSON-specific knowledge of which SQL
  operators are supported on the native JSON type. The supported-
  operator matrix is SQL Server's responsibility (per FR-009 and the
  2026-06-09 Clarifications session).
- No CLI changes (`dab init` / `dab add` flags) are introduced; the
  config schema is unchanged in shape.
