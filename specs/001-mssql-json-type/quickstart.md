# Quickstart: MSSQL JSON Data Type Support

**Feature**: 001-mssql-json-type
**Purpose**: End-to-end manual validation that the feature works against
a real SQL Server 2025+ / Azure SQL DB target. Mirrors what the
automated MSSQL integration tests assert.

> **Revision 2026-06-09**: Per the Clarifications session in
> [spec.md](./spec.md), DAB treats a SQL Server `JSON` column exactly
> like a string column. Step 7 below has been updated so the error
> body explicitly contains the SQL Server error number; Step 8 has
> been updated so unsupported filter operators are forwarded to SQL
> rather than rejected by DAB; Step 10 has been replaced with a
> negative-assertion MCP check.

This is a **validation guide**, not implementation. For exact request
shapes see:

- [contracts/rest-openapi.md](./contracts/rest-openapi.md)
- [contracts/graphql.md](./contracts/graphql.md)
- [contracts/mcp-tools.md](./contracts/mcp-tools.md)

For the test fixture schema see
[data-model.md](./data-model.md).

---

## Prerequisites

- .NET 8 SDK (per `global.json`).
- Docker Desktop (for the SQL Server 2025+ container).
- A SQL client (e.g., `sqlcmd`, Azure Data Studio, or the VS Code MSSQL
  extension).
- This branch of the DAB repo checked out.
- **`Microsoft.Data.SqlClient >= 6.0.0`** in
  [src/Directory.Packages.props](../../src/Directory.Packages.props).
  This is a **prerequisite delivered by a separate dependency PR**;
  if this branch does not yet contain it, merge or rebase that PR
  before running the quickstart. The repo will not compile-time
  recognize `SqlDbType.Json` without it.

---

## 1. Bring up SQL Server 2025+

```powershell
# Use the existing docker-compose, but ensure the image tag points at
# a SQL Server 2025 (or current Azure SQL Edge equivalent) build that
# supports the native JSON column type.
docker compose -f docker/docker-compose-mssql.yml up -d
```

If `docker/docker-compose-mssql.yml` still references an older SQL
Server image, update the image tag locally to a 2025+ build before
running the JSON tests.

---

## 2. Apply the test schema (which now includes the `profiles` table)

```powershell
$env:SA_PASSWORD = '<your-test-password>'
sqlcmd -S localhost,1433 -U sa -P $env:SA_PASSWORD -d master `
    -i src/Service.Tests/DatabaseSchema-MsSql.sql
```

Verify the new table exists:

```sql
SELECT name, system_type_name
FROM sys.dm_exec_describe_first_result_set(
    'SELECT id, metadata FROM profiles', NULL, 0);
```

`metadata` should report system type `json`.

---

## 3. Point DAB at the fixture config and start

```powershell
$env:MSSQL_CONNECTION_STRING = `
    'Server=localhost,1433;Database=master;User Id=sa;Password=' + $env:SA_PASSWORD + ';TrustServerCertificate=true'

dotnet run --project src/Service `
    --no-launch-profile `
    -- --ConfigFileName src/Service.Tests/dab-config.MsSql.json
```

DAB starts in development mode. Note the REST base path (`/api`) and
GraphQL path (`/graphql`).

---

## 4. Verify schema discovery (User Story 1)

```powershell
# REST OpenAPI: confirm metadata is type: string, nullable: true
curl http://localhost:5000/api/openapi | jq '.components.schemas.Profile'

# GraphQL: introspect the Profile type
curl -X POST http://localhost:5000/graphql `
  -H 'Content-Type: application/json' `
  -d '{"query":"{ __type(name:\"Profile\") { fields { name type { name kind } } } }"}'
```

Expected: `metadata` appears as `type: string` (REST) and `String`
scalar (GraphQL). No new scalar type in the GraphQL `__schema`.

---

## 5. Read a single row & a collection (User Stories 2, 3)

```powershell
# Single
curl http://localhost:5000/api/Profile/id/1

# Collection
curl http://localhost:5000/api/Profile
```

Confirm each row's `metadata` field is a **JSON string** containing the
JSON text (not a nested object). Repeat via GraphQL:

```graphql
query { profile_by_pk(id: 1) { id metadata } }
query { profiles { items { id metadata } } }
```

---

## 6. Create, update, delete (User Stories 4, 5, 6)

```powershell
# Create
curl -X POST http://localhost:5000/api/Profile `
  -H 'Content-Type: application/json' `
  -d '{"metadata":"{\"role\":\"guest\"}"}'

# Update (PUT)
curl -X PUT http://localhost:5000/api/Profile/id/6 `
  -H 'Content-Type: application/json' `
  -d '{"metadata":"{\"role\":\"owner\"}"}'

# Patch
curl -X PATCH http://localhost:5000/api/Profile/id/6 `
  -H 'Content-Type: application/json' `
  -d '{"metadata":"{\"role\":\"viewer\"}"}'

# Delete
curl -X DELETE http://localhost:5000/api/Profile/id/6
```

GraphQL equivalents — see [contracts/graphql.md](./contracts/graphql.md).

---

## 7. Verify error paths (User Story 7)

```powershell
# Malformed JSON text — expect 400 with SQL Server error number in body
curl -i -X POST http://localhost:5000/api/Profile `
  -H 'Content-Type: application/json' `
  -d '{"metadata":"{not valid json"}'

# Non-string token on input — expect 400 from the REST deserializer
# (no DAB-specific check; `metadata` is a string-typed property)
curl -i -X POST http://localhost:5000/api/Profile `
  -H 'Content-Type: application/json' `
  -d '{"metadata":{"role":"guest"}}'
```

Both should return `HTTP 400`. The malformed-JSON body should contain
the SQL Server JSON-validation error number that was raised (currently
in the 13608–13614 range). No 5xx should surface.

---

## 8. Filter and orderby contract (User Story 9)

DAB forwards every `$filter` operator to SQL Server. SQL Server
decides which ones succeed against the JSON type.

```powershell
# Currently succeeds: equality
curl 'http://localhost:5000/api/Profile?$filter=metadata%20eq%20%27%7B%22role%22%3A%22admin%22%2C%22tier%22%3A3%7D%27'

# Currently succeeds: null check
curl 'http://localhost:5000/api/Profile?$filter=metadata%20eq%20null'

# Forwarded to SQL Server; currently fails — expect 400 with the
# SQL error number in the body (DAB does NOT pre-reject)
curl -i 'http://localhost:5000/api/Profile?$filter=contains(metadata,%27admin%27)'

# orderby is forwarded; currently succeeds with string-order semantics
curl 'http://localhost:5000/api/Profile?$orderby=metadata'
```

---

## 9. NULL handling (User Story 8)

```powershell
# Insert with explicit null
curl -X POST http://localhost:5000/api/Profile `
  -H 'Content-Type: application/json' `
  -d '{"metadata":null}'

# Clear an existing value to NULL via PATCH
curl -X PATCH http://localhost:5000/api/Profile/id/1 `
  -H 'Content-Type: application/json' `
  -d '{"metadata":null}'

# Read back: expect "metadata": null
curl http://localhost:5000/api/Profile/id/1
```

---

## 10. MCP plain-string surface check (FR-017)

With MCP enabled in the fixture config, run an MCP `describe_entities`
call (e.g., via MCP Inspector per
[docs/testing-guide/mcp-inspector-testing.md](../../docs/testing-guide/mcp-inspector-testing.md)):

Expected (negative assertion): the `metadata` field metadata for the
`Profile` entity is shaped **identically to a plain string column**.
It MUST NOT contain a `description`, `format`, or any other
JSON-specific annotation. This is the 2026-06-09 design — see
[contracts/mcp-tools.md](./contracts/mcp-tools.md) for the rationale.
If you see an annotation, that is a regression of the design.

---

## 11. Run the automated integration test suite

The above manual steps are mirrored by tests under
`src/Service.Tests` with `TestCategory=MsSql`:

```powershell
dotnet test src/Service.Tests --filter "TestCategory=MsSql"
```

All JSON-related tests added by this feature must pass; pre-existing
MSSQL tests must remain passing. Per Constitution Principle I, also run
the other engine categories on CI before merge:

```powershell
dotnet test src/Service.Tests --filter "TestCategory=PostgreSql"
dotnet test src/Service.Tests --filter "TestCategory=MySql"
dotnet test src/Service.Tests --filter "TestCategory=CosmosDb_NoSql"
dotnet test src/Service.Tests --filter "TestCategory=DwSql"
```

Existing counts in non-MSSQL categories must be **unchanged** by this
feature (SC-002 / FR-012).

---

## Success criteria mapping

| Success criterion | Step(s) above |
|-------------------|---------------|
| SC-001 — 100% of US 1-9 tests pass | Step 11 |
| SC-002 — 0 regressions in other engines | Step 11 (other categories) |
| SC-003 — Write→read round-trips preserve text | Steps 6, 9 |
| SC-004 — Malformed → 400, no 5xx | Step 7 |
| SC-005 — Discoverable without DAB-specific docs | Step 4 (introspection) |
