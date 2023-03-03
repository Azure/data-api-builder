# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion,
    [Parameter (Mandatory=$false)][switch] $Package,
    [Parameter (Mandatory=$false)][switch] $CreateZip
)

$BuildRoot = Split-Path $PSScriptRoot -Parent

$RIDs = "win-x64", "linux-x64", "osx-x64"

if ($Package)
{
    foreach ($RID in $RIDs) {
        $cmd = "dotnet publish --configuration $BuildConfiguration --output $BuildOutputDir/publish/$BuildConfiguration/$RID/dab --runtime $RID --self-contained true -p:Version=$DabVersion $BuildRoot/src/Cli/Cli.csproj"
        Write-Host $cmd
        Invoke-Expression $cmd
    }
}

if ($CreateZip)
{
    foreach ($RID in $RIDs) {
        $cmd = "Compress-Archive -Force -Path $BuildOutputDir/publish/$BuildConfiguration/$RID/dab/* -DestinationPath $BuildOutputDir/publish/$BuildConfiguration/$RID/dab_$RID-$DabVersion.zip"
        Write-Host $cmd
        Invoke-Expression $cmd
    }
}
