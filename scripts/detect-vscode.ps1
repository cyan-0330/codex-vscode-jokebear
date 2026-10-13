$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment.ps1')
$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'petdex-integration.json'
$config = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
$knownExtension = $null
if ($config.extensionDirectory) {
    $knownRoot = Split-Path -Parent $config.extensionDirectory
    if (Test-Path -LiteralPath $knownRoot) { $newest = Get-ChildItem -LiteralPath $knownRoot -Directory -Filter 'openai.chatgpt-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($newest) { $knownExtension = $newest.FullName } }
    if (!$knownExtension -and (Test-Path -LiteralPath $config.extensionDirectory)) { $knownExtension = $config.extensionDirectory }
}
$detected = Get-VscodeEnvironment -CodeExecutable $config.codeExecutable -ExtensionDirectory $knownExtension
foreach ($field in @('CodeExecutable','ExtensionDirectory','ExtensionVersion','ThreadRouteSupported')) { $config | Add-Member -NotePropertyName ($field.Substring(0,1).ToLowerInvariant()+$field.Substring(1)) -NotePropertyValue $detected.$field -Force }
$config | Add-Member -NotePropertyName vscodeOnly -NotePropertyValue $true -Force
if ($env:CODEX_HOME) { $config.codexHome = $env:CODEX_HOME }
[IO.File]::WriteAllText($path,($config | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Host ('VS Code Codex ' + $config.extensionVersion + ': ' + $config.codeExecutable)
