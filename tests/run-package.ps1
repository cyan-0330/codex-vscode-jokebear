param([Parameter(Mandatory=$true)][string]$DependencyCache,[string]$TestDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$TestDirectory) { $TestDirectory = Join-Path $PSScriptRoot ('tmp\package-' + [guid]::NewGuid().ToString('N')) }
$fixture = [IO.Path]::GetFullPath($TestDirectory)
if (Test-Path -LiteralPath $fixture) { throw 'Package fixture already exists.' }
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
function Check($ok,$message) { if (!$ok) { throw $message }; Write-Host ('PASS: ' + $message) }
$unicodeFolder = [string][char]0x6d4b + [char]0x8bd5 + ' folder'
$install = Join-Path $fixture $unicodeFolder
$editor = Join-Path $fixture 'VS Code\Code.exe'
New-Item -ItemType Directory -Path (Split-Path $editor) -Force | Out-Null
[IO.File]::WriteAllText($editor,'fixture - not executed')
$extension = Join-Path $fixture 'extensions\openai.chatgpt-fixture'
New-Item -ItemType Directory -Path (Join-Path $extension 'webview\assets') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $extension 'bin\windows-x86_64') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $extension 'package.json'),' {"publisher":"openai","name":"chatgpt","version":"26.1007.21434","main":"extension.js"}')
[IO.File]::WriteAllText((Join-Path $extension 'extension.js'),'registerUriHandler')
[IO.File]::WriteAllText((Join-Path $extension 'webview\assets\app-initial-fixture.js'),'/local/:conversationId')
[IO.File]::WriteAllText((Join-Path $extension 'bin\windows-x86_64\codex.exe'),'fixture - not executed')
$codex = Join-Path $fixture 'codex-data'
$pets = Join-Path $fixture 'pet-data'
New-Item -ItemType Directory -Path $codex,$pets -Force | Out-Null
$hooks = Join-Path $codex 'hooks.json'
[IO.File]::WriteAllText($hooks,'{"description":"preserve me","hooks":{"PermissionRequest":[{"matcher":"Bash","hooks":[{"type":"command","command":"existing-handler"}]}]}}')
$settings = Join-Path $pets 'desktop-native-settings.json'
[IO.File]::WriteAllText($settings,'{"active_pet":"another-pet","waiting_sound":false,"custom_setting":"preserve me"}')
$options = @{ InstallDirectory=$install; CodexDirectory=$codex; PetdexHome=$pets; CodeExecutable=$editor; ExtensionDirectory=$extension; DependencyCache=$DependencyCache; NoStart=$true }
& (Join-Path $root 'install.ps1') @options
$config = Get-Content -LiteralPath (Join-Path $install 'petdex-integration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($config.vscodeOnly -and $config.codeExecutable -eq $editor -and $config.codexHome -eq $codex -and $config.extensionDirectory -eq $extension) 'fresh install generates this users VS Code configuration'
Check (Test-Path -LiteralPath (Join-Path $install 'bin\CodexPet.Hook.exe')) 'compiled companion is installed at a Unicode path'
$prepared = Get-Content -LiteralPath $hooks -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($prepared.description -eq 'preserve me' -and $prepared.hooks.PermissionRequest[0].matcher -eq 'Bash' -and $prepared.hooks.PermissionRequest[0].hooks[0].command -eq 'existing-handler') 'install preserves existing hooks and matchers'
Check ($prepared.hooks.PermissionRequest.Count -eq 2 -and $prepared.hooks.PermissionRequest[1].hooks[0].command.Contains($install)) 'approval handler points to the actual new install'
$nativeSettings = Get-Content -LiteralPath $settings -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($nativeSettings.active_pet -eq 'jokebear-codexpet' -and $nativeSettings.scale -eq 0.70 -and $nativeSettings.custom_setting -eq 'preserve me') 'JokeBear appearance is selected without losing native settings'
Check ($nativeSettings.agents_prompted -and [IO.File]::ReadAllText($settings).Contains('"agents_prompted":true')) 'native agent setup is suppressed using its supported compact JSON format'
$before = [IO.File]::ReadAllText($hooks)
& (Join-Path $root 'install.ps1') @options
Check ([IO.File]::ReadAllText($hooks) -eq $before) 'repeated install does not duplicate or rewrite unchanged hooks'
[IO.File]::WriteAllText((Join-Path $install 'data\progress.json'),'{"Unit":"conversation","Hidden":["fixture-hidden"],"Projects":[]}')
& (Join-Path $root 'install.ps1') @options
$preserved = Get-Content -LiteralPath (Join-Path $install 'data\progress.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($preserved.Hidden -contains 'fixture-hidden') 'reinstall preserves hidden conversation settings'
& (Join-Path $install 'uninstall-hooks.ps1') -CodexDir $codex
$removed = Get-Content -LiteralPath $hooks -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($removed.hooks.PermissionRequest.Count -eq 1 -and $removed.hooks.PermissionRequest[0].hooks[0].command -eq 'existing-handler') 'uninstall removes only companion handlers'
Check (!(Test-Path -LiteralPath (Join-Path $codex 'config.toml'))) 'installer never writes hook trust or Codex approval policy'
$badCache = Join-Path $fixture 'bad-cache'
New-Item -ItemType Directory -Path $badCache | Out-Null
$lock = Get-Content -LiteralPath (Join-Path $root 'dependencies.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
[IO.File]::WriteAllText((Join-Path $badCache $lock.python.fileName),'corrupted dependency')
$rejected = $false
try { & (Join-Path $root 'install.ps1') -InstallDirectory (Join-Path $fixture 'rejected') -CodexDirectory $codex -PetdexHome $pets -CodeExecutable $editor -ExtensionDirectory $extension -DependencyCache $badCache -PrepareOnly } catch { $rejected = $_.Exception.Message -like '*checksum mismatch*' }
Check $rejected 'corrupted cached dependencies are rejected before hooks are installed'
Write-Host ('Package fixture: ' + $fixture)
