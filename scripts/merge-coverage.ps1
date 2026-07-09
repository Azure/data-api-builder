<#
.SYNOPSIS
    Merges multiple Cobertura coverage reports (e.g. one per DAB pipeline: unit, mssql,
    postgres, mysql, cosmos, dwsql) into a single combined line/branch coverage number.

.DESCRIPTION
    Azure DevOps' PublishCodeCoverageResults@1 renders a per-pipeline coverage tab but does
    not merge across pipelines. Download each pipeline run's raw *.cobertura.xml (from the
    "Code Coverage Report_<id>" artifact) into one folder, then run this script.

    Merge semantics (union): a source line is covered if it is hit in ANY report; a line's
    covered branch-conditions are the MAX seen across reports (denominator is fixed per line).
    This matches how ReportGenerator unions reports when per-condition detail is absent.

    Zero external dependencies (no reportgenerator / no az CLI required).

.PARAMETER Path
    Folder to search recursively for *.cobertura.xml files.

.PARAMETER Files
    Explicit list of cobertura files to merge (overrides -Path).

.EXAMPLE
    ./merge-coverage.ps1 -Path C:\coverage-downloads

.EXAMPLE
    ./merge-coverage.ps1 -Files unit.cobertura.xml,mssql.cobertura.xml,pg.cobertura.xml
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string[]]$Files,
    # Package (assembly) names matching this regex are excluded (test projects by default).
    [string]$ExcludePattern = '\.Tests$'
)

if ($Files) {
    $reportFiles = $Files
}
elseif ($Path) {
    $reportFiles = Get-ChildItem -Path $Path -Recurse -Filter *.cobertura.xml -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
}
else {
    Write-Error "Provide -Path <folder> or -Files a.xml,b.xml,..."
    exit 1
}

$reportFiles = @($reportFiles | Where-Object { $_ -and (Test-Path $_) })
if ($reportFiles.Count -eq 0) {
    Write-Error "No cobertura files found."
    exit 1
}

# key: "package|filename|lineNumber" -> merged coverage point
$points = @{}

foreach ($rf in $reportFiles) {
    try {
        [xml]$xml = Get-Content -LiteralPath $rf -Raw
    }
    catch {
        Write-Warning "Skipping unreadable file: $rf"
        continue
    }

    foreach ($pkg in $xml.coverage.packages.package) {
        $pkgName = [string]$pkg.name
        if ($ExcludePattern -and $pkgName -match $ExcludePattern) { continue }
        foreach ($cls in $pkg.classes.class) {
            $clsName = [string]$cls.name
            if (-not $cls.lines) { continue }
            foreach ($ln in $cls.lines.line) {
                if (-not $ln) { continue }
                $num = [int]$ln.number
                $hits = [int]$ln.hits
                $isBranch = ([string]$ln.branch -eq 'true')
                $condCov = 0
                $condTot = 0
                if ($isBranch -and ([string]$ln.'condition-coverage') -match '\((\d+)/(\d+)\)') {
                    $condCov = [int]$Matches[1]
                    $condTot = [int]$Matches[2]
                }

                # Key by FQ class name (agent-path independent) + line number so the same
                # source point unions across reports built on different agents (Windows vs Linux).
                $key = "$pkgName|$clsName|$num"
                if ($points.ContainsKey($key)) {
                    $e = $points[$key]
                    if ($hits -gt $e.Hits) { $e.Hits = $hits }
                    if ($isBranch) {
                        $e.IsBranch = $true
                        if ($condCov -gt $e.CondCov) { $e.CondCov = $condCov }
                        if ($condTot -gt $e.CondTot) { $e.CondTot = $condTot }
                    }
                }
                else {
                    $points[$key] = [pscustomobject]@{
                        Package  = $pkgName
                        Hits     = $hits
                        IsBranch = $isBranch
                        CondCov  = $condCov
                        CondTot  = $condTot
                    }
                }
            }
        }
    }
}

# Aggregate overall and per-assembly.
$byPkg = @{}
$totLines = 0; $covLines = 0; $totBr = 0; $covBr = 0

foreach ($e in $points.Values) {
    $totLines++
    if ($e.Hits -gt 0) { $covLines++ }
    if ($e.IsBranch) { $totBr += $e.CondTot; $covBr += $e.CondCov }

    if (-not $byPkg.ContainsKey($e.Package)) {
        $byPkg[$e.Package] = [pscustomobject]@{ TL = 0; CL = 0; TB = 0; CB = 0 }
    }
    $p = $byPkg[$e.Package]
    $p.TL++
    if ($e.Hits -gt 0) { $p.CL++ }
    if ($e.IsBranch) { $p.TB += $e.CondTot; $p.CB += $e.CondCov }
}

Write-Host ""
Write-Host "Merged $($reportFiles.Count) coverage file(s):"
$reportFiles | ForEach-Object { Write-Host "  - $_" }
Write-Host ""
Write-Host ("{0,-45} {1,12} {2,14}" -f 'Assembly', 'Line %', 'Branch %')
Write-Host ("{0,-45} {1,12} {2,14}" -f ('-' * 40), '------', '--------')
foreach ($k in ($byPkg.Keys | Sort-Object)) {
    $p = $byPkg[$k]
    $lr = if ($p.TL) { [math]::Round($p.CL / $p.TL * 100, 1) } else { 0 }
    $br = if ($p.TB) { [math]::Round($p.CB / $p.TB * 100, 1) } else { 0 }
    Write-Host ("{0,-45} {1,9}% ({2}/{3})  {4,7}% ({5}/{6})" -f $k, $lr, $p.CL, $p.TL, $br, $p.CB, $p.TB)
}
Write-Host ""
$LR = if ($totLines) { [math]::Round($covLines / $totLines * 100, 2) } else { 0 }
$BR = if ($totBr) { [math]::Round($covBr / $totBr * 100, 2) } else { 0 }
Write-Host "==================== COMBINED (all pipelines) ===================="
Write-Host "  LINE   : $covLines / $totLines = $LR%"
Write-Host "  BRANCH : $covBr / $totBr = $BR%"
Write-Host "================================================================="
