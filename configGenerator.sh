#!/bin/bash
commandFiles=("MsSqlCommands.txt" "MySqlCommands.txt" "PostgreSqlCommands.txt" "CosmosCommands.txt")
#Fetching absolute path of this script
absolutePath="$( cd "$(dirname "$0")" ; pwd -P )";
cliOutputPath="$absolutePath/src/out/cli";
#Fetching the path of dab dll file
pathToDLL=$(find $cliOutputPath -name dab.dll)
workingDirectory="$absolutePath/src/Service/"
# During start-up engine looks for config files inside /src/Service directory.
cd $workingDirectory;
# Generating the config using dab commands
echo "Generating config file using dab commands";
for file in "${commandFiles[@]}"
do
    commandsFileNameWithPath="$absolutePath/$file";
    while read -r command; do
        command="dotnet ${pathToDLL} ${command}";
        eval $command;
    done <$commandsFileNameWithPath;
done