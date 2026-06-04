# Data Model: MSSQL JSON Data Type Support

**Feature**: 001-mssql-json-type
**Date**: 2026-06-04
**Scope**: Test-fixture schema additions only. No production data model
or runtime config schema changes.

This feature does not introduce or modify any DAB runtime entity. The
only persisted data model artifact is a new test-fixture table that
underwrites the integration tests required by Principles I and II.

---

## Test fixture: `profiles` table (MSSQL only)

**File**: [src/Service.Tests/DatabaseSchema-MsSql.sql](src/Service.Tests/DatabaseSchema-MsSql.sql)

**Purpose**: Provide a minimal table that exercises the SQL Server
native `JSON` column type, so integration tests under
`TestCategory=MsSql` can drive every user story in the spec.

### Columns

| Column | SQL type | Nullable | PK | Notes |
|--------|----------|----------|----|-------|
| `id` | `INT IDENTITY(1,1)` | NO | YES | Surrogate primary key; auto-increment so REST/GraphQL create tests don't need to generate IDs. |
| `metadata` | `JSON` | YES | — | The column under test. Nullable to exercise FR-008 (NULL handling). |

### Validation rules

- `metadata` SQL-level validation is performed exclusively by SQL Server
  (no DAB-side schema validation per FR-006). Invalid JSON text inserted
  via direct SQL is rejected by SQL Server with an error in the 13600
  range; DAB maps these to `HTTP 400` per FR-007 / R4.
- `metadata` accepts any well-formed JSON text — object, array, scalar,
  or `NULL` — without further constraint.

### Relationships

None. The table is intentionally standalone to keep tests focused on
JSON column behavior and to avoid coupling to existing test fixtures.

### State transitions

The `metadata` column has no state machine. Standard CRUD lifecycle:

```
[no row]  --POST/createProfile-->  [persisted, metadata=value-or-null]
[persisted] --PUT/updateProfile-->  [persisted, metadata=new-value]
[persisted] --PATCH/updateProfile metadata-->  [persisted, metadata=new-value]
[persisted] --DELETE/deleteProfile-->  [no row]
```

### Seed rows

Five rows, covering every edge case enumerated in the spec:

| `id` | `metadata` (canonical text) | Edge case |
|------|------------------------------|-----------|
| 1 | `{"role":"admin","tier":3}` | Simple flat object. |
| 2 | `{"tags":["a","b","c"]}` | Object containing an array. |
| 3 | `{"nested":{"key":{"deep":true}}}` | Deeply nested object. |
| 4 | `{"unicode":"éü😀"}` (with `\u`-escaped emoji) | Non-ASCII / surrogate pair preservation. |
| 5 | *(SQL `NULL`)* | NULL handling. |

The exact byte representation returned by `SELECT metadata FROM profiles`
is what tests assert against (R6); JSON-text normalization differences
between the seed literal and SQL Server's stored form (if any) are
absorbed by reading the canonical value from the DB before comparison.

---

## DAB entity: `Profile`

**File**: [src/Service.Tests/dab-config.MsSql.json](src/Service.Tests/dab-config.MsSql.json)

A new entity entry exposes `dbo.profiles` over REST and GraphQL with
permissive permissions for the standard test roles (`anonymous`,
`authenticated`, `authorizationHandlerTester`, etc.) matching the
existing fixture conventions.

```jsonc
"Profile": {
    "source": { "type": "table", "object": "profiles" },
    "rest": { "path": "/profiles" },
    "graphql": { "singular": "profile", "plural": "profiles" },
    "permissions": [
        { "role": "anonymous",     "actions": ["*"] },
        { "role": "authenticated", "actions": ["*"] }
        // Additional roles per existing fixture pattern
    ]
}
```

### Surfaced columns

| Field name (exposed) | Backing column | REST/OpenAPI type | GraphQL type | Notes |
|----------------------|----------------|-------------------|--------------|-------|
| `id` | `id` | `integer` (`int32`) | `Int!` | PK; non-null on output, omitted on create input. |
| `metadata` | `metadata` | `string` (no `format`) | `String` (nullable) | The JSON column; rendered exactly as in any other nullable string column. |

No new GraphQL scalar (FR-013); no new field-level config attributes;
no `dab-config` schema change (FR-014, R11).

---

## No production schema changes

- `schemas/dab.draft.schema.json` — unchanged (R11).
- `src/Config/**` — unchanged.
- `src/Service.Tests/DatabaseSchema-PostgreSql.sql`,
  `DatabaseSchema-MySql.sql`, `DatabaseSchema-DwSql.sql`, and
  CosmosDB schema files — unchanged (FR-012, Principle I).

Any deviation from the above in implementation MUST be flagged in the
Phase 2 plan as a violation of FR-012 and Principle VII.
