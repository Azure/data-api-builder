# Script to replace [DataTestMethod] with [TestMethod] in Cli.Tests

$files = @(
    "Cli.Tests\AddEntityTests.cs",
    "Cli.Tests\AddOpenTelemetryTests.cs",
    "Cli.Tests\AddTelemetryTests.cs",
    "Cli.Tests\ConfigGeneratorTests.cs",
    "Cli.Tests\ConfigureOptionsTests.cs",
    "Cli.Tests\EndToEndTests.cs",
    "Cli.Tests\InitTests.cs",
    "Cli.Tests\UpdateEntityTests.cs",
    "Cli.Tests\UtilsTests.cs",
    "Cli.Tests\ValidateConfigTests.cs"
)

$totalCount = 0
$filesModified = 0

foreach ($file in $files) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        $newContent = $content -replace '\[DataTestMethod\]', '[TestMethod]'
        
        if ($content -ne $newContent) {
            Set-Content -Path $file -Value $newContent -NoNewline
            $fileCount = ($content | Select-String -Pattern '\[DataTestMethod\]' -AllMatches).Matches.Count
            $totalCount += $fileCount
            $filesModified++
            Write-Host "✓ Fixed $fileCount occurrence(s) in $(Split-Path $file -Leaf)" -ForegroundColor Green
        }
    } else {
        Write-Host "✗ File not found: $file" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Files modified: $filesModified" -ForegroundColor Green
Write-Host "  Total replacements: $totalCount" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "MSTEST0044 errors should now be resolved!" -ForegroundColor Green
