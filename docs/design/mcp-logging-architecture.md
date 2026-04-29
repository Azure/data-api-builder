# MCP Logging Architecture Guide

This document explains how logging works in Data API builder (DAB) when running as an MCP server. It is designed for engineers reviewing the code or onboarding into the DAB/MCP logging system.

---

## Quick Summary

When DAB runs as an MCP server (`--mcp-stdio`), it must keep `stdout` clean for JSON-RPC protocol messages. All logs must go elsewhere (stderr, null, or MCP notifications). The log level can be controlled from three places with strict precedence rules. By default, MCP mode produces **zero output** until a client requests logging.

---

## Who Controls Logging? (Precedence Rules)

Three layers can set the log level. The first one that sets a level **wins** and blocks all lower layers:

```
┌─────────────────────────────────────────────────────┐
│  1. CLI (Highest Priority)                          │
│     --LogLevel Debug                                │
│     ↓ Blocks everything below                       │
├─────────────────────────────────────────────────────┤
│  2. Config File (Second Priority)                   │
│     runtime.telemetry.log-level.default: "Warning"  │
│     ↓ Blocks MCP control                            │
├─────────────────────────────────────────────────────┤
│  3. MCP Client (Lowest Priority)                    │
│     logging/setLevel → "info"                       │
│     Only works if neither CLI nor config set level  │
└─────────────────────────────────────────────────────┘
```

### Why This Design?

- **CLI wins** because operators deploying DAB need guaranteed control
- **Config wins over MCP** because admins want predictable behavior
- **MCP clients can adjust dynamically** only when given permission (no CLI/config override)

### What Happens When MCP Tries to Change a Locked Level?

The MCP `logging/setLevel` request is **accepted silently** (no error returned to the client), but the level is not changed. This follows the MCP spec philosophy: clients should not fail if the server chooses to ignore a preference.

---

## What is LogLevel.None?

`LogLevel.None` is a special .NET log level that means "emit nothing." In MCP stdio mode:

| Scenario | Default Log Level | Why |
|----------|-------------------|-----|
| MCP mode, no CLI/config override | `None` | Zero output keeps stdout clean |
| MCP mode with `--LogLevel Debug` | `Debug` | User explicitly wants logs |
| Normal web mode | `Error` (then adjusted by config) | Standard web server behavior |

When `LogLevel.None` is active:
- `Console.Out` → `TextWriter.Null`
- `Console.Error` → `TextWriter.Null`
- No bytes are emitted anywhere except JSON-RPC responses

This "silent by default" behavior is intentional. MCP clients that want logs must call `logging/setLevel` to enable them.

---

## How Each Layer Configures Logging

### Layer 1: CLI Arguments

```bash
dab start --mcp-stdio --LogLevel Information --config dab-config.json
```

- Parsed **very early** (before host is built)
- Sets `IsCliOverridden = true` in `DynamicLogLevelProvider`
- All subsequent attempts to change log level are blocked
- Valid values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`
- **Note:** MCP-style levels like `notice` are NOT valid here (they fail parsing)

### Layer 2: Config File

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

- Read early in MCP mode to determine initial log level
- Sets `IsConfigOverridden = true` if a log-level is explicitly set
- Blocks MCP `logging/setLevel` from changing the level
- Can also set namespace-specific levels (see "Namespace-Level Logging" section)

### Layer 3: MCP Client (logging/setLevel)

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "logging/setLevel",
  "params": { "level": "info" }
}
```

- Only works if neither CLI nor config set a level
- Accepts MCP-style levels: `debug`, `info`, `notice`, `warning`, `error`, `critical`, `alert`, `emergency`
- These map to .NET `LogLevel` values (see mapping table below)
- When successful, also enables MCP log notifications

---

## .NET Logging vs MCP Logging

DAB uses two parallel logging systems in MCP mode:

| System | Where Logs Go | Purpose |
|--------|---------------|---------|
| .NET ILogger | stderr (or null) | Standard application logging |
| MCP Notifications | stdout (JSON-RPC) | Streaming logs to MCP clients |

### .NET Logging (Microsoft.Extensions.Logging)

Standard logging that all DAB code uses:

```csharp
_logger.LogInformation("Processing request for entity {Entity}", entityName);
```

In MCP mode, these logs are redirected to stderr (or null if `LogLevel.None`).

### MCP Notifications (notifications/message)

A **separate ILogger provider** (`McpLoggerProvider`) captures log messages and sends them as MCP JSON-RPC notifications:

```json
{"jsonrpc":"2.0","method":"notifications/message","params":{"level":"info","logger":"Azure.DataApiBuilder.Service.RestController","data":"Processing request for entity Book"}}
```

This allows MCP clients (like MCP Inspector) to display logs in real-time.

### How They Interact

Both systems receive the same log events, but output to different destinations:

```
┌─────────────────────┐
│  _logger.LogInfo()  │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│   Logging Pipeline  │
│  (AddFilter based   │
│   on DynamicLevel)  │
└─────────┬───────────┘
          │
    ┌─────┴─────┐
    ▼           ▼
┌─────────┐ ┌──────────────┐
│ Console │ │ McpLogger    │
│ (stderr)│ │ (stdout MCP) │
└─────────┘ └──────────────┘
```

---

## Output Streams Explained

MCP stdio mode uses careful stream management:

| Stream | Normal Mode | MCP Mode (LogLevel.None) | MCP Mode (Other Levels) |
|--------|-------------|--------------------------|-------------------------|
| `stdout` | Logs (console) | `TextWriter.Null` | Reserved for JSON-RPC |
| `stderr` | Errors | `TextWriter.Null` | Application logs |
| JSON-RPC stdout | N/A | Protocol messages only | Protocol messages + log notifications |

### Why stdout is Sacred

The MCP stdio transport protocol specification requires:
- `stdout` carries **only** valid JSON-RPC messages
- Any non-JSON text (like a log line) corrupts the protocol
- Clients interpret corrupted stdout as protocol errors

DAB solves this by:
1. Redirecting `Console.Out` to stderr (or null)
2. Using `Console.OpenStandardOutput()` directly for JSON-RPC (bypasses any redirects)

### The "Raw stdout" Technique

```csharp
// MCP JSON-RPC always uses raw stdout, unaffected by Console.SetOut()
Stream stdout = Console.OpenStandardOutput();
StreamWriter writer = new StreamWriter(stdout) { AutoFlush = true };
writer.WriteLine(jsonRpcResponse);  // Goes to real stdout
```

This ensures JSON-RPC messages are never lost, even when `Console.Out` is redirected.

---

## Why VS Code Copilot Shows Logs Differently

### VS Code Copilot Chat

- Intercepts **stderr** from the spawned DAB process
- Displays stderr content in its Output panel
- Does NOT need MCP `notifications/message` because it owns the process

### MCP Inspector and Other MCP Agents

- Connect via the JSON-RPC protocol only
- Cannot see stderr (they don't own the process)
- **Must** use MCP `notifications/message` to receive logs
- That's why we implemented the `McpLoggerProvider`

### Summary

| Consumer | How They Get Logs | Needs notifications/message? |
|----------|-------------------|------------------------------|
| VS Code (owns process) | Captures stderr directly | No |
| MCP Inspector (remote) | JSON-RPC notifications | Yes |
| Claude Desktop | JSON-RPC notifications | Yes |

---

## Redirecting Logs to Null

### When It Happens

Logs are redirected to `TextWriter.Null` when:
1. Running in MCP stdio mode (`--mcp-stdio`)
2. **AND** log level is `None`
3. **AND** no CLI or config override set a different level

### What Problem It Solves

Even benign startup logs like `"Starting server..."` can corrupt the MCP protocol. By defaulting to `LogLevel.None` with null streams, DAB produces **zero characters** until explicitly asked for logs.

### How to Enable Logs

From highest to lowest priority:

1. **CLI**: `--LogLevel Information` (locks to this level)
2. **Config**: Add `runtime.telemetry.log-level.default: "Warning"` (locks to this level)
3. **MCP Client**: Call `logging/setLevel` with `"info"` (only if CLI/config didn't set level)

### When Null Redirection Changes

When `logging/setLevel` enables logging in MCP mode, DAB:
1. Updates the log level filter
2. **Restores stderr** to the real output stream
3. Enables `McpLogNotificationWriter`

This is done in `HandleSetLogLevel()`:

```csharp
if (updated && isLoggingEnabled)
{
    RestoreStderrIfNeeded();  // Restores Console.Error to real stream
}
notificationWriter.IsEnabled = updated && isLoggingEnabled;
```

---

## Hot Reload and Logging

### What Updates Immediately (Hot Reload Aware)

| Setting | Hot Reload? | Notes |
|---------|-------------|-------|
| Namespace-specific log levels | ✅ Yes | Via `LogLevelInitializer` |
| Default log level in config | ⚠️ Partial | Requires service restart for full effect |
| CLI `--LogLevel` | ❌ No | Fixed at startup |
| MCP `logging/setLevel` | ✅ Yes | Immediate effect |

### How Namespace Hot Reload Works

DAB subscribes to config change events:

```csharp
handler.Subscribe(LOG_LEVEL_INITIALIZER_ON_CONFIG_CHANGE, OnConfigChanged);
```

When config changes, `LogLevelInitializer` re-reads the log level for its namespace and updates its filter.

### Why Full Hot Reload Is Limited

The `DynamicLogLevelProvider` (global filter) and console redirections are set at startup. Changing the default log level in config mid-flight won't affect:
- Console.Out/Console.Error redirections (set once in `Main()`)
- The global logging minimum level

**Restart required** for changes to:
- Default log level in config (for MCP stdio mode)
- Switching from `None` to another level via config

---

## Namespace-Level Logging

DAB supports different log levels for different namespaces (classes/modules).

### Config Format

```json
{
  "runtime": {
    "telemetry": {
      "log-level": {
        "default": "Warning",
        "Azure.DataApiBuilder.Service.RestController": "Debug",
        "Azure.DataApiBuilder.Core.SqlQueryEngine": "Information"
      }
    }
  }
}
```

### How It Works

1. Each logger instance gets a `LogLevelInitializer` with its namespace
2. `GetConfiguredLogLevel(namespaceFilter)` looks up the namespace in config
3. Falls back to `default` if no specific level is set
4. Hot reload updates these on config change

### Why It Exists

- Debug specific areas without flooding logs
- Production deployments can keep most logs at `Error` but enable `Debug` for a problematic component
- Useful for troubleshooting without restarting

---

## MCP Level Mapping Reference

MCP uses syslog-style levels. Here's how they map to .NET:

| MCP Level | .NET LogLevel | Notes |
|-----------|---------------|-------|
| `debug` | Debug | |
| `info` | Information | |
| `notice` | Information | No .NET equivalent |
| `warning` | Warning | |
| `error` | Error | |
| `critical` | Critical | |
| `alert` | Critical | No .NET equivalent |
| `emergency` | Critical | No .NET equivalent |

### CLI Accepts Only .NET Levels

The `--LogLevel` CLI option is parsed by `System.CommandLine` using the .NET `LogLevel` enum. It does **not** accept MCP-style aliases:

| Input | CLI (`--LogLevel`) | MCP (`logging/setLevel`) |
|-------|-------------------|--------------------------|
| `notice` | ❌ Fails | ✅ Works (→ Information) |
| `Information` | ✅ Works | ❌ Fails (unknown) |
| `info` | ❌ Fails | ✅ Works |

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/Service/Program.cs` | Startup, stream redirections, log level initialization |
| `src/Service/Telemetry/DynamicLogLevelProvider.cs` | Central log level management, precedence enforcement |
| `src/Service/Telemetry/LogLevelInitializer.cs` | Namespace-level logging with hot reload |
| `src/Azure.DataApiBuilder.Mcp/Core/McpStdioServer.cs` | Handles `logging/setLevel` JSON-RPC method |
| `src/Azure.DataApiBuilder.Mcp/Telemetry/McpLogNotificationWriter.cs` | Sends logs as MCP notifications |
| `src/Azure.DataApiBuilder.Mcp/Telemetry/McpLogger.cs` | ILogger that outputs to MCP notifications |
| `src/Azure.DataApiBuilder.Mcp/Telemetry/McpLoggerProvider.cs` | Creates McpLogger instances |

---

## Common Scenarios

### "My logs aren't showing up"

1. Check if `--LogLevel` was set on CLI (it overrides everything)
2. Check if `runtime.telemetry.log-level` is in config (it overrides MCP)
3. If using MCP, did the client call `logging/setLevel`?

### "I see 'MCP logging/setLevel blocked' in stderr"

CLI or config has locked the log level. The MCP request was accepted (no error to client) but the level wasn't changed.

### "I want zero output in production"

Use `--LogLevel None` or set `log-level.default: "None"` in config. Both stdout and stderr will be null streams.

### "I want to debug just one component"

Set namespace-specific level in config:
```json
"log-level": {
  "default": "Error",
  "Azure.DataApiBuilder.Core.SqlQueryEngine": "Debug"
}
```
