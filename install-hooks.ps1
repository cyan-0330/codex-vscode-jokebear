param([string]$CodexDir = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }))
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\CodexPet.Hook.exe'
if (-not (Test-Path -LiteralPath $exe)) { & "$PSScriptRoot\build.ps1" }
if (-not (Test-Path -LiteralPath $exe)) { throw 'Hook executable is missing.' }
New-Item -ItemType Directory -Path $CodexDir -Force | Out-Null
$path = Join-Path $CodexDir 'hooks.json'
$config = if (Test-Path -LiteralPath $path) { Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json } else { [pscustomobject]@{} }
if ($null -eq $config -or $config -is [array]) { throw 'hooks.json must be an object.' }
if (-not $config.PSObject.Properties['hooks']) { $config | Add-Member NoteProperty hooks ([pscustomobject]@{}) }
if ($null -eq $config.hooks -or $config.hooks -is [array] -or $config.hooks -is [string]) { throw 'hooks must be an object.' }
# The Windows hook shell uses PowerShell. No command interpolation of tool input.
$command = "& '" + $exe.Replace("'", "''") + "' --hook"
$events = 'PermissionRequest', 'SessionStart', 'UserPromptSubmit', 'PreToolUse', 'Stop', 'Interrupt', 'SessionEnd'
foreach ($event in $events) {
    $groups = @()
    if ($config.hooks.PSObject.Properties[$event]) {
        foreach ($group in @($config.hooks.$event)) {
            $kept = @($group.hooks | Where-Object { $_.command -notlike '*CodexPet.Hook.exe*' -and $_.commandWindows -notlike '*CodexPet.Hook.exe*' })
            if ($kept.Count) { $group.hooks = $kept; $groups += $group }
        }
    }
    $timeout = if ($event -eq 'PermissionRequest') { 600 } elseif ($event -in 'Interrupt', 'SessionEnd') { 3 } else { 5 }
    $handler = [ordered]@{ type = 'command'; command = $command; commandWindows = $command; timeout = $timeout }
    $groups += [pscustomobject]@{ hooks = @([pscustomobject]$handler) }
    if ($config.hooks.PSObject.Properties[$event]) { $config.hooks.$event = @($groups) } else { $config.hooks | Add-Member NoteProperty $event @($groups) }
}
$output = $config | ConvertTo-Json -Depth 100
if ((Test-Path -LiteralPath $path) -and (Get-Content -Raw -Encoding UTF8 -LiteralPath $path).Trim() -eq $output.Trim()) {
    Write-Host 'Hooks already installed; nothing changed.'
    exit 0
}
$temp = Join-Path $CodexDir ('hooks.json.tmp-' + [guid]::NewGuid().ToString('N'))
[IO.File]::WriteAllText($temp, $output + [Environment]::NewLine, (New-Object Text.UTF8Encoding $false))
if (Test-Path -LiteralPath $path) {
    $backup = $path + '.backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
    [IO.File]::Replace($temp, $path, $backup)
    Write-Host "Backup: $backup"
} else { [IO.File]::Move($temp, $path) }
Write-Host "Installed hooks: $path"
Write-Host 'Next: review and trust these hooks in Codex /hooks, then reload VS Code.'
Write-Host 'This installer does not bypass trust or change approval/sandbox settings.'
