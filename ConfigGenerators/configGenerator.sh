#!/bin/bash
# This script can be used for generating the config files
databaseTypes=();

# This script can be invoked with either 0 or 1 argument.
# The argument represents the database type. Valid arguments are MsSql, MySql, PostgreSql and Cosmos
# When invoked with a database type, config file for that database type will be generated.
# When invoked without any arguments, config files for all the database types will be generated.
if [[ $# -eq 0 ]]; then
    databaseTypes=("mssql" "mysql" "postgresql" "cosmosdb_nosql")
elif [[ $# -eq 1 ]]; then
    databaseType=$1;
    if ! { [ $databaseType == "mssql" ] || [ $databaseType == "mysql" ] || [ $databaseType == "postgresql" ] || [ $databaseType == "cosmos" ]; }; then
        echo "Valid arguments are mssql, mysql, postgresql or cosmosdb_nosql";
        exit 1;
    fi
    databaseTypes+=$databaseType;    
else
    echo "Please run with 0 or 1 arguments";
    exit 1;
fi

#Fetching absolute path of this script
absolutePath="$( cd "$(dirname "$0")" ; pwd -P )";
cliOutputPath="$absolutePath/../src/out/cli";
commandFilesBasePath=$absolutePath;

#Finding the path of dab dll file, piped to `head` to pick up only the first match.
pathToDLL=$(find $cliOutputPath -name dab.dll | head -n 1)

#Change the working directory to where the config file needs to be generated.
workingDirectory="$absolutePath/../src/Service/"
cd $workingDirectory;

#Generates the config files for the selected database types.
for databaseType in ${databaseTypes[@]}
do
    if [[ $databaseType == "mssql" ]]; then 
        commandFile="MsSqlCommands.txt";
        configFile="dab-config.MsSql.json";
    elif [[ $databaseType == "mysql" ]]; then 
        commandFile="MySqlCommands.txt";
        configFile="dab-config.MySql.json";
    elif [[ $databaseType == "postgresql" ]]; then
        commandFile="PostgreSqlCommands.txt";
        configFile="dab-config.PostgreSql.json";
    else 
        commandFile="CosmosCommands.txt";
        configFile="dab-config.Cosmos.json";
    fi

    # If a config file with the same name exists, it is deleted to avoid writing to
    # the same config file
    if [ -f $configFile ]; then
        rm $configFile;
    fi

    commandFileNameWithPath="$commandFilesBasePath/$commandFile";
    
    #The dab commands are run using the DLL executable
    while read -r command; do
        command="dotnet ${pathToDLL} ${command}";
        eval $command;
    done <$commandFileNameWithPath;

done
