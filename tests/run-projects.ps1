$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot 'tmp\ProjectTests.exe'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs' | ForEach-Object { $_.FullName })
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /utf8output /target:exe /main:ProjectTests "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $sources "$PSScriptRoot\ProjectTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Project test compilation failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Project tests failed' }
