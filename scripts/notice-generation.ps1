# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param (
    [Parameter (Mandatory=$true)][string] $BuildArtifactStagingDir,
    [Parameter (Mandatory=$true)][string] $BuildSourcesDir
)

# Download and save the ChilliCream License 1.0 from ChilliCream/graphql-platform GitHub repo
$chiliCreamLicenseSavePath = "$BuildArtifactStagingDir/chillicreamlicense.txt"
$chiliCreamLicenseMetadataURL = "https://raw.githubusercontent.com/ChilliCream/graphql-platform/main/website/src/basic/licensing/chillicream-license.md"
Invoke-WebRequest -Uri $chiliCreamLicenseMetadataURL -UseBasicParsing |
 Select-Object -ExpandProperty Content |
 Out-File $chiliCreamLicenseSavePath

# Download and save the Microsoft.Data.SqlClient.SNI.runtime license
$sqlClientSNILicenseSavePath = "$BuildArtifactStagingDir/sqlclient_sni_runtime.txt"
$sqlClientSNILicenseMetadataURL = "https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/5.0.1/License"
$pageContent = Invoke-WebRequest $sqlClientSNILicenseMetadataURL

# Regular expression with three capture groups.
# Capture Group 1: HTML tag which indicates start of license text
# Named Capture Group licenseText: License text. Match across many lines with regex modifier (?s)
# Capture Group 3: HTML tag which indicates end of license text.
$licenseRegex = '(<pre class="license-file-contents custom-license-container">)(?<licenseText>(?s).*)(<\/pre>)'
$pageContent -match $licenseRegex
$sqlClientSNILicense = $Matches.licenseText

# Path of notice file generated in CI/CD pipeline.
$noticeFilePath = "$BuildSourcesDir/NOTICE.txt"

# Replace erroneous copyright, using [System.IO.File] for better performance than Get-Content and Set-Content
$content = [System.IO.File]::ReadAllText($noticeFilePath).Replace("(c) Microsoft 2023`r`n", "")

# Prepare license content for writing to file.
$sqlClientSNIComponentName = "`r`nMICROSOFT.DATA.SQLCLIENT.SNI`r`n`r`n"
$bananaCakePopComponentName = "`r`n`r`nBanana Cake Pop`r`n`r`n"
$chiliCreamLicenseText = [System.IO.File]::ReadAllText($chiliCreamLicenseSavePath)

# Combine all notice file components and write to file. Each license is separated
# by two full lines of dashes, where after each line of dashes, there is an empty line.
# The autogenerated NOTICE text ends with a single line of dashes, so the second must be added here.
$licenseSeparator = "`r`n`r`n---------------------------------------------------------`r`n"
$finalOutputContent = $content + $licenseSeparator + $sqlClientSNIComponentName + $sqlClientSNILicense + $licenseSeparator + $licenseSeparator + $bananaCakePopComponentName + $chiliCreamLicenseText
[System.IO.File]::WriteAllText($noticeFilePath, $finalOutputContent)
