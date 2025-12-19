# Test script for MCP Custom Tools
# Tests various scenarios and edge cases

# Bypass SSL certificate validation for local testing
add-type @"
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    public class TrustAllCertsPolicy : ICertificatePolicy {
        public bool CheckValidationResult(
            ServicePoint srvPoint, X509Certificate certificate,
            WebRequest request, int certificateProblem) {
            return true;
        }
    }
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy

$baseUrl = "https://localhost:5001/mcp"
$headers = @{
    "Content-Type" = "application/json"
    "Accept" = "application/json, text/event-stream"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "MCP Custom Tools - Test Suite" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Helper function to make MCP requests
function Invoke-McpRequest {
    param(
        [string]$Method,
        [object]$Params,
        [int]$Id = 1
    )
    
    $body = @{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
        id = $Id
    } | ConvertTo-Json -Depth 10
    
    try {
        $response = Invoke-RestMethod -Uri $baseUrl -Method Post -Headers $headers -Body $body
        return $response
    }
    catch {
        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) {
            Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
        }
        return $null
    }
}

# Test 1: List all tools
Write-Host "Test 1: List all tools" -ForegroundColor Yellow
Write-Host "Expected: Should see get_books, get_book, insert_book, count_books in the list" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/list" -Params @{}
if ($response) {
    $customTools = $response.result.tools | Where-Object { $_.name -match "^(get_books|get_book|insert_book|count_books)$" }
    Write-Host "Custom tools found: $($customTools.Count)" -ForegroundColor Green
    $customTools | ForEach-Object { 
        Write-Host "  - $($_.name): $($_.description)" -ForegroundColor White
        Write-Host "    Input Schema: $($_.inputSchema | ConvertTo-Json -Compress)" -ForegroundColor Gray
    }
}
Write-Host ""

# Test 2: Call get_books (no parameters)
Write-Host "Test 2: Call get_books (no parameters)" -ForegroundColor Yellow
Write-Host "Expected: Should return list of books" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ name = "get_books" } -Id 2
if ($response) {
    Write-Host "Success! Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 3: Call count_books (no parameters)
Write-Host "Test 3: Call count_books (no parameters)" -ForegroundColor Yellow
Write-Host "Expected: Should return total count of books" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ name = "count_books" } -Id 3
if ($response) {
    Write-Host "Success! Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 4: Call get_book with parameter (id=1)
Write-Host "Test 4: Call get_book with parameter (id=1)" -ForegroundColor Yellow
Write-Host "Expected: Should return book with id=1" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "get_book"
    arguments = @{ id = 1 }
} -Id 4
if ($response) {
    Write-Host "Success! Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 5: Call get_book with non-existent id
Write-Host "Test 5: Call get_book with non-existent id (id=999999)" -ForegroundColor Yellow
Write-Host "Expected: Should return empty result or handle gracefully" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "get_book"
    arguments = @{ id = 999999 }
} -Id 5
if ($response) {
    Write-Host "Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 6: Call get_book without required parameter
Write-Host "Test 6: Call get_book without required parameter" -ForegroundColor Yellow
Write-Host "Expected: Should fail with parameter error" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "get_book"
} -Id 6
if ($response) {
    Write-Host "Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 7: Call insert_book with parameters
Write-Host "Test 7: Call insert_book with parameters" -ForegroundColor Yellow
Write-Host "Expected: Should insert a new book" -ForegroundColor Gray
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "insert_book"
    arguments = @{ 
        title = "MCP Test Book $timestamp"
        publisher_id = "1234"
    }
} -Id 7
if ($response) {
    Write-Host "Success! Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 8: Call insert_book with only title (missing publisher_id)
Write-Host "Test 8: Call insert_book with only title (missing publisher_id)" -ForegroundColor Yellow
Write-Host "Expected: Should use default value from config (1234) or fail" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "insert_book"
    arguments = @{ 
        title = "Test Book Missing Publisher"
    }
} -Id 8
if ($response) {
    Write-Host "Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 9: Call insert_book with invalid publisher_id
Write-Host "Test 9: Call insert_book with invalid publisher_id" -ForegroundColor Yellow
Write-Host "Expected: Should fail with foreign key constraint error" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "insert_book"
    arguments = @{ 
        title = "Test Book Invalid Publisher"
        publisher_id = "99999"
    }
} -Id 9
if ($response) {
    Write-Host "Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 10: Call non-existent custom tool
Write-Host "Test 10: Call non-existent custom tool" -ForegroundColor Yellow
Write-Host "Expected: Should fail with tool not found error" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ 
    name = "non_existent_tool"
} -Id 10
if ($response) {
    Write-Host "Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 11: Verify count after inserts
Write-Host "Test 11: Verify count after inserts" -ForegroundColor Yellow
Write-Host "Expected: Should show updated count" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ name = "count_books" } -Id 11
if ($response) {
    Write-Host "Success! Response:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5) -ForegroundColor White
}
Write-Host ""

# Test 12: Call get_books again to see new books
Write-Host "Test 12: Call get_books to see newly inserted books" -ForegroundColor Yellow
Write-Host "Expected: Should include newly inserted books" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{ name = "get_books" } -Id 12
if ($response) {
    Write-Host "Success! Response (first 5 books):" -ForegroundColor Green
    $books = $response.result.content[0].text | ConvertFrom-Json
    $books | Select-Object -First 5 | ForEach-Object {
        Write-Host "  Book: $($_.title) (ID: $($_.id), Publisher: $($_.publisher_id))" -ForegroundColor White
    }
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Suite Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
