# Edge case tests for custom MCP tools
# Bypass SSL validation
add-type @"
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    public class TrustAllCertsPolicy : ICertificatePolicy {
        public bool CheckValidationResult(
            ServicePoint svcPoint, X509Certificate certificate,
            WebRequest request, int certificateProblem) {
            return true;
        }
    }
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$baseUrl = "https://localhost:5001/mcp"
$headers = @{
    "Content-Type" = "application/json"
    "Accept" = "application/json, text/event-stream"
}

function Invoke-McpRequest {
    param(
        [string]$Method,
        [hashtable]$Params,
        [int]$Id
    )
    
    $body = @{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
        id = $Id
    } | ConvertTo-Json -Depth 10
    
    try {
        $response = Invoke-WebRequest -Uri $baseUrl -Method Post -Headers $headers -Body $body -UseBasicParsing
        return $response.Content
    }
    catch {
        return $_.Exception.Message
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Custom MCP Tools - Edge Case Tests" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Edge Case 1: SQL Injection attempt
Write-Host "Edge Case 1: SQL Injection attempt in parameters" -ForegroundColor Yellow
Write-Host "Expected: Should be safely parameterized, no SQL injection" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_book"
    arguments = @{
        id = "1; DROP TABLE books; --"
    }
} -Id 100
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 2: Very large parameter value
Write-Host "Edge Case 2: Very large string parameter" -ForegroundColor Yellow
Write-Host "Expected: Should handle or reject gracefully" -ForegroundColor Gray
$largeString = "A" * 10000
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "insert_book"
    arguments = @{
        title = $largeString
        publisher_id = "1234"
    }
} -Id 101
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 3: Special characters in parameters
Write-Host "Edge Case 3: Special characters in title" -ForegroundColor Yellow
Write-Host "Expected: Should handle special characters correctly" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "insert_book"
    arguments = @{
        title = "Test Book with 'quotes' and `"double quotes`" and <tags>"
        publisher_id = "1234"
    }
} -Id 102
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 4: Null/empty parameters
Write-Host "Edge Case 4: Empty string for title" -ForegroundColor Yellow
Write-Host "Expected: Should handle empty string" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "insert_book"
    arguments = @{
        title = ""
        publisher_id = "1234"
    }
} -Id 103
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 5: Wrong parameter type
Write-Host "Edge Case 5: String value for integer parameter" -ForegroundColor Yellow
Write-Host "Expected: Should convert or reject with type error" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_book"
    arguments = @{
        id = "not_a_number"
    }
} -Id 104
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 6: Extra unexpected parameters
Write-Host "Edge Case 6: Extra parameters not in schema" -ForegroundColor Yellow
Write-Host "Expected: Should ignore extra parameters or fail gracefully" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_books"
    arguments = @{
        unexpected_param = "value"
        another_param = 123
    }
} -Id 105
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 7: Unicode characters
Write-Host "Edge Case 7: Unicode characters in title" -ForegroundColor Yellow
Write-Host "Expected: Should handle Unicode correctly" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "insert_book"
    arguments = @{
        title = "Test Unicode Book"
        publisher_id = "1234"
    }
} -Id 106
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 8: Negative ID
Write-Host "Edge Case 8: Negative ID value" -ForegroundColor Yellow
Write-Host "Expected: Should return empty result" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_book"
    arguments = @{
        id = -1
    }
} -Id 107
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 9: Very large ID
Write-Host "Edge Case 9: Very large ID value (Int32.MaxValue)" -ForegroundColor Yellow
Write-Host "Expected: Should handle large integers" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_book"
    arguments = @{
        id = 2147483647
    }
} -Id 108
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

# Edge Case 10: Case sensitivity in tool names
Write-Host "Edge Case 10: Wrong case in tool name" -ForegroundColor Yellow
Write-Host "Expected: Should fail - tool names are case sensitive" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "GET_BOOKS"
    arguments = @{}
} -Id 109
Write-Host "Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Green
Write-Host ""

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Edge Case Tests Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
