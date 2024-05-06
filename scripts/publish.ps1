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
$dotnetTargetFrameworks = "net6.0", "net8.0"
$RIDs = "win-x64", "linux-x64", "osx-x64"

# Runs dotnet publish for each target framework and RID.
# Example results:
# \dotnetpublishout\publish\Release\net8.0\win-x64\dab
# \dotnetpublishout\publish\Release\net6.0\win-x64\dab
if ($Package)
{
    foreach ($targetFramework in $dotnetTargetFrameworks)
    {
        foreach ($RID in $RIDs) {
            $cmd = "dotnet publish --framework $targetFramework --configuration $BuildConfiguration --output $BuildOutputDir/publish/$BuildConfiguration/$targetFramework/$RID/dab --runtime $RID --self-contained true -p:Version=$DabVersion $BuildRoot/src/Cli/Cli.csproj"
            Write-Host $cmd
            Invoke-Expression $cmd
        }
    }   
}

# Zips the published output for each target framework and RID.
# For example:
# \dotnetpublishout\publish\Release\net8.0\win-x64\dab_net8.0_win-x64-0.14.123-rc.zip
# \dotnetpublishout\publish\Release\net6.0\win-x64\dab_net6.0_win-x64-0.14.123-rc.zip
if ($CreateZip)
{
    foreach ($targetFramework in $dotnetTargetFrameworks)
    {
        foreach ($RID in $RIDs) {
            $filesToZipPath = "$BuildOutputDir/publish/$BuildConfiguration/$targetFramework/$RID/dab/*"
            $archiveOutputPath = "$BuildOutputDir/publish/$BuildConfiguration/$targetFramework/$RID/dab_${targetFramework}_${RID}-${DabVersion}.zip"
            $cmd = "Compress-Archive -Force -Path $filesToZipPath -DestinationPath $archiveOutputPath"
            Write-Host $cmd
            Invoke-Expression $cmd
        }
    }
}
