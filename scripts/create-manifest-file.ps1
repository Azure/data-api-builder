# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion,
    [Parameter (Mandatory=$true)][string] $isReleaseBuild
)

$versionId = $DabVersion
$versionTag = "untagged" #untagged. non-release build will have no tag
$releaseType = "development"
$releaseDate = (Get-Date).ToUniversalTime().ToString('u')
$maxVersionCount = 100

if ($isReleaseBuild -eq 'true')
{
    $versionTag = "v" + $versionId
    $releaseType = "released"
}

# Generating hash for DAB packages
# TODO: Release-engineering - confirm the net10.0_{linux,win,osx}-x64 download
# URLs and SHA hashes referenced downstream still resolve after the .NET 10
# publish cycle runs. (Add a tracking issue number/link here once created.)
$dotnetTargetFrameworks = "net10.0"
$RIDs = "win-x64", "linux-x64", "osx-x64"
[hashtable]$frameworkPlatformDownloadMetadata = @{}
[hashtable]$frameworkPlatformFileHashMetadata = @{}

foreach ($targetFramework in $dotnetTargetFrameworks)
{
    foreach ($RID in $RIDs) {
        $fileName = "dab_${targetFramework}_${RID}-${DabVersion}.zip"
        $filePath = "$BuildOutputDir/publish/$BuildConfiguration/$targetFramework/$RID/$fileName"
        $download_url = "https://github.com/Azure/data-api-builder/releases/download/$versionTag/$fileName"
        $fileHashInfo = Get-FileHash $filePath
        $hash = $fileHashInfo.Hash
        $frameworkPlatformDownloadMetadata.Add("${targetFramework}_${RID}", $download_url)
        $frameworkPlatformFileHashMetadata.Add("${targetFramework}_${RID}", $hash)
    }
}

# Generating hash for nuget
$nugetCliFileName = "Microsoft.DataApiBuilder.$DabVersion.nupkg"
$nugetCliFilePath = "$BuildOutputDir/nupkg/$nugetCliFileName"
$fileCliHashInfo = Get-FileHash $nugetCliFilePath
$nuget_cli_file_hash = $fileCliHashInfo.Hash
$download_url_nuget_cli = "https://github.com/Azure/data-api-builder/releases/download/$versionTag/$nugetCliFileName"

$nugetCoreFileName = "Microsoft.DataApiBuilder.Core.$DabVersion.nupkg"
$nugetCoreFilePath = "$BuildOutputDir/nupkg/$nugetCoreFileName"
$fileCoreHashInfo = Get-FileHash $nugetCoreFilePath
$nuget_core_file_hash = $fileCoreHashInfo.Hash
$download_url_nuget_core = "https://github.com/Azure/data-api-builder/releases/download/$versionTag/$nugetCoreFileName"

# Creating new block to insert latest version 
# String substitution requires hashtable to be wrapped in $( $hashtable['key'] ) to avoid parsing issues.
$latestBlock = @'
{
    "version": "latest",
    "versionId": "${versionId}",
    "releaseType": "${releaseType}",
    "releaseDate": "${releaseDate}",
    "files": {
        "linux-x64":{
            "url": "$($frameworkPlatformDownloadMetadata["net10.0_linux-x64"])",
            "sha": "$($frameworkPlatformFileHashMetadata["net10.0_linux-x64"])"
        },
        "win-x64":{
            "url": "$($frameworkPlatformDownloadMetadata["net10.0_win-x64"])",
            "sha": "$($frameworkPlatformFileHashMetadata["net10.0_win-x64"])"
        },
        "osx-x64":{
            "url": "$($frameworkPlatformDownloadMetadata["net10.0_osx-x64"])",
            "sha": "$($frameworkPlatformFileHashMetadata["net10.0_osx-x64"])"
        },
        "nuget": {
            "url": "${download_url_nuget_cli}",
            "sha": "${nuget_cli_file_hash}"
        },
        "nuget-core": {
            "url": "${download_url_nuget_core}",
            "sha": "${nuget_core_file_hash}"
        }
    }
}
'@ 

$latestBlock = $ExecutionContext.InvokeCommand.ExpandString($latestBlock) | ConvertFrom-Json

# Get file content of the last released manifest file
$manifestFilePath = "$BuildOutputDir/dab-manifest.json"
$lastReleasedData = @()
if (Test-Path $manifestFilePath) {
    $lastReleasedData = Get-Content $manifestFilePath -raw | ConvertFrom-Json
}

# marking previous versions as old
# there will always be only 1 "latest" version
foreach($data in $lastReleasedData) {
    if($data.version -eq "latest") {
        $data.version = "old"
        break
    }
}

# Adding new block to the top of the list of released versions.
# Add the data from the last released manifest file.
$versionArray = @()
$versionArray += $latestBlock
$versionArray += $lastReleasedData

# Removing the oldest version if total count exceeds the max permissible count 
if($versionArray.Length -gt $maxVersionCount){ 
    $versionArray = [System.Collections.ArrayList]$versionArray 
    $versionArray.RemoveAt($versionArray.Count-1)
} 

# Updating the manifest file 
# Keeping Depth as 4, as by default ConvertTo-Json only support conversion till depth 2.
ConvertTo-Json -Depth 4 $versionArray | Out-File $BuildOutputDir/dab-manifest.json

