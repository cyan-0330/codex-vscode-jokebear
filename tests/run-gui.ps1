$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot 'tmp\ConversationGuiTests.exe'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /utf8output /target:exe /main:ConversationGuiTests "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $sources (Join-Path $PSScriptRoot 'ConversationGuiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'GUI fixture compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'GUI fixture checks failed.' }
