$ErrorActionPreference = 'Stop'
$taskCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskSource = Join-Path $PSScriptRoot 'src\BlogSearcher.cs'
$taskOutput = Join-Path $PSScriptRoot '블로거서쳐.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /out:$taskOutput $taskSource
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
