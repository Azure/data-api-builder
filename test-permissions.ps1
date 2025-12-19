# Permission tests for custom MCP tools
# Test with different authentication roles

$baseUrl = "https://localhost:5001/mcp"
$headers = @{
    "Content-Type" = "application/json"
    "Accept" = "application/json, text/event-stream"
}

# Bypass SSL validation
if (-not ([System.Management.Automation.PSTypeName]'TrustAllCertsPolicy2').Type) {
    add-type @"
        using System.Net;
        using System.Security.Cryptography.X509Certificates;
        public class TrustAllCertsPolicy2 : ICertificatePolicy {
            public bool CheckValidationResult(
                ServicePoint svcPoint, X509Certificate certificate,
                WebRequest request, int certificateProblem) {
                return true;
            }
        }
"@
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy2
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Invoke-McpRequest {
    param(
        [string]$Method,
        [hashtable]$Params,
        [int]$Id,
        [string]$Role = "anonymous"
    )
    
    $requestHeaders = $headers.Clone()
    if ($Role -ne "anonymous") {
        $requestHeaders["X-MS-CLIENT-PRINCIPAL"] = ConvertTo-Json @{
            userId = "test-user-123"
            userRoles = @($Role)
            claims = @()
            identityProvider = "staticwebapps"
        } -Compress
    }
    
    $body = @{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
        id = $Id
    } | ConvertTo-Json -Depth 10
    
    try {
        $response = Invoke-WebRequest -Uri $baseUrl -Method Post -Headers $requestHeaders -Body $body -UseBasicParsing
        return $response.Content
    }
    catch {
        return "ERROR: $($_.Exception.Message)"
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Custom MCP Tools - Permission Tests" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Anonymous role (default in config)
Write-Host "Test 1: Call get_books as anonymous user" -ForegroundColor Yellow
Write-Host "Expected: Should succeed - anonymous has execute permission" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "get_books"
} -Id 1 -Role "anonymous"
if ($response -like "*Execution successful*") {
    Write-Host "✓ SUCCESS" -ForegroundColor Green
} else {
    Write-Host "✗ FAILED" -ForegroundColor Red
    Write-Host "Response: $($response.Substring(0, [Math]::Min(300, $response.Length)))" -ForegroundColor Red
}
Write-Host ""

# Test 2: Authenticated role
Write-Host "Test 2: Call insert_book as authenticated user" -ForegroundColor Yellow
Write-Host "Expected: Should succeed - authenticated has execute permission" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/call" -Params @{
    name = "insert_book"
    arguments = @{
        title = "Permission Test Book"
        publisher_id = "1234"
    }
} -Id 2 -Role "authenticated"
if ($response -like "*Execution successful*") {
    Write-Host "✓ SUCCESS" -ForegroundColor Green
} else {
    Write-Host "✗ FAILED" -ForegroundColor Red
    Write-Host "Response: $($response.Substring(0, [Math]::Min(300, $response.Length)))" -ForegroundColor Red
}
Write-Host ""

# Test 3: Check tools list is accessible anonymously
Write-Host "Test 3: List tools as anonymous" -ForegroundColor Yellow
Write-Host "Expected: Should see all custom tools" -ForegroundColor Gray
$response = Invoke-McpRequest -Method "tools/list" -Params @{} -Id 3 -Role "anonymous"
$customToolCount = ([regex]::Matches($response, '"name":"(get_books|get_book|insert_book|count_books)"')).Count
Write-Host "Custom tools found: $customToolCount / 4" -ForegroundColor $(if ($customToolCount -eq 4) { "Green" } else { "Red" })
Write-Host ""

# Test 4: Multiple rapid requests
Write-Host "Test 4: Multiple rapid requests (concurrency test)" -ForegroundColor Yellow
Write-Host "Expected: All requests should complete successfully" -ForegroundColor Gray
$jobs = @()
1..5 | ForEach-Object {
    $jobs += Start-Job -ScriptBlock {
        param($baseUrl, $headers, $i)
        
        # Bypass SSL in job context
        add-type @"
            using System.Net;
            using System.Security.Cryptography.X509Certificates;
            public class TrustAllCertsPolicy3 : ICertificatePolicy {
                public bool CheckValidationResult(
                    ServicePoint svcPoint, X509Certificate certificate,
                    WebRequest request, int certificateProblem) {
                    return true;
                }
            }
"@
        [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy3
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        
        $body = @{
            jsonrpc = "2.0"
            method = "tools/call"
            params = @{
                name = "count_books"
            }
            id = $i
        } | ConvertTo-Json -Depth 10
        
        try {
            $response = Invoke-WebRequest -Uri $baseUrl -Method Post -Headers $headers -Body $body -UseBasicParsing
            return @{ Success = $true; Id = $i; Response = $response.Content }
        }
        catch {
            return @{ Success = $false; Id = $i; Error = $_.Exception.Message }
        }
    } -ArgumentList $baseUrl, $headers, $_
}

$results = $jobs | Wait-Job | Receive-Job
$successCount = ($results | Where-Object { $_.Success }).Count
Write-Host "Successful requests: $successCount / 5" -ForegroundColor $(if ($successCount -eq 5) { "Green" } else { "Yellow" })
$jobs | Remove-Job
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Permission Tests Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
