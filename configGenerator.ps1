$PSDefaultParameterValues['*:Encoding'] = 'utf8';

$commandFiles = "MsSqlCommands.txt", "MySqlCommands.txt", "PostgreSqlCommands.txt", "CosmosCommands.txt";
$pathToCLIBuildOutput = $PSScriptRoot + "\src\out\cli";

#Fetching the absolute path of dab.dll from build output directory
$pathToDabDLL = Get-ChildItem -Path $pathToCLIBuildOutput -Recurse -include "dab.dll" | ForEach-Object{$_.FullName};

foreach($file in $commandFiles)
{
    $commandsFileWithPath = $PSScriptRoot + "\" + $file;
    
    # Generating the config files using dab commands
    foreach($command in Get-Content $commandsFileWithPath){
        $commandToExecute = "dotnet " + $pathToDabDLL + " " + $command;
        Invoke-Expression $commandToExecute;
    }
}