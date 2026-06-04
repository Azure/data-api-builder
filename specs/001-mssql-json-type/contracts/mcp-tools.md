# MCP Contract: JSON Column Surface

**Tools affected**: `describe_entities`, `dynamic_custom_tool`
(per-SP), `create_record`, `update_record`.
**Scope**: Per FR-017, how SQL Server `JSON` columns are annotated in
MCP tool schemas so calling LLM agents know to embed valid JSON text.

This document defines the **per-column annotation contract**; it does
NOT add new tool actions, change existing tool schemas beyond
annotation, or alter CRUD execution paths (which route through the
shared engines unchanged).

---

## Annotation string (canonical)

A single shared description string is used wherever a JSON column is
exposed in an MCP tool schema:

```text
JSON-encoded string; embed valid JSON text (e.g., a JSON object or array
serialized as a string). Do not send a nested object.
```

Implementation MAY define this as a constant in
`src/Azure.DataApiBuilder.Mcp/Utils/` and reuse it across the two
emission sites.

No non-standard `format` value is used (Spec Clarification Q5 chose
option B — description only).

---

## `describe_entities` output

For each field whose underlying `ColumnDefinition.SqlDbType ==
SqlDbType.Json`, the per-field metadata MUST include a `description`
property set to the canonical annotation string above. Existing field
metadata properties (name, type, nullable, isPrimaryKey, etc.) are
unchanged.

**Example response excerpt for the `Profile` entity:**

```json
{
  "entities": [
    {
      "name": "Profile",
      "type": "Table",
      "fields": [
        { "name": "id",       "type": "Int32",  "nullable": false, "isPrimaryKey": true },
        {
          "name": "metadata",
          "type": "String",
          "nullable": true,
          "isPrimaryKey": false,
          "description": "JSON-encoded string; embed valid JSON text (e.g., a JSON object or array serialized as a string). Do not send a nested object."
        }
      ],
      "permissions": [ /* ... */ ]
    }
  ]
}
```

This is the **primary annotation surface** — the existing tool
descriptions on `create_record` and `update_record` instruct agents to
call `describe_entities` first, so the annotation reaches the agent
before it constructs the `data` payload.

---

## `dynamic_custom_tool` (per-SP) input schema

`DynamicCustomTool.BuildInputSchemaFromDbMetadata` emits a JSON Schema
property for each stored-procedure parameter. When a parameter's
`SqlDbType` is `Json`, the per-parameter schema MUST include the
canonical annotation as its `description`.

**Example** — SP with one JSON-typed parameter `@payload`:

```json
{
  "type": "object",
  "properties": {
    "payload": {
      "type": "string",
      "description": "JSON-encoded string; embed valid JSON text (e.g., a JSON object or array serialized as a string). Do not send a nested object."
    }
  }
}
```

If the SP-emission path already produces a generic per-parameter
description (e.g., "Parameter @payload"), the JSON-column description
REPLACES it. (Implementation note: the existing
`BuildParameterDescription` helper may be extended, or the JSON-column
case may short-circuit it.)

---

## `create_record` and `update_record` input schemas

These tools currently emit a **coarse** input schema:

```json
{
  "type": "object",
  "properties": {
    "entity": { "type": "string", "description": "Entity name with CREATE permission." },
    "data":   { "type": "object", "description": "Required fields and values for the new record." }
  },
  "required": ["entity", "data"]
}
```

`data` is an opaque `object` — there are no per-column slots in which
to attach a JSON-column annotation. Per R5 (research note), this is
**intentionally not refactored** under FR-017. The contract is:

- The `data` schema shape is **unchanged** by this feature.
- Agents discover JSON columns via `describe_entities`, which annotates
  them per the section above.
- The existing tool descriptions on `create_record` and `update_record`
  already mandate `describe_entities` as STEP 1; no change to those
  descriptions is required, though implementers MAY append a line to
  the `data` description noting that JSON columns expect JSON-encoded
  strings.

If implementation decides to append such a line, the canonical wording
is:

```
Required fields and values for the new record. For fields whose
describe_entities metadata indicates a JSON-encoded string, supply the
value as a string containing valid JSON text — do not send a nested
object.
```

This is OPTIONAL; the FR-017 contract is satisfied by the
`describe_entities` annotation alone.

---

## CRUD execution paths — unchanged

`CreateRecordTool.ExecuteAsync`, `UpdateRecordTool.ExecuteAsync`,
`ReadRecordsTool.ExecuteAsync`, `DeleteRecordTool.ExecuteAsync`,
`AggregateRecordsTool.ExecuteAsync`, and `ExecuteEntityTool.ExecuteAsync`
route through the shared SQL engines (`SqlMutationEngine`,
`SqlQueryEngine`) and therefore inherit all read/write/error behavior
defined by the REST/GraphQL contracts:

- Malformed JSON text → `HTTP 400`-equivalent MCP error result.
- Read responses serialize `metadata` as a JSON string (the underlying
  REST/GraphQL pipeline output).
- NULL handling matches the REST/GraphQL contract.

No MCP-tool-specific code changes are required to achieve the above —
only the annotation additions documented in this contract.
