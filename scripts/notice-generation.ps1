# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param (
    [Parameter (Mandatory=$true)][string] $BuildArtifactStagingDir,
    [Parameter (Mandatory=$true)][string] $BuildSourcesDir
)

# Download and save the ChilliCream License 1.0 from ChilliCream/graphql-platform GitHub repo
$chiliCreamLicenseSavePath = "$BuildArtifactStagingDir/chillicreamlicense.txt"
$chiliCreamLicenseMetadataURL = "https://raw.githubusercontent.com/ChilliCream/graphql-platform/main/website/src/basic/licensing/chillicream-license.md"
Invoke-WebRequest $chiliCreamLicenseMetadataURL `
| Select-Object -ExpandProperty Content `
| Out-File $chiliCreamLicenseSavePath

# Concatenate existing NOTICE.txt file with Chilicream license.
$noticeFilePath = "$BuildSourcesDir/NOTICE.txt"

# Replace erroneous copyright, using [System.IO.File] for better performance than Get-Content and Set-Content
$content = [System.IO.File]::ReadAllText($noticeFilePath).Replace("(c) Microsoft 2023`n", "")
$chiliCreamLicenseText = [System.IO.File]::ReadAllText($chiliCreamLicenseSavePath)
$bananaCakePopCopyright = "`r`nBanana Cake Pop`r`n`r`nCopyright 2023 ChilliCream, Inc.`r`n`r`n"

# Combine all notice file components and write to file.
$finalOutputContent = $content + $bananaCakePopCopyright + $chiliCreamLicenseText
[System.IO.File]::WriteAllText($noticeFilePath, $finalOutputContent)
