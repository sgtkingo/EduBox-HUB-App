param(
    [ValidateSet('none', 'major', 'minor', 'patch', 'build')]
    [string]$Bump = 'none'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$versionPath = Join-Path $root 'VERSION'
$current = (Get-Content -LiteralPath $versionPath -Raw -Encoding utf8).Trim()
if ($current -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Invalid VERSION '$current'; expected major.minor.patch.build."
}
$parts = @($current.Split('.') | ForEach-Object { [int]$_ })
if ($Bump -ne 'none') {
    $index = @{ major = 0; minor = 1; patch = 2; build = 3 }[$Bump]
    $parts[$index]++
    for ($i = $index + 1; $i -lt 4; $i++) { $parts[$i] = 0 }
}
if (@($parts | Where-Object { $_ -gt 65534 }).Count) {
    throw 'Version components must not exceed 65534 (.NET assembly limit).'
}
$version = $parts -join '.'
[IO.File]::WriteAllText($versionPath, "$version`n", [Text.UTF8Encoding]::new($false))
$assemblyPath = Join-Path $root 'NewGUI/Properties/AssemblyInfo.cs'
$assembly = Get-Content -LiteralPath $assemblyPath -Raw -Encoding utf8
foreach ($attribute in 'AssemblyVersion', 'AssemblyFileVersion') {
    $pattern = '\[assembly: ' + $attribute + '\("[^"]+"\)\]'
    if ($assembly -notmatch $pattern) { throw "Missing $attribute in AssemblyInfo.cs." }
    $assembly = [regex]::Replace($assembly, $pattern, ('[assembly: ' + $attribute + '("' + $version + '")]'))
}
[IO.File]::WriteAllText($assemblyPath, $assembly, [Text.UTF8Encoding]::new($false))
if ($env:GITHUB_OUTPUT) {
    "version=$version" >> $env:GITHUB_OUTPUT
    "tag=v$version" >> $env:GITHUB_OUTPUT
}
Write-Host "Version: $version"
