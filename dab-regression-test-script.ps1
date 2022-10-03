param (
    [Parameter (Mandatory=$true)][string] $BuildConfiguration,
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

# Check version
$ver = Invoke-expression "./src/out/cli/$BuildConfiguration/net6.0/dab --version"

describe MyTest {
    it 'verifies something' {
        $ver.Contains("dab $DabVersion") | Should -Be True
    }
}

