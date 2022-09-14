param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

$versionId = $DabVersion # should be picked up from pipeline
$releaseType = "released"
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

# Get file content and convert to powershell object
#$currentData = Get-Content D:/dab/manifest.json -raw | ConvertFrom-Json 

# Updating the most recent latest as old
# foreach($data in $currentData)
# {
#     if($data.version -eq "latest")
#     {
#         $data.version = "old"
#     }
#     break
# } 

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

# # Adding new block to the top of the list of released versions. 
$versionArray = '[]' | ConvertFrom-Json 
$versionArray += $latestBlock 
# $versionArray += $currentData 

# # Removing the oldest version if total count exceeds the max permissible count 
if($versionArray.Length -gt $maxVersionCount){ 
    $versionArray = [System.Collections.ArrayList]$versionArray 
    $versionArray.RemoveAt($versionArray.Count-1)
} 
$x = $versionArray | ConvertTo-Json -Depth 4
Write-Host $x
# # Updating the manifest file 
$versionArray | ConvertTo-Json -Depth 4 | Out-File manifest.json 