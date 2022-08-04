
# Absolute path this script is in, thus /home/user/bin
BuildRoot=$(dirname "$0")
BuildConfiguration=$1

RIDs=("win-x64" "linux-x64" "osx-x64")

for RID in ${RIDs[@]}; do
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/engine --runtime $RID ./DataGateway.Service/Azure.DataGateway.Service.csproj
    dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/cli --runtime $RID ./Hawaii-Cli/src/Hawaii.Cli.csproj
done
