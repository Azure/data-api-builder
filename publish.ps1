param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration
)

$BuildRoot = $PSScriptRoot

$RIDs = "win-x64", "linux-x64", "osx-x64"

foreach ($RID in $RIDs) {
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/engine --runtime $RID $BuildRoot\DataGateway.Service\Azure.DataGateway.Service.csproj
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/cli --runtime $RID $BuildRoot\Hawaii-Cli\src\Hawaii.Cli.csproj

    Compress-Archive -Force -Path $BuildRoot/publish/$BuildConfiguration/$RID/engine/* -DestinationPath $BuildRoot/publish/$BuildConfiguration/$RID/engine.zip
    Compress-Archive -Force -Path $BuildRoot/publish/$BuildConfiguration/$RID/cli/* -DestinationPath $BuildRoot/publish/$BuildConfiguration/$RID/cli.zip
}
