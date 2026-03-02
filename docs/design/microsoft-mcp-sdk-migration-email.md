# Microsoft MCP SDK Migration — Status Update

**From:** DAB Engineering Team
**To:** Data API builder Stakeholders
**Date:** February 20, 2026
**Subject:** Microsoft MCP SDK Migration — Complete

---

Hi team,

We've migrated DAB's MCP layer from the community SDK (`ModelContextProtocol` 0.3.0-preview.4) to Microsoft's official `Microsoft.ModelContextProtocol.HttpServer` (0.1.0-preview.25). This brings Entra ID authentication, MISE integration, rate limiting, and first-party OpenTelemetry support.

## Summary

**12 files changed across 4 phases.** No changes to built-in MCP tools, custom tool factory, stdio server, or tool utilities.

1. **Package Swap** — Replaced two community NuGet packages with single `Microsoft.ModelContextProtocol.HttpServer`. Added `EngThrive-MCP` ADO feed. Bumped transitive deps (OpenTelemetry 1.9→1.12/1.13, Configuration.Binder 8.0→9.0). Fixed breaking API changes.

2. **Microsoft MCP API Adoption** — `AddMcpServer()` → `AddMicrosoftMcpServer()`, `MapMcp()` → `MapMicrosoftMcpServer()`, added `UseMicrosoftMcpServer()` middleware. Custom tool handlers preserved.

3. **Conditional Auth** — `AddMicrosoftMcpServer()` requires `AzureAd:ClientId` for MISE. Added conditional branching: full Microsoft pipeline when Entra ID is configured, base pipeline (no auth) otherwise. DAB works in dev mode without Entra ID config.

4. **Stdio Fix** — Made `McpToolRegistry.RegisterTool()` idempotent for same-instance re-registration to prevent duplicate errors in stdio mode.

## Validation

- **Build:** 0 errors, 0 warnings
- **Unit Tests:** 77 MCP-related tests — all passing
- **Integration (HTTP):** Initialize, tools/list, tools/call — all pass
- **Integration (Stdio):** Full CRUD cycle (describe, read, filter, create, update, delete, shutdown) — all pass

## Outstanding

- Entra ID end-to-end test
- Make `ResourceHost` configurable from DAB runtime config
- Evaluate rate limiting defaults
- MCP Inspector validation for SSE transport
- CI pipeline access to EngThrive-MCP NuGet feed

---

Full details: `docs/design/microsoft-mcp-sdk-migration.md`

Thanks,
DAB Engineering Team
