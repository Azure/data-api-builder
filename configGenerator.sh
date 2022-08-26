#!/bin/bash
commandFiles=("MsSqlCommands.txt" "MySqlCommands.txt" "PostgreSqlCommands.txt" "CosmosCommands.txt")
absolutePath="$( cd "$(dirname "$0")" ; pwd -P )";
cliOutputPath="$absolutePath/src/out/cli";
#Fetching the path to dab dll
pathToDLL=$(find $cliOutputPath -name dab.dll)
#Generating the config using dab commands
echo "Generating config file using dab commands";
for file in "${commandFiles[@]}"
do
    commandsFileNameWithPath="$absolutePath/$file";
    while read -r command; do
        cmd="dotnet ${pathToDLL} ${command}";
        eval $cmd;
    done <$commandsFileNameWithPath;
done