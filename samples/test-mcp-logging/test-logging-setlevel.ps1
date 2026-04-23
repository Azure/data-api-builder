# Test script for MCP logging/setLevel
# This script tests the logging/setLevel handler in DAB's STDIO MCP server

param(
    [switch]$WithCliOverride
)

$dabExe = "Q:\src\DABRepo\data-api-builder\src\out\cli\net8.0\Microsoft.DataApiBuilder.exe"
$config = "dab-config.json"

# JSON-RPC messages
$initRequest = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}'
$initializedNotification = '{"jsonrpc":"2.0","method":"notifications/initialized"}'
$setLevelDebug = '{"jsonrpc":"2.0","id":2,"method":"logging/setLevel","params":{"level":"debug"}}'
$setLevelError = '{"jsonrpc":"2.0","id":3,"method":"logging/setLevel","params":{"level":"error"}}'
$setLevelInvalid = '{"jsonrpc":"2.0","id":4,"method":"logging/setLevel","params":{"level":"invalid_level"}}'

# Combine messages
$allMessages = @($initRequest, $initializedNotification, $setLevelDebug, $setLevelError, $setLevelInvalid) -join "`n"

Write-Host "=== Testing MCP logging/setLevel ===" -ForegroundColor Cyan

if ($WithCliOverride) {
    Write-Host "Running WITH --LogLevel Warning (CLI override)" -ForegroundColor Yellow
    Write-Host "Expected: setLevel should return success but NOT change the level" -ForegroundColor Yellow
    $allMessages | & $dabExe start --config $config --mcp-stdio --LogLevel Warning 2>&1
} else {
    Write-Host "Running WITHOUT CLI override" -ForegroundColor Green
    Write-Host "Expected: setLevel should change the log level" -ForegroundColor Green
    $allMessages | & $dabExe start --config $config --mcp-stdio 2>&1
}
