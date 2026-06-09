# MCP Contract: JSON Column Surface

**Scope**: How SQL Server `JSON` columns appear in MCP tool schemas.

Per the 2026-06-09 Clarifications session in [spec.md](../spec.md), a
JSON column is treated exactly like a plain string column at the MCP
layer. **No JSON-specific annotation, description, or `format` is
added** to any MCP tool. This document pins that "no annotation"
contract so future implementations do not accidentally introduce
JSON-specific behavior.

This document does not change CRUD execution paths. MCP tools route
through the same `IQueryEngine` / `IMutationEngine` / `IQueryExecutor`
used by REST and GraphQL.

---

## Governing principle

A SQL Server `JSON` column appears in every MCP tool schema
indistinguishably from an `nvarchar(max)` column whose CLR type maps
to `string`. The column's `type` is `"string"`; there is no
`description`, no `format`, no `x-dab-*` extension, no other
JSON-specific property.

Rationale (per FR-009 and FR-017 as updated 2026-06-09):

- DAB does not maintain JSON-specific behavior anywhere. The MCP
  surface follows the same rule.
- When SQL Server adds richer JSON support in future releases, no MCP
  tool schema needs to be updated.
- Agents that handle string columns correctly handle JSON columns
  correctly with no additional learning.

---

## `describe_entities` output

For a field whose underlying `ColumnDefinition.SqlDbType ==
SqlDbType.Json`, the per-field metadata is identical to a string
column. No `description` property is added by this feature.

**Example response excerpt for the `Profile` entity** (`metadata`
column is JSON):

```json
{
  "entities": [
    {
      "name": "Profile",
      "type": "Table",
      "fields": [
        { "name": "id",       "type": "Int32",  "nullable": false, "isPrimaryKey": true },
        { "name": "metadata", "type": "String", "nullable": true,  "isPrimaryKey": false }
      ],
      "permissions": [ /* ... */ ]
    }
  ]
}
```

Integration tests MUST assert the `metadata` field carries no
JSON-specific annotation (no `description`, no `format`) in
`describe_entities` output. The assertion guards against accidental
future divergence.

---

## `dynamic_custom_tool` (per-SP) input schema

For a stored-procedure parameter whose `SqlDbType` is `Json`, the
per-parameter schema is identical to a `string`-typed parameter. No
JSON-specific `description` is added.

**Example** — SP with one JSON-typed parameter `@payload`:

```json
{
  "type": "object",
  "properties": {
    "payload": {
      "type": "string"
    }
  }
}
```

Any generic per-parameter description the existing
`BuildInputSchemaFromDbMetadata` already attaches (for example
`"Parameter @payload"`) is preserved unchanged. No JSON-specific
substitution is performed.

---

## `create_record` and `update_record` input schemas

Unchanged by this feature. These tools already emit a coarse `data:
{ type: "object" }` schema; per-column slots are not introduced.

---

## What MCP error responses look like

MCP tools surface server errors through the same DAB error envelope as
REST and GraphQL. When a tool invocation triggers a SQL Server JSON
error (for example, malformed JSON text passed to a write tool, or a
filter predicate SQL Server cannot evaluate against a JSON column),
the tool response includes the SQL Server error number, consistent
with FR-007. No MCP-specific error mapping is added.

---

## Files changed by this feature in the MCP namespace

None. `src/Azure.DataApiBuilder.Mcp/**` is **not modified** by this
feature.

The previous design (2026-06-04) added a canonical description to
`DescribeEntitiesTool` and `DynamicCustomTool`. That design was
superseded by the 2026-06-09 Clarifications session; see
[research.md §R5](../research.md) for the audit trail.
