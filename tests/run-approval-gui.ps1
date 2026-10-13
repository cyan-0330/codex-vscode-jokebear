$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temporary = Join-Path $PSScriptRoot 'tmp'
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
& (Join-Path $root 'build.ps1') -OutputDirectory $temporary
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$output = Join-Path $temporary 'GuiTests.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /utf8output /target:exe /main:GuiTests "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $sources (Join-Path $PSScriptRoot 'GuiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Approval GUI compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Synthetic approval buttons failed.' }
