param([string]$CodexDir = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }))
$ErrorActionPreference = 'Stop'
$path = Join-Path $CodexDir 'hooks.json'
if (-not (Test-Path -LiteralPath $path)) { Write-Host 'No hooks file.'; exit 0 }
$config = Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
if (-not $config.hooks) { exit 0 }
foreach ($property in @($config.hooks.PSObject.Properties)) {
    $groups = @()
    foreach ($group in @($property.Value)) {
        $kept = @($group.hooks | Where-Object { $_.command -notlike '*CodexPet.Hook.exe*' -and $_.commandWindows -notlike '*CodexPet.Hook.exe*' })
        if ($kept.Count) { $group.hooks = $kept; $groups += $group }
    }
    $config.hooks.($property.Name) = @($groups)
}
$output = $config | ConvertTo-Json -Depth 100
$temp = $path + '.tmp-' + [guid]::NewGuid().ToString('N')
$backup = $path + '.backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
[IO.File]::WriteAllText($temp, $output + [Environment]::NewLine, (New-Object Text.UTF8Encoding $false))
[IO.File]::Replace($temp, $path, $backup)
Write-Host "Removed Codex Desktop Pet hooks. Backup: $backup"
