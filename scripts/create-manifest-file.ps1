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
$RIDs = "win-x64", "linux-x64", "osx-x64"
foreach ($RID in $RIDs) {
    $fileName = "dab_$RID-$DabVersion.zip"
    $filePath = "$BuildOutputDir/publish/$BuildConfiguration/$RID/$fileName"
    $download_url = "https://github.com/Azure/data-api-builder/releases/download/$versionTag/$fileName"
    $fileHashInfo = Get-FileHash $filePath
    $hash = $fileHashInfo.Hash
    switch ($RID) {
        "win-x64"{
            $win_file_hash = $hash
            $download_url_win = $download_url
        }
        "linux-x64"{
            $linux_file_hash = $hash
            $download_url_linux = $download_url
        }
        "osx-x64"{ 
            $osx_file_hash = $hash
            $download_url_osx = $download_url
        }
    }
}

# Generating hash for nuget
$nugetFileName = "Microsoft.DataApiBuilder.$DabVersion.nupkg"
$nugetFilePath = "$BuildOutputDir/nupkg/$nugetFileName"
$fileHashInfo = Get-FileHash $nugetFilePath
$nuget_file_hash = $fileHashInfo.Hash
$download_url_nuget = "https://github.com/Azure/data-api-builder/releases/download/$versionTag/$nugetFileName"

# Creating new block to insert latest version 
$latestBlock = @'
{
    "version": "latest",
    "versionId": "${versionId}",
    "releaseType": "${releaseType}",
    "releaseDate": "${releaseDate}",
    "files": {
        "linux-x64":{
            "url": "${download_url_linux}",
            "sha": "${linux_file_hash}"
        },
        "win-x64":{
            "url": "${download_url_win}",
            "sha": "${win_file_hash}"
        },
        "osx-x64":{
            "url": "${download_url_osx}",
            "sha": "${osx_file_hash}"
        },
        "nuget": {
            "url": "${download_url_nuget}",
            "sha": "${nuget_file_hash}"
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

