# Extending DAB with a Custom Database Executor

Data API builder (DAB) natively supports SQL Server compatible databases, PostgreSQL, MySQL, and Cosmos DB.
This guide shows how to create a custom `QueryExecutor` for a database engine that DAB does not support out of the box (for example, Oracle).

## Prerequisites

- Familiarity with C# generics and nullable reference types.
- The [Oracle managed driver NuGet package](https://www.nuget.org/packages/Oracle.ManagedDataAccess.Core)
  (or the equivalent driver for your database).

---

## 1. Project setup

Create a new class-library project and reference the `Azure.DataApiBuilder.Core` package.

In your `.csproj`, enable **nullable reference types** — this is required because the
`QueryExecutor<TConnection>` base class uses `TResult?` on unconstrained generic type
parameters:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

---

## 2. Implement `OracleQueryExecutor`

Inherit from `QueryExecutor<TConnection>`, supplying your concrete connection type as
the generic argument:

```csharp
using System.Data.Common;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

public class OracleQueryExecutor : QueryExecutor<OracleConnection>
{
    public OracleQueryExecutor(
        DbExceptionParser dbExceptionParser,
        ILogger<IQueryExecutor> logger,
        RuntimeConfigProvider configProvider,
        IHttpContextAccessor httpContextAccessor,
        HotReloadEventHandler<HotReloadEventArgs>? handler)
        : base(dbExceptionParser, logger, configProvider, httpContextAccessor, handler)
    {
    }

    // Override only the methods that need Oracle-specific behavior.
    // All other virtual methods (CreateConnection, PrepareDbCommand, etc.)
    // fall back to the base-class implementations.

    /// <inheritdoc/>
    public override async Task<TResult?> ExecuteQueryAsync<TResult>(
        string sqltext,
        IDictionary<string, DbConnectionParam> parameters,
        Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
        string dataSourceName,
        HttpContext? httpContext = null,
        List<string>? args = null)
    {
        // Add Oracle-specific pre/post processing here if needed.
        return await base.ExecuteQueryAsync(
            sqltext,
            parameters,
            dataReaderHandler,
            dataSourceName,
            httpContext,
            args);
    }
}
```

---

## 3. Common pitfalls

### Missing generic type argument

```csharp
// ❌ WRONG — "QueryExecutor" without a type argument is an open generic type
//            and cannot be used as a base class directly.
public class OracleQueryExecutor : QueryExecutor

// ✅ CORRECT — provide the concrete connection type
public class OracleQueryExecutor : QueryExecutor<OracleConnection>
```

### Missing `<TResult>` on the override method name

```csharp
// ❌ WRONG — "ExecuteQueryAsync(" without <TResult> declares a new non-generic
//            method; it does not override the base class generic method.
public override async Task<TResult?> ExecuteQueryAsync(

// ✅ CORRECT — include <TResult> so the compiler matches the base signature
public override async Task<TResult?> ExecuteQueryAsync<TResult>(
```

### Missing `<Nullable>enable</Nullable>` in the project file

Without this setting, `TResult?` on an unconstrained generic type parameter is not
valid C# syntax and produces:

```text
Cannot implicitly convert type 'TResult' to 'TResult?'.
An explicit conversion exists (are you missing a cast?)
```

Add `<Nullable>enable</Nullable>` to the `<PropertyGroup>` of your `.csproj` file.

---

## 4. Registering the custom executor

Wire up your executor in the dependency-injection container during startup, replacing
the default executor for your data source type:

```csharp
services.AddSingleton<IQueryExecutor, OracleQueryExecutor>();
```

---

## 5. Supported override points

`QueryExecutor<TConnection>` exposes the following `virtual` members that you can
override to customize behavior:

| Member | Description |
|---|---|
| `CreateConnection(string)` | Create and return a new database connection. |
| `ExecuteQueryAsync<TResult>(...)` | Async query execution with retry. |
| `ExecuteQuery<TResult>(...)` | Sync query execution with retry. |
| `ExecuteQueryAgainstDbAsync<TResult>(...)` | Async execution against an open connection. |
| `ExecuteQueryAgainstDb<TResult>(...)` | Sync execution against an open connection. |
| `PrepareDbCommand(...)` | Build the `DbCommand` from SQL text and parameters. |
| `GetSessionParamsQuery(...)` | Return optional session-context SQL to prepend. |
| `PopulateDbTypeForParameter(...)` | Set the `DbType` on a `DbParameter`. |
| `SetManagedIdentityAccessTokenIfAnyAsync(...)` | Attach a Managed Identity token to the connection. |
| `SetManagedIdentityAccessTokenIfAny(...)` | Sync variant of the above. |
| `GetMultipleResultSetsIfAnyAsync(...)` | Process multiple result sets from a reader. |
| `ConnectionStringBuilders` | Dictionary of data-source-name → connection-string builder. |
