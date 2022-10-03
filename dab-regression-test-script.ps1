param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

Invoke-expression "cd ./src/out/cli/$BuildConfiguration/"

ls -a

# Check version
$ver = Invoke-expression "./src/out/cli/$BuildConfiguration/*/dab --version"

describe MyTest {
    it 'verifies something' {
        $x.Contains("dab $DabVersion") | should be False
    }
}

