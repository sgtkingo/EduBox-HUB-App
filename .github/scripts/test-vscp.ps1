param([string]$CompilerPath)
$ErrorActionPreference = 'Stop'
if (!$CompilerPath) {
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $CompilerPath = & $vswhere -latest -products '*' -find 'MSBuild/**/Roslyn/csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'Roslyn C# compiler not found.' }
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $root
$testExe = 'NewGUI/bin/Release/VscpProtocolTests.exe'
try {
    & $CompilerPath /nologo /platform:x64 /r:NewGUI/bin/Release/NewGUI.exe "/out:$testExe" tests/VscpProtocolTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'VSCP test compilation failed.' }
    & "./$testExe"
    if ($LASTEXITCODE -ne 0) { throw 'VSCP protocol checks failed.' }
}
finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
    Pop-Location
}
