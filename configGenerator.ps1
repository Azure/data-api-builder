param(
    [Parameter (Mandatory=$true)][string] $databaseType
)

$PSDefaultParameterValues['*:Encoding'] = 'utf8';

$commandsFileName = $databaseType + "Commands.txt";
$commandsFileWithPath = $PSScriptRoot + "\" + $commandsFileName;
$pathToCLIBuildOutput = $PSScriptRoot + "\src\out\cli";
$pathToDabExe = Get-ChildItem -Path $pathToCLIBuildOutput -Recurse -include "dab.exe" | %{$_.FullName} ;

# Generating the config files using dab commands
foreach($command in Get-Content $commandsFileWithPath){
    $commandToExecute = $pathToDabExe + " " + $command;
    Invoke-Expression $commandToExecute;
}
