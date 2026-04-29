# PR: Add MCP notifications/message for log streaming to clients

## Why make this change?

Enables MCP clients (like MCP Inspector, Claude Desktop, VS Code Copilot) to receive real-time log output via MCP `notifications/message`.

Related: #3274 (depends on PR #3419)

## What is this change?

When `logging/setLevel` is called with a level other than "none", logs are sent to MCP clients as JSON-RPC notifications:

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/message",
  "params": {
    "level": "info",
    "logger": "Azure.DataApiBuilder.Service.Startup",
    "data": "Starting Data API builder..."
  }
}
```

### New files:
- `McpLogNotificationWriter.cs` - Writes logs as MCP notifications to stdout
- `McpLogger.cs` / `McpLoggerProvider.cs` - ILogger implementation for .NET logging pipeline
- `McpLogNotificationTests.cs` - Unit tests (8 tests)

### Modified files:
- `Program.cs` - Registers `McpNotificationWriter` and `McpLoggerProvider` for MCP mode
- `McpStdioServer.cs` - Enables notifications when `logging/setLevel` is called

## How was this tested?

- Unit tests: 8 tests covering level mapping, enable/disable, JSON format
- Manual testing with MCP Inspector: verified notifications appear when `logging/setLevel` is sent

## Note

This PR targets `dev/anushakolan/set-log-level` (PR #3419) as it depends on the `logging/setLevel` implementation.
