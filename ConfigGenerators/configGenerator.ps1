# This script can be used for generating the config files
$PSDefaultParameterValues['*:Encoding'] = 'utf8';

# This script can be invoked with either 0 or 1 argument.
# The argument represents the database type. Valid arguments are MsSql, MySql, PostgreSql and Cosmos
# When invoked with a database type, config file for that database type will be generated.
# When invoked without any arguments, config files for all the database types will be generated.
$databaseTypes = @();
if($args.Count -eq 0){
    $databaseTypes = "MsSql", "MySql", "PostgreSql", "Cosmos";
}
elseif($args.Count -eq 1){
    $databaseType = $args[0];
    if(-not( ($databaseType -eq "MsSql") -or ($databaseType -eq "MySql") -or ($databaseType -eq "PostgreSql") -or ($databaseType -eq "Cosmos"))){
        throw "Valid arguments are MsSql, Mysql, PostgreSql or Cosmos";
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
    if($databaseType -eq "MsSql"){
        $commandFile = "MsSqlCommands.txt";
        $configFile = "dab-config.MsSql.json";
    }
    elseif($databaseType -eq "MySql"){
        $commandFile = "MySqlCommands.txt";
        $configFile = "dab-config.MySql.json";
    }
    elseif($databaseType -eq "PostgreSql"){
        $commandFile = "PostgreSqlCommands.txt";
        $configFile = "dab-config.PostgreSql.json";
    }
    else{
        $commandFile = "CosmosCommands.txt";
        $configFile = "dab-config.Cosmos.json";
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
        Write-Output $commandToExecute
        Invoke-Expression $commandToExecute;
    }
}
