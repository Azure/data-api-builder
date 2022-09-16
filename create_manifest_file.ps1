param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

# TODO: To update the URL dynamically
$versionId = $DabVersion
$releaseType = "development"
$releaseDate = (Get-Date).ToUniversalTime().ToString('u')
$download_url_win = "https://github.com/data-api-builder/releases/$versionId/win-file.zip"
$download_url_linux = "https://github.com/data-api-builder/releases/$versionId/linux-file.zip"
$download_url_osx = "https://github.com/data-api-builder/releases/$versionId/osx-file.zip"
$maxVersionCount = 3

# Generating hash for DAB packages
$RIDs = "win-x64", "linux-x64", "osx-x64"
foreach ($RID in $RIDs) {
    $filePath = "$BuildOutputDir/publish/$BuildConfiguration/$RID/dab_$DabVersion.zip";
    $fileHashInfo = Get-FileHash $filePath
    $hash = $fileHashInfo.Hash
    switch ($RID) {
        "win-x64"{ $win_file_hash = $hash}
        "linux-x64"{ $linux_file_hash = $hash}
        "osx-x64"{ $osx_file_hash = $hash}
    }
}

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
        }
    }
}
'@ 

$latestBlock = $ExecutionContext.InvokeCommand.ExpandString($latestBlock) | ConvertFrom-Json 

# Adding new block to the top of the list of released versions.
# TODO: To use the data from the current manifest file and update it.
$versionArray = '[]' | ConvertFrom-Json 
$versionArray += $latestBlock

# Removing the oldest version if total count exceeds the max permissible count 
if($versionArray.Length -gt $maxVersionCount){ 
    $versionArray = [System.Collections.ArrayList]$versionArray 
    $versionArray.RemoveAt($versionArray.Count-1)
} 

# Updating the manifest file 
# Keeping Depth as 4, as by default ConvertTo-Json only support copnversion till depth 2.
$versionArray | ConvertTo-Json -Depth 4 | Out-File $BuildOutputDir/dab-manifest.json

