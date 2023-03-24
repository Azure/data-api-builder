# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

# This file validates the executables are passing the following basic checks:
# 1. Correct version of DAB is generated
# 2. HelpWindow gets displayed on the Console
# 3. Config file gets generated successfully.
param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $OsName,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

# Getting executable file path for DAB
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

$executableFileDirectory = "$BuildOutputDir/publish/$BuildConfiguration/$RID/dab"
$executableDAB = "$executableFileDirectory/Microsoft.DataApiBuilder"
$configFileName = "dab-config-smoke-test.json"

describe SmokeTest {
    it 'Check Version' {
        $ver = Invoke-expression "$executableDAB --version"
        Write-Host("installed-version: {$ver}")
        Write-Host("expected-version: {$DabVersion}")
        $ver.Contains("Microsoft.DataApiBuilder $DabVersion") | Should -Be True
    }

    it 'Check Command Help Window' {
        $helpText = Invoke-expression "$executableDAB --help"

        # Converting object[] to string
        $helpWritterOutput = ""
        foreach ($helpText in $helpText)
        {
            $helpWritterOutput += $helpText
        }

        # Verifying all the supported commands are displayed on the help window
        $helpWritterOutput.Contains("init") | Should -Be True
        $helpWritterOutput.Contains("add") | Should -Be True
        $helpWritterOutput.Contains("update") | Should -Be True
        $helpWritterOutput.Contains("start") | Should -Be True
    }

    it 'Check Config File is generated' {
        Invoke-expression "$executableDAB init -c $configFileName --database-type mssql --connection-string xxxx"
        Test-Path -Path $configFileName | Should -Be True
    }

    it 'Check Generated Config contains the correct path of dab schema' {
        if ($dabVersion.Contains("-"))
        {
            $dabVersion = $dabVersion.Substring(0, $dabVersion.IndexOf("-"));
        }

        $expectedSchemaPath = "https://github.com/Azure/data-api-builder/releases/download/v$dabVersion/dab.draft.schema.json";
        $parsedSchema = Get-Content -Raw -Path $configFileName | ConvertFrom-Json
        $genratedSchemaPath = $parsedSchema.'$schema'
        $genratedSchemaPath.Equals($expectedSchemaPath) | Should -Be True
    }
}

