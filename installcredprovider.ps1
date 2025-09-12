<#
.SYNOPSIS
    Installs the Azure Artifacts Credential Provider for DotNet or NuGet tool usage.

.DESCRIPTION
    This script installs the latest version of the Azure Artifacts Credential Provider plugin
    for DotNet and/or NuGet to the ~/.nuget/plugins directory.

.PARAMETER AddNetfx
    Installs the .NET Framework 4.6.1 Credential Provider.

.PARAMETER AddNetfx48
    Installs the .NET Framework 4.8.1 Credential Provider.

.PARAMETER Force
    Forces overwriting of existing Credential Provider installations.

.PARAMETER Version
    Specifies the GitHub release version of the Credential Provider to install.

.PARAMETER InstallNet6
    Installs the .NET 6 Credential Provider (default).

.PARAMETER InstallNet8
    Installs the .NET 8 Credential Provider.

.PARAMETER RuntimeIdentifier
    Installs the self-contained Credential Provider for the specified Runtime Identifier.

.EXAMPLE
    .\installcredprovider.ps1 -InstallNet8
    .\installcredprovider.ps1 -Version "1.0.1" -Force
#>

[CmdletBinding(HelpUri = "https://github.com/microsoft/artifacts-credprovider/blob/master/README.md#setup")]
param(
    [switch]$AddNetfx,
    [switch]$AddNetfx48,
    [switch]$Force,
    [string]$Version,
    [switch]$InstallNet6 = $true,
    [switch]$InstallNet8,
    [string]$RuntimeIdentifier
)

$script:ErrorActionPreference = 'Stop'

# Without this, System.Net.WebClient.DownloadFile will fail on a client with TLS 1.0/1.1 disabled
if ([Net.ServicePointManager]::SecurityProtocol.ToString().Split(',').Trim() -notcontains 'Tls12') {
    [Net.ServicePointManager]::SecurityProtocol += [Net.SecurityProtocolType]::Tls12
}

if ($Version.StartsWith("0.") -and $InstallNet6 -eq $True) {
    Write-Error "You cannot install the .Net 6 version with versions lower than 1.0.0"
    return
}
if (($Version.StartsWith("0.") -or $Version.StartsWith("1.0") -or $Version.StartsWith("1.1") -or $Version.StartsWith("1.2")) -and 
    ($InstallNet8 -eq $True -or $AddNetfx48 -eq $True)) {
    Write-Error "You cannot install the .Net 8 or NetFX 4.8.1 version or with versions lower than 1.3.0"
    return
}
if ($AddNetfx -eq $True -and $AddNetfx48 -eq $True) {
    Write-Error "Please select a single .Net framework version to install"
    return
}
if (![string]::IsNullOrEmpty($RuntimeIdentifier)) {
    if (($Version.StartsWith("0.") -or $Version.StartsWith("1.0") -or $Version.StartsWith("1.1") -or $Version.StartsWith("1.2") -or $Version.StartsWith("1.3"))) {
        Write-Error "You cannot install the .Net 8 self-contained version or with versions lower than 1.4.0"
        return
    }

    Write-Host "RuntimeIdentifier parameter is specified, the .Net 8 self-contained version will be installed"
    $InstallNet6 = $False
    $InstallNet8 = $True
}
if ($InstallNet6 -eq $True -and $InstallNet8 -eq $True) {
    # InstallNet6 defaults to true, in the case of .Net 8 install, overwrite
    $InstallNet6 = $False
}

$userProfilePath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile);
if ($userProfilePath -ne '') {
    $profilePath = $userProfilePath
}
else {
    $profilePath = $env:UserProfile
}

$tempPath = [System.IO.Path]::GetTempPath()

$pluginLocation = [System.IO.Path]::Combine($profilePath, ".nuget", "plugins");
$tempZipLocation = [System.IO.Path]::Combine($tempPath, "CredProviderZip");

$localNetcoreCredProviderPath = [System.IO.Path]::Combine("netcore", "CredentialProvider.Microsoft");
$localNetfxCredProviderPath = [System.IO.Path]::Combine("netfx", "CredentialProvider.Microsoft");

$fullNetfxCredProviderPath = [System.IO.Path]::Combine($pluginLocation, $localNetfxCredProviderPath)
$fullNetcoreCredProviderPath = [System.IO.Path]::Combine($pluginLocation, $localNetcoreCredProviderPath)

$netfxExists = Test-Path -Path ($fullNetfxCredProviderPath)
$netcoreExists = Test-Path -Path ($fullNetcoreCredProviderPath)

# Check if plugin already exists if -Force swich is not set
if (!$Force) {
    if ($AddNetfx -eq $True -and $netfxExists -eq $True -and $netcoreExists -eq $True) {
        Write-Host "The netcore and netfx Credential Providers are already in $pluginLocation"
        return
    }

    if ($AddNetfx -eq $False -and $netcoreExists -eq $True) {
        Write-Host "The netcore Credential Provider is already in $pluginLocation"
        return
    }
}

# Get the zip file from the GitHub release
$releaseUrlBase = "https://api.github.com/repos/Microsoft/artifacts-credprovider/releases"
$versionError = "Unable to find the release version $Version from $releaseUrlBase"
$releaseId = "latest"
if (![string]::IsNullOrEmpty($Version)) {
    try {
        $releases = Invoke-WebRequest -UseBasicParsing $releaseUrlBase
        $releaseJson = $releases | ConvertFrom-Json
        $correctReleaseVersion = $releaseJson | ? { $_.name -eq $Version }
        $releaseId = $correctReleaseVersion.id
    }
    catch {
        Write-Error $versionError
        return
    }
}

if (!$releaseId) {
    Write-Error $versionError
    return
}

$releaseUrl = [System.IO.Path]::Combine($releaseUrlBase, $releaseId)
$releaseUrl = $releaseUrl.Replace("\", "/")

$releaseRidPart = ""
if (![string]::IsNullOrEmpty($RuntimeIdentifier)) {
    $releaseRIdPart = $RuntimeIdentifier + "."
}

if ($Version.StartsWith("0.")) {
    # versions lower than 1.0.0 installed NetCore2 zip
    $zipFile = "Microsoft.NetCore2.NuGet.CredentialProvider.zip"
}
if ($InstallNet6 -eq $True) {
    $zipFile = "Microsoft.Net6.NuGet.CredentialProvider.zip"
}
if ($InstallNet8 -eq $True) {
    $zipFile = "Microsoft.Net8.${releaseRidPart}NuGet.CredentialProvider.zip"
}
if ($AddNetfx -eq $True) {
    Write-Warning "The .Net Framework 4.6.1 version of the Credential Provider is deprecated and will be removed in the next major release. Please migrate to the .Net Framework 4.8 or .Net Core versions."
    $zipFile = "Microsoft.NuGet.CredentialProvider.zip"
}
if ($AddNetfx48 -eq $True) {
    $zipFile = "Microsoft.NetFx48.NuGet.CredentialProvider.zip"
}
if (-not $zipFile) {
    Write-Warning "The .Net Core 3.1 version of the Credential Provider is deprecated and will be removed in the next major release. Please migrate to the .Net 8 version."
    $zipFile = "Microsoft.NetCore3.NuGet.CredentialProvider.zip"
}

function InstallZip {
    Write-Verbose "Using $zipFile"

    try {
        Write-Host "Fetching release $releaseUrl"
        $release = Invoke-WebRequest -UseBasicParsing $releaseUrl
        if (!$release) {
            throw ("Unable to make Web Request to $releaseUrl")
        }
        $releaseJson = $release.Content | ConvertFrom-Json
        if (!$releaseJson) {
            throw ("Unable to get content from JSON")
        }
        $zipAsset = $releaseJson.assets | ? { $_.name -eq $zipFile }
        if (!$zipAsset) {
            throw ("Unable to find asset $zipFile from release json object")
        }
        $packageSourceUrl = $zipAsset.browser_download_url
        if (!$packageSourceUrl) {
            throw ("Unable to find download url from asset $zipAsset")
        }
    }
    catch {
        Write-Error ("Unable to resolve the browser download url from $releaseUrl `nError: " + $_.Exception.Message)
        return
    }

    # Create temporary location for the zip file handling
    Write-Verbose "Creating temp directory for the Credential Provider zip: $tempZipLocation"
    if (Test-Path -Path $tempZipLocation) {
        Remove-Item $tempZipLocation -Force -Recurse
    }
    New-Item -ItemType Directory -Force -Path $tempZipLocation

    # Download credential provider zip to the temp location
    $pluginZip = ([System.IO.Path]::Combine($tempZipLocation, $zipFile))
    Write-Host "Downloading $packageSourceUrl to $pluginZip"
    try {
        $client = New-Object System.Net.WebClient
        $client.DownloadFile($packageSourceUrl, $pluginZip)
    }
    catch {
        Write-Error "Unable to download $packageSourceUrl to the location $pluginZip"
    }

    # Extract zip to temp directory
    Write-Host "Extracting zip to the Credential Provider temp directory $tempZipLocation"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($pluginZip, $tempZipLocation)
}

# Call InstallZip function
InstallZip

# Remove existing content and copy netfx directories to plugins directory
if ($AddNetfx -eq $True -or $AddNetfx48 -eq $True) {
    if ($netfxExists) {
        Write-Verbose "Removing existing content from $fullNetfxCredProviderPath"
        Remove-Item $fullNetfxCredProviderPath -Force -Recurse
    }
    $tempNetfxPath = [System.IO.Path]::Combine($tempZipLocation, "plugins", $localNetfxCredProviderPath)
    Write-Verbose "Copying Credential Provider from $tempNetfxPath to $fullNetfxCredProviderPath"
    Copy-Item $tempNetfxPath -Destination $fullNetfxCredProviderPath -Force -Recurse
}

# Microsoft.NuGet.CredentialProvider.zip that installs netfx provider installs .netcore3.1 version
# If InstallNet6 is also true we need to replace netcore cred provider with net6
if ($AddNetfx -eq $True -and $InstallNet6 -eq $True) {
    $zipFile = "Microsoft.Net6.NuGet.CredentialProvider.zip"
    Write-Verbose "Installing Net6"
    InstallZip
}
if ($AddNetfx -eq $True -and $InstallNet8 -eq $True) {
    $zipFile = "Microsoft.Net8.NuGet.CredentialProvider.zip"
    Write-Verbose "Installing Net8"
    InstallZip
}

# Remove existing content and copy netcore directories to plugins directory
if ($netcoreExists) {
    Write-Verbose "Removing existing content from $fullNetcoreCredProviderPath"
    Remove-Item $fullNetcoreCredProviderPath -Force -Recurse
}
$tempNetcorePath = [System.IO.Path]::Combine($tempZipLocation, "plugins", $localNetcoreCredProviderPath)
Write-Verbose "Copying Credential Provider from $tempNetcorePath to $fullNetcoreCredProviderPath"
Copy-Item $tempNetcorePath -Destination $fullNetcoreCredProviderPath -Force -Recurse

# Remove $tempZipLocation directory
Write-Verbose "Removing the Credential Provider temp directory $tempZipLocation"
Remove-Item $tempZipLocation -Force -Recurse

Write-Host "Credential Provider installed successfully"