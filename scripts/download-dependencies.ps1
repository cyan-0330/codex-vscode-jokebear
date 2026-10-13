param([string]$CacheDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment.ps1')
$root = Split-Path -Parent $PSScriptRoot
if (!$CacheDirectory) { $CacheDirectory = Join-Path $root '.cache' }
$lock = Get-Content -LiteralPath (Join-Path $root 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($name in @('python','petdex','sprite')) { $dependency = $lock.$name; Get-VerifiedDownload -Dependency $dependency -Destination (Join-Path $CacheDirectory $dependency.fileName) }
Write-Host ('Verified dependency cache: ' + $CacheDirectory)
