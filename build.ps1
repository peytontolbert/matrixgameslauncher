param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'), [switch]$Test, [string]$NativeSessionTest)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 compiler is required.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$archiveTool = Join-Path $PSScriptRoot 'vendor/7zip/7za.exe'
if ((Get-FileHash -LiteralPath $archiveTool -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'dd3757f31c97e33776f2590317d4fb12f2da075db3dbb1e3f94d0f2a01d84ffa') { throw 'Bundled archive tool checksum differs' }
$resources = @("/resource:$archiveTool,Matrix.7za.exe", "/resource:$PSScriptRoot/vendor/7zip/License.txt,Matrix.7zip.License.txt")
& $compiler @resources /nologo /target:winexe /optimize+ /platform:x64 "/out:$output\MatrixGamesLauncher.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
if ($Test) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler @resources /nologo /target:exe /main:LauncherTests /platform:x64 "/out:$output\LauncherTests.exe" @references @sources @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$output\LauncherTests.exe" (Join-Path $output 'test-scratch')
    if ($LASTEXITCODE -ne 0) { throw 'Launcher tests failed' }
}
if($NativeSessionTest){
    & $compiler @resources /nologo /target:exe /main:MatrixFrameworkTests /platform:x64 "/out:$output\MatrixFrameworkTests.exe" @references @sources (Join-Path $PSScriptRoot 'tests\MatrixFrameworkTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Matrix runtime test compilation failed'}
    & "$output\MatrixFrameworkTests.exe" (Join-Path $output 'framework-fixtures') ([IO.Path]::GetFullPath($NativeSessionTest))
    if($LASTEXITCODE -ne 0){throw 'Matrix runtime handoff tests failed'}
}
