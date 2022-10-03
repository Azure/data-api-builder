param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

Invoke-expression "cd ./$BuildOutputDir/out/cli/$BuildConfiguration/net6.0/"

ls -a

# Check version
$ver = Invoke-expression "./$BuildOutputDir/out/cli/$BuildConfiguration/*/dab --version"

describe MyTest {
    it 'verifies something' {
        $x.Contains("dab $DabVersion") | should be False
    }
}

