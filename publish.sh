#!/bin/bash
BuildRoot=$(dirname "$0")
BuildConfiguration=$1
BuildOutputDir=$2
DabVersion=$3

echo "BuildRoot: $BuildRoot"

RIDs=("win-x64" "linux-x64" "osx-x64")

for RID in ${RIDs[@]}; do
    # Publish CLI
    cmd="dotnet publish --configuration $BuildConfiguration --output $BuildOutputDir/publish/$BuildConfiguration/$RID/cli --runtime $RID --self-contained true -p:Version=$DabVersion $BuildRoot/src/Cli/src/Cli.csproj"
    echo "Running: $cmd"
    eval $cmd

    pushd $BuildOutputDir/publish/$BuildConfiguration/$RID/cli
    cmd="zip -q -r ../cli_$DabVersion.zip *"
    echo "Running: $cmd"
    eval $cmd
    popd
done
