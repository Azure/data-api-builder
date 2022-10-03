param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $OsName,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

# Dab executable
$file = "dab"
if ($OsName -eq "Linux")
{
    $RID = "linux-x64"
}
elseif ($OsName -eq "Windows_NT"){
    # for Windows
    $RID = "win-x64"
}

$executableDAB = "$BuildOutputDir/publish/$BuildConfiguration/$RID/dab/$file"

describe RegressionTest {
    it 'Check Version' {
        $ver = Invoke-expression "$executableDAB --version"
        $ver.Contains("dab $DabVersion") | Should -Be True
    }

    it 'Check Command Help Window' {
        $helpTexts = Invoke-expression "$executableDAB --help"

        # Converting to object[] to string
        $helpWritterOutput = ""
        foreach ($helpText in $helpTexts)
        {
            $helpWritterOutput += $helpTexts
        }

        # Verifying all the supported commands are displayed on the help window
        $helpWritterOutput.Contains("init") | Should -Be True
        $helpWritterOutput.Contains("add") | Should -Be True
        $helpWritterOutput.Contains("update") | Should -Be True
        $helpWritterOutput.Contains("start") | Should -Be True
    }

    it 'Check Config File is generated' {
        $configFileName = "dab-config-regression-test.json"
        Invoke-expression "$executableDAB init -c $configFileName --database-type mssql --connection-string xxxx"
        Test-Path -Path $configFileName | Should -Be True
    }
}

