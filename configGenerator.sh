#!/bin/bash
commandFiles=("MsSqlCommands.txt" "MySqlCommands.txt" "PostgreSqlCommands.txt" "CosmosCommands.txt")
#Fetching absolute path of this script
absolutePath="$( cd "$(dirname "$0")" ; pwd -P )";
cliOutputPath="$absolutePath/src/out/cli";
#Fetching the path of dab dll file
pathToDLL=$(find $cliOutputPath -name dab.dll)
workingDirectory="$absolutePath/src/Service/"

configFiles=("dab-config.MsSql.json" "dab-config.MySql.json" "dab-config.PostgreSql.json" "dab-config.Cosmos.json")

# During start-up engine looks for config files inside /src/Service directory.
cd $workingDirectory;

# Deleting existing config files
for configFile in "${configFiles[@]}"
do
    if test -f "$configFile"; then
        eval "rm ${configFile}"
    fi
done

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