# GraphQL Contract: JSON Column Surface

**Entity**: `Profile` (table `dbo.profiles`)
**Scope**: Exact GraphQL schema, query, and mutation shapes for SQL
Server `JSON` columns. Reuses the built-in `String` scalar — no new
scalar (FR-013).

---

## Generated SDL (excerpt)

```graphql
type Profile {
  id: Int!
  metadata: String
}

type ProfileConnection {
  items: [Profile!]!
  endCursor: String
  hasNextPage: Boolean!
}

input ProfileFilterInput {
  id: IntFilterInput
  metadata: StringFilterInput
  and: [ProfileFilterInput!]
  or: [ProfileFilterInput!]
}

input ProfileOrderByInput {
  id: OrderBy
  metadata: OrderBy
}

input CreateProfileInput {
  metadata: String
}

input UpdateProfileInput {
  metadata: String
}

extend type Query {
  profile_by_pk(id: Int!): Profile
  profiles(
    first: Int
    after: String
    filter: ProfileFilterInput
    orderBy: ProfileOrderByInput
  ): ProfileConnection!
}

extend type Mutation {
  createProfile(item: CreateProfileInput!): Profile
  updateProfile(id: Int!, item: UpdateProfileInput!): Profile
  deleteProfile(id: Int!): Profile
}
```

`metadata` is `String` (nullable, mirroring the SQL `NULL` column
declaration) in both the output type and the Create/Update input types
(FR-001, FR-003, FR-005). The GraphQL parser/validator therefore
rejects a nested object literal in `item.metadata` automatically (FR-005,
spec Clarification Q1) — no DAB-specific gate is required for this
case.

---

## Queries

### Single by primary key (User Story 2)

```graphql
query {
  profile_by_pk(id: 1) {
    id
    metadata
  }
}
```

```json
{
  "data": {
    "profile_by_pk": {
      "id": 1,
      "metadata": "{\"role\":\"admin\",\"tier\":3}"
    }
  }
}
```

`metadata` is a `String` whose value is the JSON text returned by SQL
Server (FR-003, User Story 2). DAB does not parse it.

### List with filter and orderBy (User Stories 3, 9)

```graphql
query {
  profiles(
    filter: { metadata: { eq: "{\"role\":\"admin\",\"tier\":3}" } }
    orderBy: { metadata: ASC }
  ) {
    items {
      id
      metadata
    }
  }
}
```

All operators that GraphQL's `StringFilterInput` exposes (`eq`, `ne`,
`contains`, `startsWith`, `endsWith`, `gt`, `lt`, `gte`, `lte`,
`isNull`) are forwarded to SQL Server. DAB does **not** maintain a
JSON-specific allow-list (FR-009; 2026-06-09 Clarifications):

| Filter op | DAB behavior | Outcome (current SQL Server JSON support) |
|-----------|--------------|-------------------------------------------|
| `eq` | Forwarded | Succeeds: string compare on stored JSON text |
| `ne` | Forwarded | Succeeds: string compare on stored JSON text |
| `isNull: true` / `isNull: false` (or `eq: null` / `ne: null`) | Forwarded | Succeeds: `IS NULL` / `IS NOT NULL` |
| `contains`, `startsWith`, `endsWith` | Forwarded | Fails with SQL Server error → GraphQL error with `extensions.code = "BAD_REQUEST"` and the SQL error number in the message (FR-007) |
| `gt`, `lt`, `gte`, `lte` | Forwarded | Fails the same way |

This design intentionally avoids hard-coding SQL Server's operator
support matrix. If SQL Server adds support for additional operators
on the native JSON type in a future release, DAB inherits the new
behavior with no code change.

---

## Mutations

### Create (User Story 4)

```graphql
mutation {
  createProfile(item: { metadata: "{\"role\":\"guest\"}" }) {
    id
    metadata
  }
}
```

```json
{
  "data": {
    "createProfile": {
      "id": 6,
      "metadata": "{\"role\":\"guest\"}"
    }
  }
}
```

**Rejected at GraphQL validation** — nested object input:

```graphql
mutation {
  createProfile(item: { metadata: { role: "guest" } }) { id }
}
```

→ GraphQL error: `metadata` is of type `String`; input cannot be an
object literal. (No DAB code path executes — the request is rejected by
Hot Chocolate's input validator.)

### Update (User Story 5)

```graphql
mutation {
  updateProfile(id: 1, item: { metadata: "{\"role\":\"owner\"}" }) {
    id
    metadata
  }
}
```

**Clear to NULL** (User Story 8):

```graphql
mutation {
  updateProfile(id: 1, item: { metadata: null }) {
    id
    metadata
  }
}
```

### Delete (User Story 6)

```graphql
mutation {
  deleteProfile(id: 1) { id }
}
```

Unchanged behavior; JSON column does not affect delete.

---

## Error handling for SQL Server JSON errors on write or filter (User Story 7, FR-007)

When SQL Server returns a JSON-related error on a JSON column —
whether the cause is malformed JSON text on write or an unsupported
filter operator — DAB returns a GraphQL error with
`extensions.code = "BAD_REQUEST"`. The error message contains the SQL
Server error number so the customer can diagnose the cause.

```graphql
mutation {
  createProfile(item: { metadata: "{not valid json" }) { id }
}
```

```json
{
  "errors": [
    {
      "message": "Database error: SQL Server error 13608.",
      "extensions": { "code": "BAD_REQUEST" }
    }
  ]
}
```

The specific error number (and exact message text) depends on which
SQL Server JSON validation rule was violated; the contract is that the
response contains a SQL Server error number from the list
`MsSqlDbExceptionParser.BadRequestExceptionCodes` includes for JSON
validation (currently 13608–13614). DAB does not rewrite the message
beyond the standard envelope.

---

## Introspection contract (User Story 1)

`__type(name: "Profile")` MUST report `metadata` as:

```json
{
  "name": "metadata",
  "type": {
    "kind": "SCALAR",
    "name": "String",
    "ofType": null
  }
}
```

`__type(name: "CreateProfileInput")` and `__type(name: "UpdateProfileInput")`
MUST report `metadata` as nullable `String`. No new scalar type appears
in `__schema.types`.
