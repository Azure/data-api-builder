# This script can be used for generating the config files
$PSDefaultParameterValues['*:Encoding'] = 'utf8';

# This script can be invoked with either 0 or 1 argument.
# The argument represents the database type. Valid arguments are MsSql, MySql, PostgreSql and Cosmos
# When invoked with a database type, config file for that database type will be generated.
# When invoked without any arguments, config files for all the database types will be generated.
$databaseTypes = @();
if($args.Count -eq 0){
    $databaseTypes = "mssql", "mysql", "postgresql", "cosmosdb_nosql";
}
elseif($args.Count -eq 1){
    $databaseType = $args[0];
    if(-not( ($databaseType -eq "mssql") -or ($databaseType -eq "mysql") -or ($databaseType -eq "postgresql") -or ($databaseType -eq "cosmosdb_nosql"))){
        throw "Valid arguments are mssql, mysql, postgresql or cosmosdb_nosql";
    }
    $databaseTypes += $databaseType;
}
else{
    throw "Please run with 0 or 1 arguments";
}

$cliBuildOutputPath = $PSScriptRoot + "\..\src\out\cli\";
$commandsFilesBasePath = $PSScriptRoot;

#Fetching the absolute path of dab.dll from build output directory
$pathToDabDLL = Get-ChildItem -Path $cliBuildOutputPath -Recurse -include "dab.dll" | Select-Object -ExpandProperty FullName -First 1

#Change the working directory to where the config file needs to be generated.
$workingDirectory = $PSScriptRoot + "\..\src\Service\";
Set-Location $workingDirectory;

#Generates the config files for the selected database types.
foreach($databaseType in $databaseTypes){
    if($databaseType -eq "mssql"){
        $commandFile = "MsSqlCommands.txt";
        $configFile = "dab-config.MsSql.json";
    }
    elseif($databaseType -eq "mysql"){
        $commandFile = "MySqlCommands.txt";
        $configFile = "dab-config.MySql.json";
    }
    elseif($databaseType -eq "postgresql"){
        $commandFile = "PostgreSqlCommands.txt";
        $configFile = "dab-config.PostgreSql.json";
    }
    else{
        $commandFile = "CosmosCommands.txt";
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
}
