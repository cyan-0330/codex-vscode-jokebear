$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $root 'petdex-integration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$extension = $config.extensionDirectory
if (!$extension -or !(Test-Path -LiteralPath $extension)) { throw 'Run install.cmd first; the VS Code Codex extension was not found.' }
$codex = Join-Path $extension 'bin\windows-x86_64\codex.exe'
if (!(Test-Path -LiteralPath $codex)) { throw 'The installed VS Code extension does not contain the Windows Codex runtime. Update the extension.' }
Write-Host 'Type /hooks, review the CodexPet.Hook.exe PermissionRequest handler, and trust it.'
Write-Host 'Then exit this review session and reload your VS Code window.'
Write-Host 'No approval policy or hook trust records are changed by this script.'
$reviewStart = New-Object Diagnostics.ProcessStartInfo
$reviewStart.FileName = $codex
$reviewStart.Arguments = '--no-daemon --no-alt-screen'
$reviewStart.WorkingDirectory = $root
$reviewStart.UseShellExecute = $false
$reviewStart.EnvironmentVariables['CODEX_HOME'] = $config.codexHome
$reviewProcess = [Diagnostics.Process]::Start($reviewStart)
$reviewProcess.WaitForExit()
if ($reviewProcess.ExitCode -ne 0) { throw 'The Codex review session did not exit successfully.' }
