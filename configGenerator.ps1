param(
    [Parameter (Mandatory=$true)][string] $databaseType
)

$commandsFileName = $databaseType + "Commands.txt";
$commandsFileWithPath = $PSScriptRoot + "\" + $commandsFileName;
$pathToDabExe = Get-ChildItem -Path .\src\out\cli\ -Recurse -include "dab.exe" | %{$_.FullName} ;

# Generating the config files using dab commands
foreach($command in Get-Content $commandsFileWithPath){
    $commandToExecute = $pathToDabExe + " " + $command;
    Invoke-Expression $commandToExecute;
}
