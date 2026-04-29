# PR Description

> **Note:** This PR includes changes from [PR #3420](https://github.com/Azure/data-api-builder/pull/3420) which has been merged into this branch.

## Why make this change?

Closes #3274 - MCP Server returns "Method not found: logging/setLevel" error when clients send the standard MCP logging/setLevel request.

Closes #3275 - Control output in MCP stdio mode (default to `LogLevel.None`, redirect/suppress console output).

## What is this change?

### MCP `logging/setLevel` Handler
- Added handler for `logging/setLevel` JSON-RPC method in `McpStdioServer.cs`
- Implemented `DynamicLogLevelProvider` with `ILogLevelController` interface to allow MCP to update log levels dynamically
- Added `IsCliOverridden` and `IsConfigOverridden` properties to enforce precedence rules

### Log Level Precedence System
**Precedence (highest to lowest):**
1. **CLI `--LogLevel` flag** - cannot be changed by MCP
2. **Config `runtime.telemetry.log-level`** - cannot be changed by MCP  
3. **MCP `logging/setLevel`** - only works if neither CLI nor config set a level

If CLI or config set a level, MCP requests are accepted but silently ignored (no error returned per MCP spec).

### Early Config Reading for MCP Mode
- Added `TryGetLogLevelFromConfig()` in `Program.cs` to read config file early (before host build)
- This ensures config log level is detected before Console redirect decision
- Console redirect for MCP stdio mode now respects config log level

### CLI Log Level Handling
- Added `Utils.CliLogLevel` property to track the parsed `--LogLevel` value
- CLI's `CustomLoggerProvider` now respects the `--LogLevel` value for its own logging
- Fixed duplicate variable name in `CustomLoggerProvider.cs` (`abbreviation` → `mcpAbbreviation`)

### Config Helpers
- Added `HasExplicitLogLevel()` helper to `RuntimeConfig` to correctly detect when config actually pins a log level
- This properly handles null values in telemetry section (null values don't count as explicit override)

### Code Cleanup
- Removed debug `Console.Error.WriteLine` messages that bypassed the logging system
- Capitalized all MCP tool parameter descriptions (per PR review feedback)
- Clarified comments in various files

## Files Changed

| File | Change |
|------|--------|
| `src/Azure.DataApiBuilder.Mcp/Core/McpStdioServer.cs` | Added `logging/setLevel` handler |
| `src/Service/Telemetry/DynamicLogLevelProvider.cs` | New provider with `ILogLevelController` interface |
| `src/Core/Telemetry/ILogLevelController.cs` | New interface for log level control |
| `src/Service/Program.cs` | Added `TryGetLogLevelFromConfig()`, early config reading |
| `src/Config/ObjectModel/RuntimeConfig.cs` | Added `HasExplicitLogLevel()` helper |
| `src/Cli/Utils.cs` | Added `CliLogLevel` property |
| `src/Cli/Program.cs` | Parse `--LogLevel` value into `Utils.CliLogLevel` |
| `src/Cli/CustomLoggerProvider.cs` | Use `Utils.CliLogLevel`, fix duplicate variable |
| `src/Cli/ConfigGenerator.cs` | Capitalize MCP tool parameter descriptions |
| `src/Service.Tests/UnitTests/DynamicLogLevelProviderTests.cs` | Unit tests for precedence logic |
| `docs/design/mcp-logging.md` | Design documentation |

## How was this tested?

- [x] Unit Tests (`DynamicLogLevelProviderTests` - 5 tests)
- [x] Manual Testing

### Manual Test 1: No override (MCP can change level)
1. Start MCP server without `--LogLevel` and without config `log-level`
2. MCP sends `logging/setLevel` with `level: info`
3. Result: Log level changes to info

### Manual Test 2: CLI override (MCP blocked)
1. Start MCP server with `--LogLevel Warning`
2. MCP sends `logging/setLevel` with `level: info`
3. Result: Log level stays at Warning, MCP request accepted silently

### Manual Test 3: Config override (MCP blocked)
1. Add `"telemetry": { "log-level": { "default": "Warning" } }` to config
2. Start MCP server without `--LogLevel`
3. MCP sends `logging/setLevel` with `level: info`
4. Result: Log level stays at Warning, MCP request accepted silently

### Manual Test 4: Config with null values (MCP can change level)
1. Add `"telemetry": { "log-level": { "default": null } }` to config
2. Start MCP server without `--LogLevel`
3. MCP sends `logging/setLevel` with `level: info`
4. Result: Log level changes to info (null values don't count as override)

### Manual Test 5: CLI `--LogLevel Trace` shows verbose logs
1. Start MCP server with `--LogLevel Trace`
2. Result: Trace/Debug level logs visible in stderr

### Manual Test 6: CLI `--LogLevel None` suppresses all output
1. Start MCP server with `--LogLevel None`
2. Result: Zero output to stderr, stdout clean for JSON-RPC

### Manual Test 7: Config log level respected at startup
1. Add `"telemetry": { "log-level": { "default": "Error" } }` to config
2. Start MCP server without `--LogLevel`
3. Result: Only Error/Critical logs shown (no Info/Debug startup noise)

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
