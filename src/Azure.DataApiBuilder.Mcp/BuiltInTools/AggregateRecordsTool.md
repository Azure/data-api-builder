# AggregateRecordsTool

MCP tool that computes SQL-level aggregations (COUNT, AVG, SUM, MIN, MAX) on DAB entities. All aggregation is pushed to the database engine — no in-memory computation.

## Class Structure

| Member | Kind | Purpose |
|---|---|---|
| `ToolType` | Property | Returns `ToolType.BuiltIn` for reflection-based discovery. |
| `_validFunctions` | Static field | Allowlist of aggregation functions: count, avg, sum, min, max. |
| `GetToolMetadata()` | Method | Returns the MCP `Tool` descriptor (name, description, JSON input schema). |
| `ExecuteAsync()` | Method | Main entry point — validates input, resolves metadata, authorizes, builds the SQL query via the engine's `IQueryBuilder.Build(SqlQueryStructure)`, executes it, and formats the response. |
| `ComputeAlias()` | Static method | Produces the result column alias: `"count"` for count(\*), otherwise `"{function}_{field}"`. |
| `DecodeCursorOffset()` | Static method | Decodes a base64 opaque cursor string to an integer offset for OFFSET/FETCH pagination. Returns 0 on any invalid input. |
| `BuildPaginatedResponse()` | Private method | Formats a grouped result set into `{ items, endCursor, hasNextPage }` when `first` is provided. |
| `BuildSimpleResponse()` | Private method | Formats a scalar or grouped result set without pagination. |

## ExecuteAsync Sequence

```mermaid
sequenceDiagram
    participant Model as LLM / MCP Client
    participant Tool as AggregateRecordsTool
    participant Config as RuntimeConfigProvider
    participant Meta as ISqlMetadataProvider
    participant Auth as IAuthorizationService
    participant QB as IQueryBuilder (engine)
    participant QE as IQueryExecutor
    participant DB as Database

    Model->>Tool: ExecuteAsync(arguments, serviceProvider, cancellationToken)

    Note over Tool: 1. Input validation
    Tool->>Config: GetConfig()
    Config-->>Tool: RuntimeConfig
    Tool->>Tool: Validate tool enabled (runtime + entity level)
    Tool->>Tool: Parse & validate arguments (entity, function, field, distinct, filter, groupby, having, first, after)

    Note over Tool: 2. Metadata resolution
    Tool->>Meta: TryResolveMetadata(entityName)
    Meta-->>Tool: sqlMetadataProvider, dbObject, dataSourceName

    Note over Tool: 3. Early field validation
    Tool->>Meta: TryGetBackingColumn(entityName, field)
    Meta-->>Tool: backingColumn (or FieldNotFound error)
    loop Each groupby field
        Tool->>Meta: TryGetBackingColumn(entityName, groupbyField)
        Meta-->>Tool: backingColumn (or FieldNotFound error)
    end

    Note over Tool: 4. Authorization
    Tool->>Auth: AuthorizeAsync(user, FindRequestContext, ColumnsPermissionsRequirement)
    Auth-->>Tool: AuthorizationResult

    Note over Tool: 5. Build SqlQueryStructure
    Tool->>Tool: Create SqlQueryStructure from FindRequestContext
    Tool->>Tool: Populate GroupByMetadata (fields, AggregationColumn, HAVING predicates)
    Tool->>Tool: Clear default columns/OrderBy, set aggregation flag

    Note over Tool: 6. Generate SQL via engine
    Tool->>QB: Build(SqlQueryStructure)
    QB-->>Tool: SQL string (SELECT ... GROUP BY ... HAVING ... FOR JSON PATH)

    Note over Tool: 7. Post-process SQL
    Tool->>Tool: Insert ORDER BY aggregate expression before FOR JSON PATH
    opt Pagination (first provided)
        Tool->>Tool: Remove TOP N (conflicts with OFFSET/FETCH)
        Tool->>Tool: Append OFFSET/FETCH NEXT
    end

    Note over Tool: 8. Execute query
    Tool->>QE: ExecuteQueryAsync(sql, parameters, GetJsonResultAsync, dataSourceName)
    QE->>DB: Execute SQL
    DB-->>QE: JSON result
    QE-->>Tool: JsonDocument

    Note over Tool: 9. Format response
    alt first provided (paginated)
        Tool->>Tool: BuildPaginatedResponse(resultArray, first, after)
        Tool-->>Model: { items, endCursor, hasNextPage }
    else simple
        Tool->>Tool: BuildSimpleResponse(resultArray, alias)
        Tool-->>Model: { entity, result: [{alias: value}] }
    end

    Note over Tool: Exception handling
    alt TimeoutException
        Tool-->>Model: TimeoutError — "query timed out, narrow filters or paginate"
    else TaskCanceledException
        Tool-->>Model: TimeoutError — "canceled, likely timeout"
    else OperationCanceledException
        Tool-->>Model: OperationCanceled — "interrupted, retry"
    else DbException
        Tool-->>Model: DatabaseOperationFailed
    end
```

## Key Design Decisions

- **No in-memory aggregation.** The engine's `GroupByMetadata` / `AggregationColumn` types drive SQL generation via `queryBuilder.Build(structure)`.
- **COUNT(\*) workaround.** The engine's `Build(AggregationColumn)` doesn't support `*` as a column name, so the primary key column is used instead (`COUNT(pk)` ≡ `COUNT(*)` since PK is NOT NULL).
- **ORDER BY aggregate.** Neither the GraphQL nor REST paths support ORDER BY on an aggregate expression, so the tool post-processes the generated SQL to insert it before `FOR JSON PATH`.
- **TOP vs OFFSET/FETCH.** SQL Server forbids both in the same query. When pagination is used, `TOP N` is stripped via regex.
- **Database support.** Only MsSql / DWSQL — matches the engine's GraphQL aggregation support. PostgreSQL, MySQL, and CosmosDB return an `UnsupportedDatabase` error.
