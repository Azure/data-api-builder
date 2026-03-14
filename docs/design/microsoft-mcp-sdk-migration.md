# Microsoft MCP SDK Migration Tracker

## Objective

Replace the community MCP SDK (`ModelContextProtocol` 0.3.0-preview.4) with Microsoft's official `Microsoft.ModelContextProtocol.HttpServer` (0.1.0-preview.25) for Entra ID authentication, MISE integration, rate limiting, and OpenTelemetry support.

## Background

DAB exposes MCP tools via two transports:
- **HTTP/SSE** — MCP SDK + ASP.NET Core middleware
- **Stdio** — hand-rolled JSON-RPC server (`McpStdioServer`), independent of the SDK

Tools are registered dynamically via `IMcpTool` + assembly scanning + config-driven factory. Custom `ListToolsHandler` / `CallToolHandler` delegates dispatch to `McpToolRegistry`.

## Analysis

### Package Architecture
`Microsoft.ModelContextProtocol.HttpServer` (0.1.0-preview.25) wraps:
- `ModelContextProtocol` 0.5.0-preview.1 (core protocol types)
- `ModelContextProtocol.AspNetCore` 0.5.0-preview.1 (ASP.NET Core transport)

Three entry-point extension methods:
- `AddMicrosoftMcpServer(IConfiguration, Action<MicrosoftMcpServerOptions>)` — service registration
- `UseMicrosoftMcpServer()` — middleware (auth, MISE, rate limiting)
- `MapMicrosoftMcpServer(path)` — endpoint mapping with authorization

Protocol types (`Tool`, `CallToolResult`, `TextContentBlock`, etc.) remain in `ModelContextProtocol.Protocol` / `ModelContextProtocol.Server` namespaces.

### Breaking Changes (0.3.0 → 0.5.0)

| Change | Impact |
|--------|--------|
| `TextContentBlock.Type` now read-only (defaults to `"text"`) | Removed explicit `Type = "text"` assignments |
| `ToolsCapability` lost handler properties | Moved to `.WithListToolsHandler()` / `.WithCallToolHandler()` on `IMcpServerBuilder` |
| `MicrosoftMcpServerOptions` differs from `McpServerOptions` | `ServerInfo`/`Capabilities` moved to `Configure<McpServerOptions>()` |
| OpenTelemetry 1.9.0 → 1.12.0+ (transitive) | `SetStatus(Status.Error)` → `SetStatus(ActivityStatusCode.Error)`, `RecordException` → `AddException` |
| `Microsoft.Extensions.Configuration.Binder` 8.0.2 → 9.0.0 (transitive) | Required by OpenTelemetry 1.13.0 dependency chain |

### NuGet Feed
Package hosted on `EngThrive-MCP` ADO feed. Required `<packageSourceMapping>` in `Nuget.config`.

## Changes Made

### Phase 1 — Package Swap & Build Fix

| File | Change |
|------|--------|
| `src/Directory.Packages.props` | Replaced `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (0.3.0-preview.4) → `Microsoft.ModelContextProtocol.HttpServer` (0.1.0-preview.25). Bumped transitive deps: `OpenTelemetry.Extensions.Hosting` (1.9.0→1.13.0), `OpenTelemetry.Instrumentation.AspNetCore` (1.9.0→1.12.0), `OpenTelemetry.Instrumentation.Http` (1.9.0→1.12.0), `Microsoft.Extensions.Configuration.Binder` (8.0.2→9.0.0). |
| `src/Azure.DataApiBuilder.Mcp/Azure.DataApiBuilder.Mcp.csproj` | Replaced two `PackageReference` entries with single `Microsoft.ModelContextProtocol.HttpServer`. |
| `Nuget.config` | Added `EngThrive-MCP` feed source and `<packageSourceMapping>` entries. |
| `src/Azure.DataApiBuilder.Mcp/Utils/McpResponseBuilder.cs` | Removed read-only `Type = "text"` assignments on `TextContentBlock` (2 occurrences). |
| `src/Service.Tests/UnitTests/McpTelemetryTests.cs` | Removed `Type = "text"` from `TextContentBlock` construction. |
| `src/Core/Telemetry/TelemetryTracesHelper.cs` | Replaced deprecated OTel APIs: `SetStatus(Status.Error.WithDescription())` → `SetStatus(ActivityStatusCode.Error, msg)`, `RecordException()` → `AddException()`. |

### Phase 2 — Adopt Microsoft MCP API

| File | Change |
|------|--------|
| `src/Azure.DataApiBuilder.Mcp/Core/McpServerConfiguration.cs` | `AddMcpServer()` → `AddMicrosoftMcpServer(configuration, ...)`. `ResourceHost` set on `MicrosoftMcpServerOptions`; `ServerInfo`/`Capabilities` moved to `Configure<McpServerOptions>()`. |
| `src/Azure.DataApiBuilder.Mcp/Core/McpServiceCollectionExtensions.cs` | `AddDabMcpServer` gained `IConfiguration configuration` parameter, forwarded to `ConfigureMcpServer`. |
| `src/Azure.DataApiBuilder.Mcp/Core/McpEndpointRouteBuilderExtensions.cs` | `MapMcp(mcpPath)` → `MapMicrosoftMcpServer(mcpPath)`. |
| `src/Service/Startup.cs` | Passes `Configuration` to `AddDabMcpServer`. Added `app.UseMicrosoftMcpServer()` after auth middleware. |

### Phase 3 — Conditional Auth

`AddMicrosoftMcpServer()` unconditionally registers MISE, which requires `AzureAd:ClientId`. Without it, DAB crashes with `MiseConfigurationException: MISE12031: clientId cannot be empty or null`.

**Solution**: When `AzureAd:ClientId` is present → full Microsoft MCP pipeline (Entra ID, MISE, rate limiting). When absent → base `AddMcpServer()` pipeline (no auth). Both share the same tool handlers and HTTP transport.

| File | Change |
|------|--------|
| `src/Azure.DataApiBuilder.Mcp/Core/McpServerConfiguration.cs` | Added `IsEntraIdConfigured(IConfiguration)`. Conditional branching: `AddMicrosoftMcpServer` vs `AddMcpServer`. |
| `src/Azure.DataApiBuilder.Mcp/Core/McpServiceCollectionExtensions.cs` | Public `IsEntraIdConfigured` method for use by `Startup.cs`. |
| `src/Azure.DataApiBuilder.Mcp/Core/McpEndpointRouteBuilderExtensions.cs` | Conditional `MapMicrosoftMcpServer` vs `MapMcp`. |
| `src/Service/Startup.cs` | Conditional `app.UseMicrosoftMcpServer()` only when Entra ID is configured. |

### Phase 4 — Stdio Idempotent Registration Fix

`McpToolRegistryInitializer` (hosted service) registers all tools during `host.Start()`. In stdio mode, `McpStdioHelper.RunMcpStdioHost()` also registers tools after `host.Start()`, causing duplicate registration errors.

| File | Change |
|------|--------|
| `src/Azure.DataApiBuilder.Mcp/Core/McpToolRegistry.cs` | `RegisterTool()` now skips silently when the same tool instance (by reference) is already registered. Different tool instances with the same name still throw. |

### Test Config Changes (Local Testing Only)

| File | Change |
|------|--------|
| `src/Service.Tests/dab-config.MsSql.json` | Added `TrustServerCertificate=True` and switched to Windows auth for local dev. Added `description` and `fields` metadata to Publisher, Book, Stock, Author entities. Added `"mcp": { "custom-tool": true }` to GetBooks and UpdateBookTitle stored procedures. |

### Files NOT Changed
- All 6 built-in tools (`CreateRecordTool`, `ReadRecordsTool`, `UpdateRecordTool`, `DeleteRecordTool`, `DescribeEntitiesTool`, `ExecuteEntityTool`)
- `DynamicCustomTool.cs`, `CustomMcpToolFactory.cs`
- `McpToolRegistryInitializer.cs`
- `McpStdioServer.cs`, `McpStdioHelper.cs`, `IMcpStdioServer.cs`
- `IMcpTool.cs`
- All Utils (`McpAuthHelper.cs`, `McpErrorHelpers.cs`, `McpJsonHelper.cs`, `McpTelemetryHelper.cs`)

## Validation

### Build
- 0 errors, 0 warnings

### Unit Tests (77 MCP-related — all pass)

| Test Suite | Count | Result |
|---|---|---|
| McpToolRegistryTests | 9 | Pass |
| EntityLevelDmlToolConfigurationTests | 4 | Pass |
| DynamicCustomToolTests | 9 | Pass |
| DescribeEntitiesFilteringTests | 10 | Pass |
| CustomMcpToolFactoryTests | 3 | Pass |
| McpTelemetryTests | 9 | Pass |
| EntityMcpConfigurationTests | 11 | Pass |
| McpRuntimeOptionsSerializationTests | 6 | Pass |
| CLI MCP tests (Add/Update Entity) | 12 | Pass |
| CLI ConfigureOptions (MCP) | 1 | Pass |

### Integration Tests (HTTP + Stdio — All Pass)

| Test | Result |
|------|--------|
| DAB startup (dev mode, no Entra ID) | Starts, no MISE crash |
| MCP initialize (`POST /mcp`) | Server info returned |
| MCP tools/list | All tools listed |
| MCP tools/call (read_records) | Returns database rows |
| Stdio initialize | Correct JSON-RPC response |
| Stdio CRUD (describe, read, filter, create, update, delete) | All operations succeed |
| Stdio shutdown | Clean shutdown |

## Next Steps

- [ ] Entra ID end-to-end test with `AzureAd:ClientId` + `AzureAd:TenantId`
- [ ] Make `ResourceHost` configurable from DAB runtime config
- [ ] Evaluate rate limiting defaults (`MaxRequestsPerMinutePerUser = 100`)
- [ ] MCP Inspector validation for SSE transport
- [ ] CI pipeline access to EngThrive-MCP NuGet feed
