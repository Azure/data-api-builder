# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# This script can be used for generating the config files
$PSDefaultParameterValues['*:Encoding'] = 'utf8';

# This script can be invoked with either 0 or 1 argument.
# The argument represents the database type. Valid arguments are MsSql, MySql, PostgreSql and Cosmos
# When invoked with a database type, config file for that database type will be generated.
# When invoked without any arguments, config files for all the database types will be generated.
$allowedDbTypes = @("mssql", "mysql", "postgresql", "cosmosdb_nosql", "dwsql");
$databaseTypes = @();
if($args.Count -eq 0){
    $databaseTypes = $allowedDbTypes
}
elseif($args.Count -eq 1){
    $databaseType = $args[0];
    if(!($allowedDbTypes -contains $databaseType)){
        throw "Valid arguments are mssql, mysql, postgresql, cosmosdb_nosql or dwsql";
    }
    $databaseTypes += $databaseType;
}
else{
    throw "Please run with 0 or 1 arguments";
}

$cliBuildOutputPath = $PSScriptRoot + "\..\src\out\cli\";
$commandsFilesBasePath = $PSScriptRoot;

#Fetching the absolute path of Microsoft.DataApiBuilder.dll from build output directory
$pathToDabDLL = Get-ChildItem -Path $cliBuildOutputPath -Recurse -include "Microsoft.DataApiBuilder.dll" | Select-Object -ExpandProperty FullName -First 1

#Change the working directory to where the config file needs to be generated.
$workingDirectory = $PSScriptRoot + "\..\src\Service.Tests\";
Set-Location $workingDirectory;

#Generates the config files for the selected database types.
foreach($databaseType in $databaseTypes){
    if($databaseType -eq "mssql"){
        $commandFile = "mssql-commands.txt";
        $configFile = "dab-config.MsSql.json";
    }
    elseif($databaseType -eq "mysql"){
        $commandFile = "mysql-commands.txt";
        $configFile = "dab-config.MySql.json";
    }
    elseif($databaseType -eq "postgresql"){
        $commandFile = "postgresql-commands.txt";
        $configFile = "dab-config.PostgreSql.json";
    }
    elseif($databaseType -eq "dwsql"){
        $commandFile = "dwsql-commands.txt";
        $configFile = "dab-config.DwSql.json";
    }
    else{
        $commandFile = "cosmosdb_nosql-commands.txt";
        $configFile = "dab-config.CosmosDb_NoSql.json";
    }

    # If a config file with the same name exists, it is deleted to avoid writing to
    # the same config file
    if(Test-Path $configFile){
        Remove-Item $configFile;
    }

    $commandsFileWithPath = $commandsFilesBasePath + "\" + $commandFile;

    #The dab commands are run using the DLL executable
    foreach($command in Get-Content $commandsFileWithPath){
        $commandToExecute = "dotnet " + $pathToDabDLL + " " + $command;
        Invoke-Expression $commandToExecute;
    }

    # Post-process MsSql config to fix stored procedure GraphQL operations
    # The CLI currently ignores --graphql.operation parameter for stored procedures,
    # defaulting them to 'mutation'. We manually fix specific procedures that should be 'query'.
    if($databaseType -eq "mssql"){
        $configContent = Get-Content $configFile -Raw | ConvertFrom-Json;
        if($configContent.entities.GetBooks){
            $configContent.entities.GetBooks.graphql.operation = "query";
        }
        if($configContent.entities.GetPublisher){
            $configContent.entities.GetPublisher.graphql.operation = "query";
        }
        $configContent | ConvertTo-Json -Depth 100 | Set-Content $configFile -Encoding UTF8;
    }
}
