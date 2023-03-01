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
# Add newline before and after to meet formatting requirements
Add-Content $noticeFilePath -Value "`r`nBanana Cake Pop`r`n"
Add-Content $noticeFilePath -Value "Copyright 2023 ChilliCream, Inc.`r`n"
Add-Content $noticeFilePath -Value (Get-Content $chiliCreamLicenseSavePath)
