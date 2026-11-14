param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$Development)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sdk=Join-Path ([IO.Path]::GetFullPath($SdkDirectory)) 'ScriptHookVDotNet3.dll'
if(!(Test-Path -LiteralPath $sdk)){throw 'ScriptHookVDotNet3.dll from the pinned official SDK is required'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$defines=@();if($Development){$defines+='/define:MATRIX_GTA_DEVELOPMENT'}
& $compiler /nologo /target:library /optimize+ /platform:x64 "/out:$output\MatrixGtaClient.dll" "/r:$sdk" /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll @defines (Join-Path $PSScriptRoot 'MatrixTransport.cs') (Join-Path $PSScriptRoot 'MatrixGtaClient.cs')
if($LASTEXITCODE -ne 0){throw 'Matrix GTA client compilation failed'}
Get-FileHash -LiteralPath (Join-Path $output 'MatrixGtaClient.dll') -Algorithm SHA256
