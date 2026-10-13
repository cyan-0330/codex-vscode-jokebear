param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts' }
& (Join-Path $root 'build.ps1')
$version = (Get-Content -LiteralPath (Join-Path $root 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
$bundle = Join-Path $OutputDirectory ('bundle-' + $version)
if (Test-Path -LiteralPath $bundle) { throw 'Bundle staging directory already exists; choose a fresh output directory.' }
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
$files = @('install.ps1','install.cmd','start.ps1','start.cmd','review-hooks.cmd','install-hooks.ps1','uninstall-hooks.ps1','build.ps1','README.md','INSTALL_FOR_CODEX.md','AGENTS.md','LICENSE','THIRD_PARTY.md','dependencies.lock.json','package.json','bin\CodexPet.exe','bin\CodexPet.Hook.exe')
foreach ($folder in @('src','scripts','tests')) {
    $files += @(Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | Where-Object { $_.Extension -in '.cs','.ps1','.py' -and $_.FullName -notmatch '[\\/]tmp[\\/]' } | ForEach-Object { $_.FullName.Substring($root.Length + 1) })
}
foreach ($relative in $files) {
    $target = Join-Path $bundle $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
# The allowlist is the package boundary. Never include live config/data/logs.
$forbidden = @(Get-ChildItem -LiteralPath $bundle -Recurse -File | Where-Object { $_.Name -in 'runtime.json','hooks.json','config.toml','progress.json','recent-projects-seed.json','petdex-integration.json' -or $_.Extension -in '.sqlite','.log' })
if ($forbidden.Count) { throw 'Live user data was found in the bundle.' }
$zip = Join-Path $OutputDirectory ('codex-vscode-jokebear-' + $version + '-windows-x64.zip')
if (Test-Path -LiteralPath $zip) { throw 'Release archive already exists; refusing to overwrite.' }
Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'),($hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
Write-Host ('Package: ' + $zip)
Write-Host ('SHA-256: ' + $hash)
