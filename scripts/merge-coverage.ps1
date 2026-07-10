<#
.SYNOPSIS
    Merges multiple Cobertura coverage reports (e.g. one per DAB pipeline: unit, mssql,
    postgres, mysql, cosmos, dwsql) into a single combined line/branch coverage number.

.DESCRIPTION
    Azure DevOps' PublishCodeCoverageResults renders a per-pipeline coverage tab but does not
    merge across pipelines. Download each pipeline run's raw *.cobertura.xml into one folder
    (or pass -Files) and run this script to produce a single combined report.

    Merge semantics:
      * LINE coverage is an EXACT union - a source line is covered if it is hit in ANY report.
      * BRANCH coverage is an EXACT union WHEN per-edge detail is available. Coverlet may emit
        <conditions><condition number=".." coverage="N%"/></conditions> children - one per
        edge. When present (their count matches the aggregate denominator) we OR each edge's
        covered flag across reports for a true union. When only the aggregate "(covered/total)"
        is available we fall back to MAX(covered/total): a conservative FLOOR that never
        over-counts but can under-count (e.g. (1/2)+(1/2) covering different edges is really 2/2
        but reports 1/2). Line % is always exact and is the headline metric.

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
    [string]$ExcludePattern = '\.Tests$',
    # When set, writes a merged Cobertura XML to this path (for PublishCodeCoverageResults).
    [string]$OutFile,
    # Optional path substrings (e.g. 'unit','mssql','cosmos'). A warning is printed for each
    # that matches none of the discovered files, so a silently-missing source is noticeable.
    [string[]]$ExpectedSources
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

$reportFiles = @($reportFiles | Where-Object { $_ -and (Test-Path $_) } |
        ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Select-Object -Unique)
if ($reportFiles.Count -eq 0) {
    Write-Error "No cobertura files found."
    exit 1   # intentional: fail this task when there is nothing to merge (CI job is continueOnError)
}

# Surface silently-missing sources: a DB whose artifact failed to download simply
# vanishes from the union, so warn loudly for each expected source with no files.
if ($ExpectedSources) {
    foreach ($src in $ExpectedSources) {
        if (-not ($reportFiles | Where-Object { $_ -like "*$src*" })) {
            Write-Warning "Expected coverage source '$src' not found - it is ABSENT from the combined union."
        }
    }
}

# key: "package|class|lineNumber" -> merged coverage point
$points = @{}
# "package|class" -> representative filename (first non-empty seen)
$classFile = @{}

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
            $clsFile = [string]$cls.filename
            if (-not $cls.lines) { continue }
            $ckey = "$pkgName|$clsName"
            if ($clsFile -and -not $classFile.ContainsKey($ckey)) { $classFile[$ckey] = $clsFile }
            foreach ($ln in $cls.lines.line) {
                if (-not $ln) { continue }
                $num = [int]$ln.number
                $hits = [long]$ln.hits
                $isBranch = ([string]$ln.branch -eq 'true')
                $condCov = 0
                $condTot = 0
                if ($isBranch -and ([string]$ln.'condition-coverage') -match '\((\d+)/(\d+)\)') {
                    $condCov = [int]$Matches[1]
                    $condTot = [int]$Matches[2]
                }

                # Per-edge branch detail: coverlet may emit one <condition number="i" coverage="N%"/>
                # per edge. When their count equals the aggregate denominator we can OR each edge's
                # covered flag across reports for a TRUE union (instead of MAX-ing aggregate counts,
                # which under-counts when two reports cover different edges of the same line).
                $edges = $null
                if ($isBranch -and $ln.conditions -and $ln.conditions.condition) {
                    $condEls = @($ln.conditions.condition)
                    if ($condTot -gt 0 -and $condEls.Count -eq $condTot) {
                        $edges = @{}
                        foreach ($c in $condEls) {
                            $covPct = 0
                            if (([string]$c.coverage) -match '(\d+)') { $covPct = [int]$Matches[1] }
                            $edges[[int]$c.number] = ($covPct -ge 100)
                        }
                    }
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
                        if ($edges) {
                            if (-not $e.Edges) { $e.Edges = @{} }
                            foreach ($n in $edges.Keys) {
                                $e.Edges[$n] = ([bool]$e.Edges[$n]) -or $edges[$n]
                            }
                        }
                    }
                }
                else {
                    $points[$key] = [pscustomobject]@{
                        Package  = $pkgName
                        Class    = $clsName
                        Line     = $num
                        Hits     = $hits
                        IsBranch = $isBranch
                        CondCov  = $condCov
                        CondTot  = $condTot
                        Edges    = $edges
                    }
                }
            }
        }
    }
}

# Effective branch cov/tot per point: exact union from per-edge detail when available,
# else the aggregate MAX (a conservative floor).
foreach ($e in $points.Values) {
    if ($e.IsBranch -and $e.Edges -and $e.Edges.Count -gt 0) {
        $cov = 0; foreach ($v in $e.Edges.Values) { if ($v) { $cov++ } }
        $e | Add-Member -NotePropertyName EffTot -NotePropertyValue $e.Edges.Count -Force
        $e | Add-Member -NotePropertyName EffCov -NotePropertyValue $cov -Force
    }
    elseif ($e.IsBranch) {
        $e | Add-Member -NotePropertyName EffTot -NotePropertyValue $e.CondTot -Force
        $e | Add-Member -NotePropertyName EffCov -NotePropertyValue $e.CondCov -Force
    }
    else {
        $e | Add-Member -NotePropertyName EffTot -NotePropertyValue 0 -Force
        $e | Add-Member -NotePropertyName EffCov -NotePropertyValue 0 -Force
    }
}

# Aggregate overall and per-assembly.
$byPkg = @{}
$totLines = 0; $covLines = 0; $totBr = 0; $covBr = 0

foreach ($e in $points.Values) {
    $totLines++
    if ($e.Hits -gt 0) { $covLines++ }
    if ($e.IsBranch) { $totBr += $e.EffTot; $covBr += $e.EffCov }

    if (-not $byPkg.ContainsKey($e.Package)) {
        $byPkg[$e.Package] = [pscustomobject]@{ TL = 0; CL = 0; TB = 0; CB = 0 }
    }
    $p = $byPkg[$e.Package]
    $p.TL++
    if ($e.Hits -gt 0) { $p.CL++ }
    if ($e.IsBranch) { $p.TB += $e.EffTot; $p.CB += $e.EffCov }
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
Write-Host "  LINE   : $covLines / $totLines = $LR% (exact union)"
Write-Host "  BRANCH : $covBr / $totBr = $BR% (exact where per-edge detail present, else floor)"
Write-Host "================================================================="

# ---------------------------------------------------------------------------
# Optionally emit a merged Cobertura XML (so PublishCodeCoverageResults can show
# the combined number in the Code Coverage tab - no ReportGenerator required).
# ---------------------------------------------------------------------------
if ($OutFile) {
    function ConvertTo-XmlAttr([string]$s) {
        if ($null -eq $s) { return '' }
        return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;').Replace("'", '&apos;')
    }

    # Group merged points into package -> class -> lines.
    $pkgTree = @{}
    foreach ($e in $points.Values) {
        if (-not $pkgTree.ContainsKey($e.Package)) { $pkgTree[$e.Package] = @{} }
        $classes = $pkgTree[$e.Package]
        if (-not $classes.ContainsKey($e.Class)) { $classes[$e.Class] = New-Object System.Collections.ArrayList }
        [void]$classes[$e.Class].Add($e)
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    $lineRate = if ($totLines) { [math]::Round($covLines / $totLines, 4) } else { 0 }
    $branchRate = if ($totBr) { [math]::Round($covBr / $totBr, 4) } else { 0 }
    $ts = [int64]([datetime]::UtcNow - [datetime]'1970-01-01').TotalSeconds
    [void]$sb.AppendLine("<coverage line-rate=""$lineRate"" branch-rate=""$branchRate"" lines-covered=""$covLines"" lines-valid=""$totLines"" branches-covered=""$covBr"" branches-valid=""$totBr"" version=""1.9"" timestamp=""$ts"">")
    [void]$sb.AppendLine('  <sources><source>.</source></sources>')
    [void]$sb.AppendLine('  <packages>')
    foreach ($pk in ($pkgTree.Keys | Sort-Object)) {
        $classes = $pkgTree[$pk]
        $pTL = 0; $pCL = 0; $pTB = 0; $pCB = 0
        foreach ($clist in $classes.Values) {
            foreach ($e in $clist) {
                $pTL++; if ($e.Hits -gt 0) { $pCL++ }
                if ($e.IsBranch) { $pTB += $e.EffTot; $pCB += $e.EffCov }
            }
        }
        $pLR = if ($pTL) { [math]::Round($pCL / $pTL, 4) } else { 0 }
        $pBR = if ($pTB) { [math]::Round($pCB / $pTB, 4) } else { 0 }
        [void]$sb.AppendLine(("    <package name=""{0}"" line-rate=""{1}"" branch-rate=""{2}"" complexity=""0"">" -f (ConvertTo-XmlAttr $pk), $pLR, $pBR))
        [void]$sb.AppendLine('      <classes>')
        foreach ($cn in ($classes.Keys | Sort-Object)) {
            $clist = $classes[$cn]
            $cTL = 0; $cCL = 0; $cTB = 0; $cCB = 0
            foreach ($e in $clist) {
                $cTL++; if ($e.Hits -gt 0) { $cCL++ }
                if ($e.IsBranch) { $cTB += $e.EffTot; $cCB += $e.EffCov }
            }
            $cLR = if ($cTL) { [math]::Round($cCL / $cTL, 4) } else { 0 }
            $cBR = if ($cTB) { [math]::Round($cCB / $cTB, 4) } else { 0 }
            $fn = ''
            if ($classFile.ContainsKey("$pk|$cn")) { $fn = $classFile["$pk|$cn"] }
            [void]$sb.AppendLine(("        <class name=""{0}"" filename=""{1}"" line-rate=""{2}"" branch-rate=""{3}"" complexity=""0"">" -f (ConvertTo-XmlAttr $cn), (ConvertTo-XmlAttr $fn), $cLR, $cBR))
            [void]$sb.AppendLine('          <methods />')
            [void]$sb.AppendLine('          <lines>')
            foreach ($e in ($clist | Sort-Object Line)) {
                if ($e.IsBranch -and $e.EffTot -gt 0) {
                    $pct = [math]::Round($e.EffCov / $e.EffTot * 100, 0)
                    [void]$sb.AppendLine(("            <line number=""{0}"" hits=""{1}"" branch=""true"" condition-coverage=""{2}% ({3}/{4})"" />" -f $e.Line, $e.Hits, $pct, $e.EffCov, $e.EffTot))
                }
                else {
                    [void]$sb.AppendLine(("            <line number=""{0}"" hits=""{1}"" branch=""false"" />" -f $e.Line, $e.Hits))
                }
            }
            [void]$sb.AppendLine('          </lines>')
            [void]$sb.AppendLine('        </class>')
        }
        [void]$sb.AppendLine('      </classes>')
        [void]$sb.AppendLine('    </package>')
    }
    [void]$sb.AppendLine('  </packages>')
    [void]$sb.AppendLine('</coverage>')

    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($OutFile, $sb.ToString())
    Write-Host ""
    Write-Host "Wrote merged Cobertura report: $OutFile"
}
