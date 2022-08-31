param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration
    [Parameter (Mandatory=$true)][string] $BuildOutputDir
    [Parameter (Mandatory=$true)][string] $DabVersion
    [Parameter (Mandatory=$false)][switch] $Package
    [Parameter (Mandatory=$false)][switch] $CreateZip
)

$BuildRoot = $PSScriptRoot

$RIDs = "win-x64", "linux-x64", "osx-x64"

if ($Package)
{
    foreach ($RID in $RIDs) {
        dotnet publish --configuration $BuildConfiguration --output $BuildOutputDir/publish/$BuildConfiguration/$RID/cli --runtime $RID --self-contained true -p:Version=$DabVersion $BuildRoot\src\Cli\src\Cli.csproj
    }
}

if ($CreateZip)
{
    foreach ($RID in $RIDs) {
        Compress-Archive -Force -Path $BuildOutputDir/publish/$BuildConfiguration/$RID/cli/* -DestinationPath $BuildRoot/publish/$BuildConfiguration/$RID/cli_$DabVersion.zip
    }
}
