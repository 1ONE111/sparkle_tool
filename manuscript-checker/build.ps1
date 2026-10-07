$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $PSScriptRoot 'src\WongoChecker.cs'
$exe = Join-Path $PSScriptRoot '원고검수기.exe'
$ico = Join-Path $PSScriptRoot 'src\app.ico'
$opts = @('/nologo', '/codepage:65001', '/target:winexe', '/optimize+',
  '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Xml.dll',
  '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')
# 아이콘이 없으면 먼저 한 번 빌드해서 앱이 직접 아이콘을 그리게 한다
if (-not (Test-Path $ico)) {
  & $csc @opts "/out:$exe" $src
  if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
  Start-Process -FilePath $exe -ArgumentList '--make-icon', "`"$ico`"" -Wait
}
& $csc @opts "/win32icon:$ico" "/out:$exe" $src
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
"빌드 완료: $exe"
