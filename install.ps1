param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'CodexJokeBearPet'),
    [string]$CodexDirectory = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }),
    [string]$PetdexHome = (Join-Path $env:USERPROFILE '.petdex'),
    [string]$CodeExecutable,
    [string]$ExtensionDirectory,
    [string]$DependencyCache,
    [switch]$PrepareOnly,
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or ![Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
. (Join-Path $PSScriptRoot 'scripts\environment.ps1')
$editor = Get-VscodeEnvironment -CodeExecutable $CodeExecutable -ExtensionDirectory $ExtensionDirectory
if (!$editor.ThreadRouteSupported) { throw 'Update the official VS Code Codex extension: this build does not expose the tested conversation route.' }
$source = [IO.Path]::GetFullPath($PSScriptRoot)
$destination = [IO.Path]::GetFullPath($InstallDirectory)
if ($destination -eq [IO.Path]::GetPathRoot($destination)) { throw 'Use a dedicated application directory, not a drive root.' }
if ($destination -ne $source -and (Test-Path -LiteralPath $destination)) {
    $marker = Join-Path $destination 'package.json'
    if (@(Get-ChildItem -LiteralPath $destination -Force).Count -gt 0) {
        if (!(Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json).name -ne 'codex-vscode-jokebear') { throw 'Destination is not empty and is not an existing installation of this package.' }
    }
}
if (!$PrepareOnly -and !$NoStart) {
    foreach ($process in @(Get-Process -Name CodexPet -ErrorAction SilentlyContinue)) {
        if ($process.Path -and $process.Path -ne (Join-Path $destination 'bin\CodexPet.exe')) { throw 'Exit the existing JokeBear approval companion from its tray before installing into a different directory.' }
    }
}
$lock = Get-Content -LiteralPath (Join-Path $source 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (!(Test-Path -LiteralPath (Join-Path $source 'bin\CodexPet.exe'))) { & (Join-Path $source 'build.ps1') }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
if ($destination -ne $source) {
    $rootFiles = @('install.ps1','install.cmd','start.ps1','start.cmd','review-hooks.cmd','install-hooks.ps1','uninstall-hooks.ps1','build.ps1','README.md','INSTALL_FOR_CODEX.md','AGENTS.md','LICENSE','THIRD_PARTY.md','dependencies.lock.json','package.json')
    foreach ($relative in $rootFiles) { Copy-Item -LiteralPath (Join-Path $source $relative) -Destination (Join-Path $destination $relative) -Force }
    foreach ($folder in @('src','scripts','tests')) {
        foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $source $folder) -Recurse -File | Where-Object { $_.Extension -in '.cs','.ps1','.py' -and $_.FullName -notmatch '[\\/]tmp[\\/]' })) {
            $relative = $file.FullName.Substring($source.Length + 1)
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    New-Item -ItemType Directory -Path (Join-Path $destination 'bin') -Force | Out-Null
    foreach ($name in @('CodexPet.exe','CodexPet.Hook.exe')) {
        $from = Join-Path $source ('bin\' + $name); $to = Join-Path $destination ('bin\' + $name)
        if (!(Test-Path -LiteralPath $to) -or (Get-FileHash -LiteralPath $from).Hash -ne (Get-FileHash -LiteralPath $to).Hash) { Copy-Item -LiteralPath $from -Destination $to -Force }
    }
}
$cache = Join-Path $destination '.cache'
$pythonArchive = Join-Path $cache $lock.python.fileName
Get-VerifiedDownload -Dependency $lock.python -Destination $pythonArchive -CacheDirectory $DependencyCache
$pythonRoot = Join-Path $destination ('runtime\python-' + $lock.python.version)
$python = Join-Path $pythonRoot 'python.exe'
New-Item -ItemType Directory -Path $pythonRoot -Force | Out-Null
if (!(Test-Path -LiteralPath $python)) { Expand-Archive -LiteralPath $pythonArchive -DestinationPath $pythonRoot -Force }
& $python -c "import sqlite3,json; print('Python SQLite runtime ready')"
if ($LASTEXITCODE -ne 0) { throw 'The embedded Python SQLite runtime failed verification.' }
$native = Join-Path $destination 'petdex-runtime\petdex-desktop-win32-x64.exe'
Get-VerifiedDownload -Dependency $lock.petdex -Destination $native -CacheDirectory $DependencyCache
$petRoot = Join-Path $PetdexHome 'pets\jokebear-codexpet'
New-Item -ItemType Directory -Path $petRoot -Force | Out-Null
Get-VerifiedDownload -Dependency $lock.sprite -Destination (Join-Path $petRoot 'spritesheet.webp') -CacheDirectory $DependencyCache
$pet = [ordered]@{ id='jokebear-codexpet'; displayName='JokeBear-CodexPet'; description=$lock.artwork.description; spriteVersionNumber=2; spritesheetPath='spritesheet.webp' }
[IO.File]::WriteAllText((Join-Path $petRoot 'pet.json'),($pet | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path (Join-Path $destination 'data') -Force | Out-Null
$config = [ordered]@{ enabled=$true; vscodeOnly=$true; petdexHome=[IO.Path]::GetFullPath($PetdexHome); codexHome=[IO.Path]::GetFullPath($CodexDirectory); python=$python; codeExecutable=$editor.CodeExecutable; extensionDirectory=$editor.ExtensionDirectory; extensionVersion=$editor.ExtensionVersion; threadRouteSupported=$editor.ThreadRouteSupported }
[IO.File]::WriteAllText((Join-Path $destination 'petdex-integration.json'),($config | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
if (!$PrepareOnly) {
    # Update only pet selection/scale; preserve other native settings.
    $settingsPath = Join-Path $PetdexHome 'desktop-native-settings.json'
    $settings = if (Test-Path -LiteralPath $settingsPath) { Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
    if (Test-Path -LiteralPath $settingsPath) { Copy-Item -LiteralPath $settingsPath -Destination ($settingsPath + '.backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
    $settings | Add-Member -NotePropertyName active_pet -NotePropertyValue 'jokebear-codexpet' -Force
    $settings | Add-Member -NotePropertyName scale -NotePropertyValue 0.70 -Force
    $settings | Add-Member -NotePropertyName agents_prompted -NotePropertyValue $true -Force
    # Petdex's boolean settings parser expects compact JSON tokens.
    [IO.File]::WriteAllText($settingsPath,($settings | ConvertTo-Json -Depth 20 -Compress),[Text.UTF8Encoding]::new($false))
    & (Join-Path $destination 'install-hooks.ps1') -CodexDir $CodexDirectory
    if (!$NoStart) {
        # Apply the selected pet even if an original Petdex process was already
        # running. Restart only the exact checksum-verified native dependency.
        $nativeId = $null
        try { $nativeId = (Invoke-RestMethod -Uri 'http://127.0.0.1:7777/whoami' -TimeoutSec 2).pid } catch [System.Net.WebException] { }
        if ($nativeId) {
            $existingNative = Get-Process -Id $nativeId
            if (!$existingNative.Path -or (Get-FileHash -LiteralPath $existingNative.Path).Hash -ne $lock.petdex.sha256) { throw 'Quit the existing Petdex instance before starting this checksum-pinned version.' }
            Stop-Process -Id $nativeId
            if (!$existingNative.WaitForExit(5000)) { throw 'Existing native pet did not exit.' }
        }
        & (Join-Path $destination 'start.ps1')
    }
}
$report = [ordered]@{ Version='1.0.0'; InstalledAt=[DateTime]::UtcNow.ToString('o'); InstallDirectory=$destination; ExtensionVersion=$editor.ExtensionVersion; ConversationUnit='session_id'; DependenciesVerified=$true; HooksInstalled=(!$PrepareOnly); HookTrustRequiresUserReview=(!$PrepareOnly); Started=(!$PrepareOnly -and !$NoStart) }
[IO.File]::WriteAllText((Join-Path $destination 'data\installation-report.json'),($report | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Host ('Installed: ' + $destination)
if (!$PrepareOnly) { Write-Host 'First installation: run review-hooks.cmd, enter /hooks, review and trust PermissionRequest, then reload VS Code.' }
