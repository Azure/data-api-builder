#!/bin/bash
BuildRoot=$(dirname "$0")
BuildConfiguration=$1

RIDs=("win-x64" "linux-x64" "osx-x64")

for RID in ${RIDs[@]}; do
    # Publish engine
    cmd="dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/engine --runtime $RID --no-self-contained $BuildRoot/DataGateway.Service/Azure.DataGateway.Service.csproj"
    echo "Running: $cmd"
    eval $cmd

    # Publish CLI
    cmd="dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/cli --runtime $RID --no-self-contained $BuildRoot/Hawaii-Cli/src/Hawaii.Cli.csproj"
    echo "Running: $cmd"
    eval $cmd
done
