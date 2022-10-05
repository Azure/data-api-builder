# This file is used to check the nuget file and executables are passing the basic checks:
# 1. Correct version
# 2. HelpWindow is displaying on Console
# 3. File is correctly generated.
param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $OsName,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

# Getting executable file path for DAB
$file = "dab"
switch ($OsName) {
    "Windows_NT"{
        $RID = "win-x64"
    }
    "Linux"{
        $RID = "linux-x64"
    }
    "Darwin"{ 
        $RID = "osx-x64"
    }
}

# Install dab nuget
$installCommand = "dotnet tool install -g --add-source $BuildOutputDir/nupkg dab --version $DabVersion"
Invoke-Expression $installCommand

Write-Host Invoke-Expression "dab --version"

$executableDAB = "$BuildOutputDir/publish/$BuildConfiguration/$RID/dab/$file"

describe SmokeTest {
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

