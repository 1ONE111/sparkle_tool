param([string]$OutputName = '블로거서쳐.exe')
$ErrorActionPreference = 'Stop'
$taskCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskSource = Join-Path $PSScriptRoot 'src\BlogSearcher.cs'
$taskOutput = Join-Path $PSScriptRoot $OutputName
$taskIcon = Join-Path $PSScriptRoot 'assets\app.ico'
$taskBrand = Join-Path $PSScriptRoot 'assets\app.png'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /win32icon:$taskIcon "/resource:$taskBrand,BlogSearcher.Brand.png" /out:$taskOutput $taskSource
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
