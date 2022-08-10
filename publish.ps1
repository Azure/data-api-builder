param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration
)

$BuildRoot = $PSScriptRoot

$RIDs = "win-x64", "linux-x64", "osx-x64"

foreach ($RID in $RIDs) {
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/cli --runtime $RID --self-contained true $BuildRoot\src\Hawaii-Cli\src\Hawaii.Cli.csproj

    Compress-Archive -Force -Path $BuildRoot/publish/$BuildConfiguration/$RID/cli/* -DestinationPath $BuildRoot/publish/$BuildConfiguration/$RID/cli.zip
}
