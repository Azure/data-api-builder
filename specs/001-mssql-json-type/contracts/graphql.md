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

Allowed `metadata` filter operators (per StringFilterInput, but
**restricted by the JSON-column gate**):

| Filter op | Status |
|-----------|--------|
| `eq` | Allowed |
| `ne` | Allowed |
| `isNull: true` / `isNull: false` (or `eq: null` / `ne: null`) | Allowed |
| `contains`, `startsWith`, `endsWith` | **Rejected** — GraphQL error, extension `code: "BAD_REQUEST"` |
| `gt`, `lt`, `gte`, `lte` | **Rejected** — same |

Rejection occurs in the same shared OData visitor that backs the REST
`$filter` (R3), so REST and GraphQL share the gate.

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

## Error handling for malformed JSON on write (User Story 7, FR-007)

```graphql
mutation {
  createProfile(item: { metadata: "{not valid json" }) { id }
}
```

```json
{
  "errors": [
    {
      "message": "The value provided for 'metadata' is not valid JSON.",
      "extensions": { "code": "BAD_REQUEST" }
    }
  ]
}
```

`extensions.code` is `BAD_REQUEST` (spec Clarification Q2). The
underlying SQL Server error (13608/13609/etc., per R4) is suppressed in
production mode.

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
