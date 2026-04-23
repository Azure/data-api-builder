# PR Description

## Why make this change?

Closes #3274 - MCP Server returns "Method not found: logging/setLevel" error when clients send the standard MCP logging/setLevel request.

## What is this change?

- Added handler for `logging/setLevel` JSON-RPC method in `McpStdioServer.cs`
- Implemented `DynamicLogLevelProvider` with `ILogLevelController` interface to allow MCP to update log levels dynamically
- Added `IsCliOverridden` and `IsConfigOverridden` properties to enforce precedence rules
- Added `HasExplicitLogLevel()` helper to correctly detect when config actually pins a log level (vs just having null values)
- Removed debug `Console.Error.WriteLine` messages that bypassed the logging system

**Precedence (highest to lowest):**
1. CLI `--LogLevel` flag - cannot be changed
2. Config `runtime.telemetry.log-level` - cannot be changed by MCP
3. MCP `logging/setLevel` - only works if neither CLI nor config set a level

If CLI or config set a level, MCP requests are accepted but silently ignored (no error returned per MCP spec).

## How was this tested?

- [x] Unit Tests (`DynamicLogLevelProviderTests` - 5 tests)
- [x] Manual Testing

**Manual Test 1: No override (MCP can change level)**
1. Start MCP server without `--LogLevel` and without config `log-level`
2. MCP sends `logging/setLevel` with `level: info`
3. Result: Log level changes to info

**Manual Test 2: CLI override (MCP blocked)**
1. Start MCP server with `--LogLevel Warning`
2. MCP sends `logging/setLevel` with `level: info`
3. Result: Log level stays at Warning, MCP request accepted silently

**Manual Test 3: Config override (MCP blocked)**
1. Add `"telemetry": { "log-level": { "default": "Warning" } }` to config
2. Start MCP server without `--LogLevel`
3. MCP sends `logging/setLevel` with `level: info`
4. Result: Log level stays at Warning, MCP request accepted silently

**Manual Test 4: Config with null values (MCP can change level)**
1. Add `"telemetry": { "log-level": { "default": null } }` to config
2. Start MCP server without `--LogLevel`
3. MCP sends `logging/setLevel` with `level: info`
4. Result: Log level changes to info (null values don't count as override)

## Sample Request(s)

MCP client sends:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "logging/setLevel",
  "params": {
    "level": "info"
  }
}
```

Server responds with empty result (success per MCP spec) and updates log level if no CLI/config override is active.
