# Test enhanced custom tools with descriptions
# Bypass SSL validation
if (-not ([System.Management.Automation.PSTypeName]'TrustAllCertsPolicy4').Type) {
    add-type @"
        using System.Net;
        using System.Security.Cryptography.X509Certificates;
        public class TrustAllCertsPolicy4 : ICertificatePolicy {
            public bool CheckValidationResult(
                ServicePoint svcPoint, X509Certificate certificate,
                WebRequest request, int certificateProblem) {
                return true;
            }
        }
"@
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy4
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$baseUrl = "https://localhost:5001/mcp"
$headers = @{
    "Content-Type" = "application/json"
    "Accept" = "application/json, text/event-stream"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Enhanced Custom Tools - Description Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: List tools and check descriptions
Write-Host "Test 1: List tools and verify descriptions" -ForegroundColor Yellow
$body = @{
    jsonrpc = "2.0"
    method = "tools/list"
    params = @{}
    id = 1
} | ConvertTo-Json

try {
    $response = Invoke-WebRequest -Uri $baseUrl -Method Post -Headers $headers -Body $body -UseBasicParsing
    $content = $response.Content
    
    # Parse SSE format
    if ($content -match 'data: (.+)') {
        $jsonData = $matches[1]
        $data = $jsonData | ConvertFrom-Json
        
        if ($data.result.tools) {
            $customTools = $data.result.tools | Where-Object { $_.name -in @('get_books', 'get_book', 'insert_book', 'count_books') }
            
            Write-Host ""
            Write-Host "Custom Tools Found:" -ForegroundColor Green
            foreach ($tool in $customTools) {
                Write-Host ""
                Write-Host "  Tool: $($tool.name)" -ForegroundColor Cyan
                Write-Host "  Description: $($tool.description)" -ForegroundColor White
                
                # Check for parameters
                if ($tool.inputSchema.properties) {
                    $propNames = $tool.inputSchema.properties.PSObject.Properties.Name
                    if ($propNames.Count -gt 0) {
                        Write-Host "  Parameters:" -ForegroundColor Yellow
                        foreach ($paramName in $propNames) {
                            $paramDesc = $tool.inputSchema.properties.$paramName.description
                            Write-Host "    - $paramName : $paramDesc" -ForegroundColor Gray
                        }
                    }
                }
            }
            
            Write-Host ""
            Write-Host "All custom tools have descriptions!" -ForegroundColor Green
        }
    }
}
catch {
    Write-Host "✗ Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Description Test Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
