# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

#!/bin/bash
# This script can be used for generating the config files
databaseTypes=();

# This script can be invoked with either 0 or 1 argument.
# The argument represents the database type. Valid arguments are MsSql, MySql, PostgreSql and Cosmos
# When invoked with a database type, config file for that database type will be generated.
# When invoked without any arguments, config files for all the database types will be generated.

allowedDbTypes=("mssql" "mysql" "postgresql" "cosmosdb_nosql" "dwsql")
databaseTypes=()

if [[ $# -eq 0 ]]; then
    databaseTypes=("${allowedDbTypes[@]}")
elif [[ $# -eq 1 ]]; then
    databaseType=$1;
    if [[! " ${allowedDbTypes[@]} " =~ " ${databaseType} " ]]; then
        echo "Valid arguments are mssql, mysql, postgresql, cosmosdb_nosql, or dwsql";
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
pathToDLL=$(find $cliOutputPath -name Microsoft.DataApiBuilder.dll | head -n 1)

#Change the working directory to where the config file needs to be generated.
workingDirectory="$absolutePath/../src/Service.Tests/"
cd $workingDirectory;

#Generates the config files for the selected database types.
for databaseType in ${databaseTypes[@]}
do
    if [[ $databaseType == "mssql" ]]; then 
        commandFile="mssql-commands.txt";
        configFile="dab-config.MsSql.json";
    elif [[ $databaseType == "mysql" ]]; then 
        commandFile="mysql-commands.txt";
        configFile="dab-config.MySql.json";
    elif [[ $databaseType == "postgresql" ]]; then
        commandFile="postgresql-commands.txt";
        configFile="dab-config.PostgreSql.json";
    elif [[ $databaseType == "dwsql" ]]; then
        commandFile="dwsql-commands.txt";
        configFile="dab-config.DwSql.json";
    else 
        commandFile="cosmosdb_nosql-commands.txt";
        configFile="dab-config.CosmosDb_NoSql.json";
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

    # Post-process MsSql and DwSql config files to set stored procedures as query operations
    if [[ $databaseType == "mssql" || $databaseType == "dwsql" ]]; then
        # Use jq to modify the operation field for GetBooks and GetPublisher
        if command -v jq &> /dev/null; then
            tmp_file="${configFile}.tmp"
            jq '
                if .entities.GetBooks then
                    .entities.GetBooks.graphql.operation = "query"
                else . end |
                if .entities.GetPublisher then
                    .entities.GetPublisher.graphql.operation = "query"
                else . end
            ' "$configFile" > "$tmp_file" && mv "$tmp_file" "$configFile"
        else
            echo "Warning: jq not found. Skipping stored procedure operation post-processing."
        fi
    fi

done
