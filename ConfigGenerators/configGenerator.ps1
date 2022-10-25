$PSDefaultParameterValues['*:Encoding'] = 'utf8';

$databaseTypes = @();
if($args.Count -eq 0){
    $databaseTypes = "MsSql", "MySql", "PostgreSql", "Cosmos";
}
elseif($args.Count -eq 1){
    $databaseType = $args[0];
    if(-not( ($databaseType -eq "MsSql") -or ($databaseType -eq "MySql") -or ($databaseType -eq "PostgreSql") -or ($databaseType -eq "Cosmos"))){
        throw "Valid arguements are MsSql, Mysql, PostgreSql or Cosmos";
    }
    $databaseTypes += $databaseType;
}
else{
    throw "Please run with 0 or 1 arguements";
}

$cliBuildOutputPath = $PSScriptRoot + "\..\src\out\cli\";

$commandsFilesBasePath = $PSScriptRoot;

#Fetching the absolute path of dab.dll from build output directory
$pathToDabDLL = Get-ChildItem -Path $cliBuildOutputPath -Recurse -include "dab.dll" | ForEach-Object{$_.FullName};

$workingDirectory = $PSScriptRoot + "\..\src\Service\";
Set-Location $workingDirectory;

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

    if(Test-Path $configFile){
        Remove-Item $configFile;
    }

    $commandsFileWithPath = $commandsFilesBasePath + "\" + $commandFile;
    #Generating the config files using dab commands
    foreach($command in Get-Content $commandsFileWithPath){
        $commandToExecute = "dotnet " + $pathToDabDLL + " " + $command;
        Invoke-Expression $commandToExecute;
    }

}
