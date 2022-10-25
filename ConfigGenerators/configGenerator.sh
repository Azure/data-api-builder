#!/bin/bash
databaseTypes=();

if [[ $# -eq 0 ]]; then
    databaseTypes=("MsSql" "MySql" "PostgreSql" "Cosmos")
elif [[ $# -eq 1 ]]; then
    databaseType=$1;
    if ! { [ $databaseType == "MsSql" ] || [ $databaseType == "MySql" ] || [ $databaseType == "PostgreSql" ] || [ $databaseType == "Cosmos" ]; }; then
        echo "Valid arguments are MsSql, Mysql, PostgreSql or Cosmos";
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

#Fetching the path of dab dll file
pathToDLL=$(find $cliOutputPath -name dab.dll)
workingDirectory="$absolutePath/../src/Service/"

cd $workingDirectory;

for databaseType in ${databaseTypes[@]}
do
    if [[ $databaseType == "MsSql" ]]; then 
        commandFile="MsSqlCommands.txt";
        configFile="dab-config.MsSql.json";
    elif [[ $databaseType == "MySql" ]]; then 
        commandFile="MySqlCommands.txt";
        configFile="dab-config.MySql.json";
    elif [[ $databaseType == "PostgreSql" ]]; then
        commandFile="PostgreSqlCommands.txt";
        configFile="dab-config.PostgreSql.json";
    else 
        commandFile="CosmosCommands.txt";
        configFile="dab-config.Cosmos.json";
    fi

    if [ -f $configFile ]; then
        rm $configFile;
    fi

    commandFileNameWithPath="$commandFilesBasePath/$commandFile";
    
    while read -r command; do
        command="dotnet ${pathToDLL} ${command}";
        eval $command;
    done <$commandFileNameWithPath;

done
