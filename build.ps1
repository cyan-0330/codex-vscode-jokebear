param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'bin' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = $OutputDirectory
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$refs = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll'
$sources = @(Get-ChildItem -LiteralPath "$PSScriptRoot\src" -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /utf8output /target:winexe "/out:$bin\CodexPet.exe" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Pet compilation failed' }
& $compiler /nologo /utf8output /target:exe "/out:$bin\CodexPet.Hook.exe" $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Hook compilation failed' }
Write-Host "Built $bin\CodexPet.exe"
