# Semantic Model Integration Architecture

## Overview

This document describes the architecture for Semantic Model (Analysis Services / Power BI / Microsoft Fabric) support in Data API Builder. Semantic models are read-only analytical data sources that use DAX (Data Analysis Expressions) as their query language and are accessed via ADOMD.NET through XMLA endpoints.

DAB exposes semantic model tables as REST and GraphQL endpoints, translating incoming API requests into DAX queries, executing them against the XMLA endpoint, and returning JSON results.

## Supported Backends

| Backend | Connection Method |
|---------|------------------|
| Power BI Desktop (local) | `localhost:<port>` (Analysis Services sidecar process) |
| Power BI Service (cloud) | XMLA endpoint with Entra ID authentication |
| Microsoft Fabric | XMLA endpoint with Entra ID authentication |
| Azure Analysis Services | Server address with Entra ID or connection string auth |
| SQL Server Analysis Services | Server address with Windows or connection string auth |

## Design Constraints

- **Read-only**: Semantic models do not support INSERT, UPDATE, or DELETE. All mutations are rejected.
- **No primary keys**: Semantic model tables have no primary key concept. This means no by-PK GraphQL queries and no cursor-based pagination.
- **No parameterized queries**: DAX does not support parameterized queries the way SQL does. All values are inlined in the DAX text.
- **ADOMD.NET quirks**: `AdomdDataReader` does not extend `DbDataReader`, `AdomdConnection` has no `OpenAsync()`, and `AdomdException` does not extend `DbException`. These require custom handling that bypasses DAB's standard `DbDataReader`-based pipeline.
- **Column name bracket notation**: ADOMD.NET returns column names with DAX table prefixes (e.g., `'customer'[CustomerName]`). These must be stripped to produce clean JSON property names.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                        API Clients                          │
│                    (REST / GraphQL)                          │
└───────────────┬──────────────────────┬──────────────────────┘
                │                      │
     ┌──────────▼──────────┐ ┌────────▼─────────────┐
     │   REST Controller   │ │   HotChocolate GQL    │
     │   (FindRequest      │ │   (IMiddlewareContext) │
     │    Context)          │ │                       │
     └──────────┬──────────┘ └────────┬──────────────┘
                │                      │
        ┌───────▼──────────────────────▼───────┐
        │       SemanticModelQueryEngine        │
        │  ┌─────────────────────────────────┐  │
        │  │  1. Resolve entity name         │  │
        │  │  2. Apply $filter → DAX filter  │  │
        │  │  3. Apply $orderby → ORDER BY   │  │
        │  │  4. Apply $select → SELECTCOLS  │  │
        │  │  5. Apply pagination → TOPN     │  │
        │  └──────────────┬──────────────────┘  │
        └─────────────────┼─────────────────────┘
                          │
              ┌───────────▼────────────┐
              │    DaxQueryStructure    │
              │  (intermediate repr.)   │
              └───────────┬────────────┘
                          │
              ┌───────────▼────────────┐
              │     DaxQueryBuilder     │
              │  → DAX EVALUATE text    │
              └───────────┬────────────┘
                          │
              ┌───────────▼────────────┐
              │ SemanticModelQuery-     │
              │   Executor              │
              │  (ADOMD.NET client)     │
              └───────────┬────────────┘
                          │
              ┌───────────▼────────────┐
              │   XMLA / AS Endpoint   │
              │  (Power BI / Fabric /  │
              │   Analysis Services)   │
              └────────────────────────┘
```

## Component Reference

### New Files

| File | Purpose |
|------|---------|
| `src/Core/Services/MetadataProviders/SemanticModelMetadataProvider.cs` | Schema discovery via ADOMD.NET DMVs; OData model building |
| `src/Core/Resolvers/SemanticModelQueryEngine.cs` | REST/GraphQL request → DAX query orchestration |
| `src/Core/Resolvers/DaxQueryBuilder.cs` | `DaxQueryStructure` → DAX text generation |
| `src/Core/Resolvers/DaxQueryStructure.cs` | Intermediate query representation |
| `src/Core/Resolvers/SemanticModelQueryExecutor.cs` | ADOMD.NET query execution and result reading |
| `src/Core/Resolvers/SemanticModelMutationEngine.cs` | Read-only stub; rejects all mutations |
| `src/Core/Resolvers/SemanticModelDbExceptionParser.cs` | ADOMD exception classification |
| `src/Core/Parsers/DaxODataASTVisitor.cs` | OData filter AST → DAX filter expression translator |

### Modified Files

| File | Change |
|------|--------|
| `src/Config/ObjectModel/DatabaseType.cs` | Added `SemanticModel` enum value |
| `src/Config/ObjectModel/DataSource.cs` | Added `SemanticModelOptions` record |
| `src/Config/ObjectModel/RuntimeConfig.cs` | Added `SemanticModelDataSourceUsed` property |
| `src/Core/Resolvers/Factories/QueryEngineFactory.cs` | Registers `SemanticModelQueryEngine` |
| `src/Core/Resolvers/Factories/MutationEngineFactory.cs` | Registers `SemanticModelMutationEngine` |
| `src/Core/Resolvers/Factories/QueryManagerFactory.cs` | Registers executor and exception parser |
| `src/Core/Services/MetadataProviders/MetadataProviderFactory.cs` | Registers `SemanticModelMetadataProvider` |
| `src/Service.GraphQLBuilder/Queries/QueryBuilder.cs` | Skips by-PK query generation for SemanticModel |
| `src/Service.GraphQLBuilder/Mutations/MutationBuilder.cs` | Skips mutation generation for SemanticModel |
| `src/Service/HealthCheck/HealthCheckHelper.cs` | ADOMD.NET health check path |
| `src/Service/Startup.cs` | Excludes SemanticModel from OpenAPI generation |
| `src/Core/Configurations/RuntimeConfigValidator.cs` | SemanticModel validation rules |
| `src/Cli/ConfigGenerator.cs` | CLI `init` support for `semanticmodel` database type |
| `schemas/dab.draft.schema.json` | `semanticmodel` in database-type enum |
| `src/Directory.Packages.props` | ADOMD.NET NuGet package |

## Schema Discovery

At startup, `SemanticModelMetadataProvider.InitializeAsync()` connects to the XMLA endpoint and discovers column metadata using Tabular Model (TOM) DMVs:

```
Step 1: SELECT * FROM $SYSTEM.TMSCHEMA_TABLES
        → Maps table Name to internal TableID

Step 2: SELECT * FROM $SYSTEM.TMSCHEMA_COLUMNS
        → Reads column Name, ExplicitDataType, IsNullable per TableID
        → Skips Type=3 columns (auto-generated RowNumber columns)

Step 3: Correlate by TableID to build table → column metadata
```

**Why TMSCHEMA and not DBSCHEMA?** Power BI Desktop's `DBSCHEMA_COLUMNS` DMV reports all columns as `DBTYPE_WSTR` (string), regardless of actual type. `TMSCHEMA_COLUMNS` provides the real TOM data types.

### TOM Data Type Mapping

| TOM Code | DAX Type | .NET Type |
|----------|----------|-----------|
| 2 | String | `System.String` |
| 6 | Int64 | `System.Int64` |
| 8 | Double | `System.Double` |
| 9 | DateTime | `System.DateTime` |
| 10 | Decimal | `System.Decimal` |
| 11 | Boolean | `System.Boolean` |
| 17 | Binary | `System.Byte[]` |

After column discovery, the provider:

1. Builds `SourceDefinition` and `DatabaseObject` entries for each configured entity
2. Registers REST path → entity name mappings for REST routing
3. Registers entity → data source name mappings
4. Constructs the OData EDM model (via `ODataParser.BuildModel`) so that `$filter` and `$orderby` query parameters work

## DAX Query Generation

`DaxQueryBuilder` translates a `DaxQueryStructure` into DAX text. The intermediate representation has these fields:

| Field | Type | Purpose |
|-------|------|---------|
| `TableName` | `string` | Target table in the semantic model |
| `SelectedColumns` | `List<string>` | Column projection (empty = all columns) |
| `IncludedMeasures` | `Dictionary<string, string>` | Measures to compute (alias → DAX expression) |
| `FilterPredicates` | `List<string>` | DAX filter expressions |
| `OrderByColumns` | `List<(string, bool)>` | Sort columns (name, ascending?) |
| `TopCount` | `int?` | Row limit |

### Generated DAX Patterns

**Basic query (all columns):**
```dax
EVALUATE
'customer'
```

**Column projection:**
```dax
EVALUATE
SELECTCOLUMNS(
    'customer',
    "CustomerID", 'customer'[CustomerID],
    "CustomerName", 'customer'[CustomerName]
)
```

**Filtering (`$filter=CustomerID gt 100`):**
```dax
EVALUATE
SELECTCOLUMNS(
    CALCULATETABLE(
        'customer',
        'customer'[CustomerID] > 100
    ),
    "CustomerID", 'customer'[CustomerID],
    "CustomerName", 'customer'[CustomerName]
)
```

**Pagination and ordering (`$orderby=CustomerID desc`, page size 100):**
```dax
EVALUATE
TOPN(
    100,
    SELECTCOLUMNS(
        'customer',
        "CustomerID", 'customer'[CustomerID],
        "CustomerName", 'customer'[CustomerName]
    ),
    [CustomerID], DESC
)
ORDER BY [CustomerID] DESC
```

**Measures via ADDCOLUMNS:**
```dax
EVALUATE
ADDCOLUMNS(
    SELECTCOLUMNS(
        'customer',
        "CustomerID", 'customer'[CustomerID]
    ),
    "TotalSales", [Total Sales]
)
```

### Identifier Quoting

- **Table names**: Single quotes with `'` escaped as `''` → `'My Table'`
- **Column names**: Square brackets with `]` escaped as `]]` → `[My Column]`

### ORDER BY with SELECTCOLUMNS

When `SELECTCOLUMNS` is used, the output columns lose their table prefix. `ORDER BY` and `TOPN` ordering must use bare `[Column]` references instead of `'table'[Column]`, or DAX raises "A single value for column cannot be determined."

## OData Filter Translation

`DaxODataASTVisitor` walks the OData filter AST (produced by Microsoft's OData URI parser) and generates DAX filter expressions.

| OData | DAX |
|-------|-----|
| `eq` | `=` |
| `ne` | `<>` |
| `gt`, `ge`, `lt`, `le` | `>`, `>=`, `<`, `<=` |
| `and` | `&&` |
| `or` | `\|\|` |
| `not` | `NOT` |
| `null` | `BLANK()` |
| `field eq null` | `ISBLANK('table'[field])` |

**Example:**

```
OData:  $filter=CustomerName eq 'John' and CustomerID gt 100
DAX:    ('customer'[CustomerName] = "John") && ('customer'[CustomerID] > 100)
```

## REST Pipeline Integration

The REST flow is:

1. `RestController` receives GET request (e.g., `/api/customers?$filter=...&$orderby=...&$select=...`)
2. Resolves entity via path-to-entity-name map (registered during metadata init)
3. Builds `FindRequestContext` with parsed OData parameters
4. Calls `SemanticModelQueryEngine.ExecuteAsync(FindRequestContext)`
5. Engine builds `DaxQueryStructure` from the context:
   - `$select` → `SelectedColumns`
   - `$filter` → `FilterPredicates` (via `DaxODataASTVisitor`)
   - `$orderby` → `OrderByColumns`
   - Pagination limit → `TopCount`
6. `DaxQueryBuilder.Build()` generates DAX text
7. `SemanticModelQueryExecutor` executes via ADOMD.NET
8. Results returned as `JsonDocument` (array); REST pipeline adds `{"value": [...]}` wrapper

## GraphQL Pipeline Integration

GraphQL queries flow through HotChocolate's middleware pipeline:

1. `ResolverTypeInterceptor` routes root query fields to `ExecutionHelper.ExecuteQueryAsync`
2. For list queries (e.g., `{ customers { items { ... } } }`):
   - The output type is `CustomerConnection!` (not a list type)
   - `ExecutionHelper` calls `queryEngine.ExecuteAsync(IMiddlewareContext, ...)`
   - Entity name is resolved via `GraphQLUtils.GetEntityNameFromContext()` which reads the `@model` directive on the underlying GraphQL object type
3. `SemanticModelQueryEngine` executes the DAX query and wraps results in a connection object:
   ```json
   { "items": "[{...}, {...}]", "hasNextPage": false }
   ```
4. HotChocolate's `ExecuteListField` resolver processes the `items` field:
   - Reads the serialized JSON array string
   - `SemanticModelQueryEngine.ResolveList` deserializes it into `List<JsonElement>`
5. `ExecuteLeafField` resolvers extract individual scalar values from each item

### Schema Generation

- **List queries generated** (`customers`, `products`): via `QueryBuilder.GenerateGetAllQuery`
- **By-PK queries skipped**: semantic models have no primary keys
- **All mutations skipped**: semantic models are read-only
- **`@model` directive**: maps GraphQL type names to config entity names

GraphQL pagination parameters (`first`, `after`) are handled by the query engine:
- `first` → `DaxQueryStructure.TopCount`
- `after` → ignored (cursor-based pagination not supported)

## ADOMD.NET Execution Layer

`SemanticModelQueryExecutor` implements `IQueryExecutor` but bypasses the standard `DbDataReader`-based result handling because `AdomdDataReader` does not extend `DbDataReader`.

```
┌─────────────────────────────────────────┐
│         ExecuteQueryAsync<T>()          │
│                                         │
│  1. Create AdomdConnection              │
│  2. Task.Run(() => connection.Open())   │
│  3. Create AdomdCommand with DAX text   │
│  4. command.ExecuteReader()             │
│  5. ReadAdomdResultsToJsonArray()       │
│     └─ Iterate FieldCount              │
│     └─ CleanColumnName() per column    │
│     └─ Build JsonArray of JsonObjects  │
│  6. Deserialize to requested type      │
└─────────────────────────────────────────┘
```

### Column Name Cleaning

`CleanColumnName` strips DAX table prefixes and bracket notation:

| Raw (from ADOMD) | Cleaned |
|-------------------|---------|
| `'customer'[CustomerName]` | `CustomerName` |
| `[City]` | `City` |
| `Status` | `Status` |

## Health Check

The health check subsystem has a dedicated ADOMD.NET path because `SemanticModelQueryExecutor` cannot use the standard `DbConnection`-based health check (ADOMD types don't extend ADO.NET base classes).

`HealthCheckHelper.ExecuteAdomdDatasourceQueryCheckAsync()`:
1. Opens a fresh `AdomdConnection`
2. Executes a lightweight DAX probe query (from `DaxHealthCheckQuery` constant)
3. Measures response time against the configured threshold
4. Returns health status with timing metrics

## Configuration

### DAB Config Example

```json
{
  "data-source": {
    "database-type": "semanticmodel",
    "connection-string": "Data Source=localhost:60488"
  },
  "entities": {
    "Customer": {
      "source": { "object": "customer", "type": "table" },
      "graphql": { "enabled": true, "type": { "singular": "Customer", "plural": "Customers" } },
      "rest": { "enabled": true, "path": "/customers" },
      "permissions": [{ "role": "anonymous", "actions": [{ "action": "read" }] }]
    }
  }
}
```

### Validation Rules

`RuntimeConfigValidator` enforces:
- Connection string is required
- Stored procedures are not supported
- Entity source type must be `table`

### Connection Strings by Backend

| Backend | Connection String |
|---------|------------------|
| Power BI Desktop | `Data Source=localhost:<port>` |
| Power BI Service | `Data Source=powerbi://api.powerbi.com/v1.0/myorg/<workspace>;Initial Catalog=<dataset>` |
| Azure Analysis Services | `Data Source=asazure://<region>.asazure.windows.net/<server>` |
| With Entra ID token | Append `Password=<access_token>` or use MSAL token provider |

### Discovering the Power BI Desktop Port

Power BI Desktop runs a local Analysis Services instance (`msmdsrv.exe`) on a dynamic port:

```powershell
Get-NetTCPConnection -State Listen |
  Where-Object { (Get-Process -Id $_.OwningProcess).ProcessName -eq 'msmdsrv' } |
  Select-Object LocalPort
```

## Factory Wiring

The SemanticModel components are conditionally registered in the factory classes when `RuntimeConfig.SemanticModelDataSourceUsed` is true:

| Factory | Registers |
|---------|-----------|
| `QueryEngineFactory` | `SemanticModelQueryEngine` |
| `MutationEngineFactory` | `SemanticModelMutationEngine` |
| `QueryManagerFactory` | `SemanticModelQueryExecutor` + `SemanticModelDbExceptionParser` |
| `MetadataProviderFactory` | `SemanticModelMetadataProvider` |

This follows the same conditional registration pattern used by CosmosDB.

## Known Limitations

| Limitation | Reason |
|------------|--------|
| No cursor-based pagination (`$after`/`endCursor`) | Semantic models lack primary keys for stable cursors |
| No mutations (create/update/delete) | Semantic models are read-only |
| No stored procedure support | DAX has no stored procedure concept |
| No OpenAPI spec generation | Excluded alongside CosmosDB |
| GraphQL field-level selection not pushed down | All columns returned; HotChocolate handles field filtering |

## Measures

Measures are DAX calculations defined in the semantic model. They are the primary
computational unit — the "beating heart" of semantic models.

### Key Properties

- **Context-aware**: The same measure produces different results depending on which entity's
  row context it evaluates in. `[Sales]` on a customer row = that customer's sales; on a
  product row = that product's sales.
- **Model-wide**: Measures can be defined on any table. A measure on a `metrics` table can be
  evaluated in the context of a `customer` table — DAX resolves through relationships.
- **Composable**: Measures can reference other measures (e.g., `[Margin] = [Sales] - [Costs]`).
  DAX resolves the full dependency chain automatically.
- **Virtual**: They don't correspond to stored data. They compute at query time.

### Discovery

Measures are discovered via `$SYSTEM.TMSCHEMA_MEASURES`:

| Field | Purpose |
|-------|---------|
| `Name` | Measure name (used as field name in API) |
| `Expression` | DAX expression (not exposed to API, used internally) |
| `DataType` | Return type (same TOM codes as columns: 2=String, 6=Int64, 8=Double, etc.) |
| `TableID` | Home table (where the measure is defined — informational only) |
| `IsHidden` | Whether the measure is hidden from client tools |

### Configuration

Measures are attached to entities via the `measures` property:

```json
"Sales": {
  "source": { "object": "sales" },
  "measures": ["Sales", "Units", "Margin", "Margin %"]
}
```

- **Explicit list**: Only named measures are exposed on this entity
- **`"*"`**: All non-hidden measures are exposed
- **Omitted**: No measures (columns only)

A measure defined on table A can be exposed on entity B — the config controls this, not the
measure's home table.

### Name Sanitization

Measure names are sanitized to valid GraphQL identifiers (`/^[_A-Za-z][_0-9A-Za-z]*$/`)
using predictable, standard rules. Names that are already valid are kept unchanged.

**Rules (applied in order):**
1. Known symbols → short word (lowercase): `%`→`pct`, `$`→`usd`, `#`→`num`, `&`→`and`, `@`→`at`
2. All remaining non-alphanumeric chars (spaces, parens, hyphens, etc.) → `_`
3. Collapse consecutive `_` to single `_`
4. Trim leading/trailing `_`
5. If starts with digit, prefix with `_`

| Original Name | GraphQL Name |
|--------------|-------------|
| `Sales` | `Sales` (unchanged) |
| `Margin %` | `Margin_pct` |
| `Labor and Overhead Cost` | `Labor_and_Overhead_Cost` |
| `Raw Materials Cost` | `Raw_Materials_Cost` |
| `Sales YoY %` | `Sales_YoY_pct` |
| `Units @ PY ShareOfTotalUnits` | `Units_at_PY_ShareOfTotalUnits` |

The mapping from sanitized name to original name is stored internally so DAX queries use
the original measure reference (e.g., `[Margin %]`).

If a sanitized name collides with an existing column name, the measure is skipped with a warning.

### `@measure` GraphQL Directive

Measure fields in the GraphQL schema are annotated with the `@measure` directive. This allows
codegen tools to distinguish measures from physical columns via introspection:

```graphql
directive @measure on FIELD_DEFINITION

type Customer {
  CustomerID: Long!
  CustomerName: String
  Sales: Float @autoGenerated @measure
  Margin_pct: Float @autoGenerated @measure
  Labor_and_Overhead_Cost: Float @autoGenerated @measure
}
```

The `@measure` directive definition is discoverable via standard GraphQL introspection
(`__schema.directives`). Applied field directives are visible in the schema SDL but not in
standard introspection (a GraphQL spec limitation).

### DAX Generation

When measures are requested (via `$select` or GraphQL field selection), they are wrapped in
`ADDCOLUMNS`:

```dax
EVALUATE
ADDCOLUMNS(
  SELECTCOLUMNS('sales', "ProductID", 'sales'[ProductID], "Amount_USD", 'sales'[Amount_USD]),
  "Sales", [Sales],
  "Margin_pct", [Margin %],
  "Labor_and_Overhead_Cost", [Labor and Overhead Cost]
)
```

The ADDCOLUMNS alias uses the sanitized GraphQL name; the DAX expression uses the original
measure name. ADOMD.NET returns the sanitized name as the column header, matching the API field name.

Measures are always nullable in the API (they may return `BLANK()` in some filter contexts).

## Relationships

Relationships in semantic models are defined in the model itself. They can form any topology:
star schema, snowflake, multi-fact, or arbitrary graphs.

### Key Properties

- **Model-defined**: Relationships exist in the semantic model, not in a database schema.
  They are discovered, not inferred.
- **Topology-agnostic**: Not limited to star schema. Supports snowflake, multi-fact,
  many-to-many, bidirectional filtering, and multiple paths between tables.
- **Active vs. inactive**: Only one relationship between two tables can be active at a time.
  Inactive relationships can be activated in DAX via `USERELATIONSHIP()`.
- **Filter propagation**: DAX automatically propagates filter context through the relationship
  graph. Filtering on `customer.City = 'Seattle'` automatically filters `sales` to that city's
  customers when a customer→sales relationship exists.

### Discovery

Relationships are discovered via `$SYSTEM.TMSCHEMA_RELATIONSHIPS`:

| Field | Purpose |
|-------|---------|
| `FromTableID` / `FromColumnID` | Source side of the relationship |
| `ToTableID` / `ToColumnID` | Target side of the relationship |
| `FromCardinality` | 1=One, 2=Many |
| `ToCardinality` | 1=One, 2=Many |
| `IsActive` | Whether this is the active relationship between these tables |
| `CrossFilteringBehavior` | 1=SingleDirection, 2=BothDirections |

TableID/ColumnID are resolved to names using the ID→name maps from TMSCHEMA_TABLES/COLUMNS.

### Cardinality Mapping

| From | To | DAB Mapping | GraphQL |
|------|----|-------------|---------|
| Many (2) | One (1) | Many-to-One | Single navigation (`sale → customer: Customer`) |
| One (1) | Many (2) | One-to-Many | Collection navigation (`customer → sales: [Sale]`) |
| Many (2) | Many (2) | Many-to-Many | Collection navigation (`product → sales: [Sale]`) |
| One (1) | One (1) | One-to-One | Single navigation |

### DAX Execution Strategy

For Many-to-One relationships (e.g., sale→customer), `RELATED()` within `ADDCOLUMNS` can
inline parent fields in a single query:

```dax
EVALUATE
ADDCOLUMNS(
  'sales',
  "CustomerName", RELATED('customer'[CustomerName])
)
```

For One-to-Many (e.g., customer→sales), a separate query with filter context is used.

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AnalysisServices.AdomdClient.NetCore.retail.amd64` | 19.84.1 | ADOMD.NET client library |

Managed centrally via `src/Directory.Packages.props`.
