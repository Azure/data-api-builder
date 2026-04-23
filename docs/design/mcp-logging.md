# MCP Logging Feature

## Overview

Data API builder supports dynamic log level control when running as an MCP (Model Context Protocol) server. This document describes how logging works in MCP stdio mode and the precedence rules for log level configuration.

## Log Level Precedence

Log levels are controlled with the following precedence (highest to lowest):

| Priority | Source | Description |
|----------|--------|-------------|
| 1 (Highest) | CLI `--LogLevel` flag | Cannot be changed once set |
| 2 | Config `runtime.telemetry.log-level` | Cannot be changed by MCP |
| 3 (Lowest) | MCP `logging/setLevel` | Only works if neither CLI nor config set a level |

### Examples

**CLI Override (Priority 1)**
```bash
dab start --mcp-stdio --LogLevel Error --config dab-config.json
```
- Log level is `Error`
- MCP `logging/setLevel` requests are accepted but ignored
- Config file log level is ignored

**Config Override (Priority 2)**
```json
{
  "runtime": {
    "telemetry": {
      "log-level": {
        "default": "Warning"
      }
    }
  }
}
```
```bash
dab start --mcp-stdio --config dab-config.json
```
- Log level is `Warning` (from config)
- MCP `logging/setLevel` requests are accepted but ignored
- Logs appear on stderr (not suppressed)

**MCP Control (Priority 3)**
```bash
dab start --mcp-stdio --config dab-config.json
```
(with no `log-level` in config)
- Default log level is `None` (for MCP stdio mode)
- Zero output emitted
- MCP client can change level via `logging/setLevel`

### How Config Override Works

1. **CLI reads config file** before starting Service
2. **If config has `log-level` set**: CLI passes `--LogLevel <value> --LogLevelFromConfig` to Service
3. **Service detects source**: Sets `IsConfigOverridden = true`
4. **MCP requests blocked**: `logging/setLevel` accepted but level not changed

## MCP Stdio Mode Behavior

When `--mcp-stdio` is used:

1. **Default Log Level**: `None` (only if no CLI or config override) - ensures zero output
2. **Output Handling** (depends on log level):
   - `LogLevel.None`: Both stdout and stderr → `TextWriter.Null` (zero characters emitted)
   - Other levels: stdout → stderr (logs appear on stderr, JSON-RPC on stdout)
3. **JSON-RPC Channel**: stdout is reserved exclusively for MCP JSON-RPC protocol messages

### Output Redirection Summary

| Log Level | stdout | stderr | Result |
|-----------|--------|--------|--------|
| None | `TextWriter.Null` | `TextWriter.Null` | Zero characters |
| Other | Redirected to stderr | Normal | Logs on stderr |

### Why Zero Output Matters

The MCP stdio transport specification requires:
- stdout = JSON-RPC protocol messages only
- Any other output (even "info" logs) is interpreted as protocol violations or warnings by MCP clients

## logging/setLevel Handler

DAB implements the MCP `logging/setLevel` method to allow clients to dynamically adjust log verbosity.

### Request Format
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

### Supported Levels

| MCP Level | .NET LogLevel |
|-----------|---------------|
| `debug` | Debug |
| `info` | Information |
| `notice` | Information |
| `warning` | Warning |
| `error` | Error |
| `critical` | Critical |
| `alert` | Critical |
| `emergency` | Critical |

### Response Behavior

- **Success**: Log level changed, empty result returned
- **CLI Override Active**: Request accepted silently, level not changed (no error to client)
- **Config Override Active**: Request accepted silently, level not changed (no error to client)
- **Invalid Level**: Request accepted, level not changed

## Implementation Details

### Key Components

1. **DynamicLogLevelProvider** (`src/Service/Telemetry/DynamicLogLevelProvider.cs`)
   - Manages current log level
   - Tracks `IsCliOverridden` and `IsConfigOverridden` flags
   - Provides `UpdateFromMcp()` method with precedence enforcement

2. **McpStdioServer** (`src/Azure.DataApiBuilder.Mcp/Core/McpStdioServer.cs`)
   - Handles `logging/setLevel` JSON-RPC method
   - Routes to `DynamicLogLevelProvider.UpdateFromMcp()`

3. **Program.cs** (`src/Service/Program.cs`)
   - Initializes log level early in startup
   - Redirects console output for MCP stdio mode
   - Configures logging filters

### Console Output Handling

```csharp
// In StartEngine() for MCP stdio mode:
if (initialLogLevel == LogLevel.None)
{
    Console.SetOut(TextWriter.Null);
    Console.SetError(TextWriter.Null);
}
else
{
    Console.SetOut(Console.Error);
}
```

This ensures:
- `LogLevel.None`: Zero characters emitted anywhere
- Other levels: Logs go to stderr, JSON-RPC uses raw stdout via `Console.OpenStandardOutput()`

## VS Code MCP Configuration

### Default (Zero Output)
```json
{
  "servers": {
    "my-dab-server": {
      "command": "dab",
      "args": [
        "start",
        "--mcp-stdio",
        "--config",
        "dab-config.json"
      ]
    }
  }
}
```

### With Explicit Log Level
```json
{
  "servers": {
    "my-dab-server": {
      "command": "dab",
      "args": [
        "start",
        "--mcp-stdio",
        "--LogLevel", "Error",
        "--config",
        "dab-config.json"
      ]
    }
  }
}
```

## Related Issues

- [#3274](https://github.com/Azure/data-api-builder/issues/3274): logging/setLevel Method not found
- [#3275](https://github.com/Azure/data-api-builder/issues/3275): When -mcp-stdio is used, control the output
