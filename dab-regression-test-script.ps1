param (
    [Parameter (Mandatory=$true)][string] $BuildOutputDir,
    [Parameter (Mandatory=$true)][string] $DabVersion
)

ls -a

# Check version
$ver = ./$BuildOutputDir/dab.exe --version

describe MyTest {
    it 'verifies something' {
        $x.Contains("dab $DabVersion") | should be False
    }
}

