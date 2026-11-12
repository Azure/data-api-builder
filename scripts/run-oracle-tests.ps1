# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

<#
.SYNOPSIS
Runs Oracle tests against an isolated, disposable Oracle XE database.
.DESCRIPTION
Requires PowerShell 7, Docker (Linux containers), and the repository's .NET SDK.
Generates temporary credentials in process environment variables, binds a random
loopback port, and removes only the container created by this invocation.
#>
[CmdletBinding()]
param(
    [string] $Image = 'gvenzl/oracle-xe:21-slim',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $ResultsDirectory = '',
    [string] $BaseOutputPath = '',
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$containerName = 'dab-oracle-tests-' + [Guid]::NewGuid().ToString('N')
$containerCreated = $false
$savedEnvironment = @{}
foreach ($name in @('ORACLE_PASSWORD', 'APP_USER', 'APP_USER_PASSWORD', 'DAB_ORACLE_TEST_CONNECTION_STRING')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    $env:ORACLE_PASSWORD = 'Db' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(12))
    $env:APP_USER = 'DAB_TESTS'
    $env:APP_USER_PASSWORD = 'App' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(12))
    if ($env:TF_BUILD -eq 'True') {
        Write-Host "##vso[task.setsecret]$env:ORACLE_PASSWORD"
        Write-Host "##vso[task.setsecret]$env:APP_USER_PASSWORD"
    }

    Write-Host 'Starting an isolated Oracle XE test database.'
    docker run --detach --name $containerName --publish '127.0.0.1::1521' `
        --env ORACLE_PASSWORD --env APP_USER --env APP_USER_PASSWORD `
        --health-cmd healthcheck.sh --health-interval 5s --health-timeout 5s --health-retries 60 $Image | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start the Oracle test container.' }
    $containerCreated = $true

    $startupTimer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $health = docker inspect --format '{{.State.Status}}|{{.State.Health.Status}}' $containerName
        if ($LASTEXITCODE -ne 0) { throw 'Unable to read the Oracle test container status.' }
        if ($health -eq 'running|healthy') { break }
        if ($health -match 'exited|unhealthy' -or $startupTimer.Elapsed.TotalMinutes -ge 5) {
            $startupLog = (docker logs --tail 40 $containerName 2>&1 | Out-String)
            Write-Host $startupLog.Replace($env:ORACLE_PASSWORD, '[redacted]').Replace($env:APP_USER_PASSWORD, '[redacted]')
            throw 'Oracle did not become healthy within five minutes.'
        }
        Start-Sleep -Seconds 5
    } while ($true)

    $binding = docker port $containerName 1521/tcp
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Unable to determine the loopback port of the Oracle test container.'
    }
    $env:DAB_ORACLE_TEST_CONNECTION_STRING = "User Id=$env:APP_USER;Password=$env:APP_USER_PASSWORD;Data Source=127.0.0.1:$($Matches[1])/XEPDB1"
    if ($env:TF_BUILD -eq 'True') {
        Write-Host "##vso[task.setsecret]$env:DAB_ORACLE_TEST_CONNECTION_STRING"
    }

    $testArguments = @(
        'test', (Join-Path $repositoryRoot 'src/Service.Tests/Azure.DataApiBuilder.Service.Tests.csproj'),
        '--configuration', $Configuration,
        '--filter', 'TestCategory=Oracle|TestCategory=OracleUnit',
        '--logger', 'trx;LogFileName=oracle.trx',
        '--collect', 'XPlat Code Coverage',
        '/p:UseAppHost=false'
    )
    if ($NoRestore) { $testArguments += '--no-restore' }
    if ($ResultsDirectory) { $testArguments += @('--results-directory', $ResultsDirectory) }
    if ($BaseOutputPath) { $testArguments += "/p:BaseOutputPath=$BaseOutputPath" }
    # Metadata regression tests create disposable views and INSTEAD OF triggers.
    "WHENEVER SQLERROR EXIT FAILURE`nALTER SESSION SET CONTAINER = XEPDB1;`nGRANT CREATE VIEW, CREATE TRIGGER TO DAB_TESTS;`nEXIT;" |
        docker exec --interactive $containerName sqlplus -s / as sysdba | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to grant privileges for disposable Oracle metadata tests.' }
    Write-Host 'Oracle is ready. Running provider and engine integration tests.'
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Oracle tests failed (exit code $LASTEXITCODE)." }
}
finally {
    if ($containerCreated) {
        docker rm --force --volumes $containerName | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove test container $containerName." }
    }
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}
