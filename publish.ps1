param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration
    [Parameter (Mandatory=$true)][string] $BuildOutputDir
    [Parameter (Mandatory=$true)][string] $DabVersion
)

$BuildRoot = $PSScriptRoot

$RIDs = "win-x64", "linux-x64", "osx-x64"

foreach ($RID in $RIDs) {
    dotnet publish --configuration $BuildConfiguration --output $BuildOutputDir/publish/$BuildConfiguration/$RID/cli --runtime $RID --self-contained true -p:Version=$DabVersion $BuildRoot\src\Cli\src\Cli.csproj

    Compress-Archive -Force -Path $BuildOutputDir/publish/$BuildConfiguration/$RID/cli/* -DestinationPath $BuildRoot/publish/$BuildConfiguration/$RID/cli_$DabVersion.zip
}
