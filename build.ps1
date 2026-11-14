param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'), [switch]$Test, [string]$NativeSessionTest)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 compiler is required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /target:winexe /optimize+ /platform:x64 "/out:$output\MatrixGamesLauncher.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
if ($Test) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler /nologo /target:exe /main:LauncherTests /platform:x64 "/out:$output\LauncherTests.exe" @references @sources @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$output\LauncherTests.exe" (Join-Path $output 'test-scratch')
    if ($LASTEXITCODE -ne 0) { throw 'Launcher tests failed' }
}
if($NativeSessionTest){
    & $compiler /nologo /target:exe /main:MatrixFrameworkTests /platform:x64 "/out:$output\MatrixFrameworkTests.exe" @references @sources (Join-Path $PSScriptRoot 'tests\MatrixFrameworkTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Matrix runtime test compilation failed'}
    & "$output\MatrixFrameworkTests.exe" (Join-Path $output 'framework-fixtures') ([IO.Path]::GetFullPath($NativeSessionTest))
    if($LASTEXITCODE -ne 0){throw 'Matrix runtime handoff tests failed'}
}
