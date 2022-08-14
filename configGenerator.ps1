param(
    [Parameter (Mandatory=$true)][string] $databaseType
)

$fileName = $databaseType + "Commands.txt";
$pathToDabExe = Get-ChildItem -Path .\src\out\cli\ -Recurse -include "dab.exe" | %{$_.FullName} ;

# Generating the config files using dab commands
foreach($command in Get-Content $fileName){
    $commandToExecute = $pathToDabExe + " " + $command;
    Invoke-Expression $commandToExecute;
}
