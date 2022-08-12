#!/bin/bash
BuildRoot=$(dirname "$0")
BuildConfiguration=$1

echo "BuildRoot: $BuildRoot"

RIDs=("win-x64" "linux-x64" "osx-x64")

for RID in ${RIDs[@]}; do
    # Publish CLI
    cmd="dotnet publish --configuration $BuildConfiguration --output $BuildRoot/publish/$BuildConfiguration/$RID/cli --runtime $RID --self-contained true $BuildRoot/src/Cli/src/Cli.csproj"
    echo "Running: $cmd"
    eval $cmd

    pushd $BuildRoot/publish/$BuildConfiguration/$RID/cli
    cmd="zip -q -r ../cli.zip *"
    echo "Running: $cmd"
    eval $cmd
    popd
done
