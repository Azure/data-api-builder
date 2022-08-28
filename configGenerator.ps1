$PSDefaultParameterValues['*:Encoding'] = 'utf8';

$commandFiles = "MsSqlCommands.txt", "MySqlCommands.txt", "PostgreSqlCommands.txt", "CosmosCommands.txt";
$workingDirectory = $PSScriptRoot + "\src\Service\";
# During start-up engine looks for config files inside /src/Service directory.
Set-Location $workingDirectory;

$cliBuildOutputPath = $PSScriptRoot + "\src\out\cli\";

#Fetching the absolute path of dab.dll from build output directory
$pathToDabDLL = Get-ChildItem -Path $cliBuildOutputPath -Recurse -include "dab.dll" | ForEach-Object{$_.FullName};

foreach($file in $commandFiles)
{
    $commandsFileWithPath = $PSScriptRoot + "\" + $file;
    
    # Generating the config files using dab commands
    foreach($command in Get-Content $commandsFileWithPath){
        $commandToExecute = "dotnet " + $pathToDabDLL + " " + $command;
        Invoke-Expression $commandToExecute;
    }
}