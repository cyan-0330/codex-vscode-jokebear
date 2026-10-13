function Get-VscodeEnvironment {
    param([string]$CodeExecutable,[string]$ExtensionDirectory)
    if ($CodeExecutable) {
        $editor = [IO.Path]::GetFullPath($CodeExecutable)
        if (!(Test-Path -LiteralPath $editor -PathType Leaf)) { throw 'The specified Code.exe does not exist.' }
    } else {
        $candidates = @()
        $command = Get-Command code -ErrorAction SilentlyContinue
        if ($command -and $command.Source) { $candidates += Join-Path (Split-Path (Split-Path $command.Source)) 'Code.exe' }
        foreach ($process in @(Get-Process -Name Code -ErrorAction SilentlyContinue)) { if ($process.Path -and [IO.Path]::GetFileName($process.Path) -eq 'Code.exe') { $candidates += $process.Path } }
        foreach ($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\Code.exe','HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\Code.exe')) {
            if (Test-Path -LiteralPath $key) { $registered = (Get-Item -LiteralPath $key).GetValue(''); if ($registered) { $candidates += $registered } }
        }
        $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\Code.exe'
        $candidates += Join-Path $env:ProgramFiles 'Microsoft VS Code\Code.exe'
        if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Microsoft VS Code\Code.exe' }
        $editor = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (!$editor) { throw 'Install Windows VS Code first, or pass -CodeExecutable with the full Code.exe path.' }
    }
    if (!$ExtensionDirectory) {
        $extensionRoots = @(Join-Path $env:USERPROFILE '.vscode\extensions')
        if ($env:VSCODE_EXTENSIONS) { $extensionRoots = @($env:VSCODE_EXTENSIONS) + $extensionRoots }
        $portable = Join-Path (Split-Path $editor) 'data\extensions'
        if (Test-Path -LiteralPath $portable) { $extensionRoots = @($portable) + $extensionRoots }
        $extensions = @()
        foreach ($extensionRoot in $extensionRoots) {
            if (Test-Path -LiteralPath $extensionRoot) { $extensions += @(Get-ChildItem -LiteralPath $extensionRoot -Directory -Filter 'openai.chatgpt-*') }
        }
        $extension = $extensions | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (!$extension) { throw 'Install the official OpenAI Codex extension in VS Code first.' }
        $ExtensionDirectory = $extension.FullName
    }
    $ExtensionDirectory = [IO.Path]::GetFullPath($ExtensionDirectory)
    $package = Get-Content -LiteralPath (Join-Path $ExtensionDirectory 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($package.publisher -ne 'openai' -or $package.name -ne 'chatgpt') { throw 'The selected directory is not the official OpenAI VS Code extension.' }
    $entry = [IO.File]::ReadAllText((Join-Path $ExtensionDirectory $package.main))
    $route = $false
    $assets = Join-Path $ExtensionDirectory 'webview\assets'
    if (Test-Path -LiteralPath $assets) {
        foreach ($file in @(Get-ChildItem -LiteralPath $assets -Filter '*.js')) {
            if ([IO.File]::ReadAllText($file.FullName).Contains('/local/:conversationId')) { $route = $true; break }
        }
    }
    $codexRuntime = Join-Path $ExtensionDirectory 'bin\windows-x86_64\codex.exe'
    if (!(Test-Path -LiteralPath $codexRuntime)) { throw 'The extension does not include the Windows x64 Codex runtime. Update the extension.' }
    [pscustomobject]@{ CodeExecutable=$editor; ExtensionDirectory=$ExtensionDirectory; ExtensionVersion=$package.version; ThreadRouteSupported=($route -and $entry.Contains('registerUriHandler')) }
}

function Get-VerifiedDownload {
    param([Parameter(Mandatory=$true)]$Dependency,[Parameter(Mandatory=$true)][string]$Destination,[string]$CacheDirectory)
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileHash -LiteralPath $Destination).Hash -eq $Dependency.sha256) { return }
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    $cached = if ($CacheDirectory) { Join-Path $CacheDirectory $Dependency.fileName } else { $null }
    if ($cached -and (Test-Path -LiteralPath $cached)) {
        if ((Get-FileHash -LiteralPath $cached).Hash -ne $Dependency.sha256) { throw ('Cached dependency checksum mismatch: ' + $Dependency.fileName) }
        Copy-Item -LiteralPath $cached -Destination $Destination -Force
        return
    }
    $uri = [Uri]$Dependency.url
    if ($uri.Scheme -ne 'https') { throw 'Dependencies must use HTTPS.' }
    $temporary = $Destination + '.download'
    try {
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            & $curl.Source --location --fail --silent --show-error --retry 2 --connect-timeout 20 --max-time 180 --output $temporary $Dependency.url
            if ($LASTEXITCODE -ne 0) { throw ('Download failed: ' + $Dependency.fileName) }
        } else {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -UseBasicParsing -Uri $Dependency.url -OutFile $temporary
        }
        if ((Get-FileHash -LiteralPath $temporary).Hash -ne $Dependency.sha256) { throw ('Downloaded dependency checksum mismatch: ' + $Dependency.fileName) }
        Move-Item -LiteralPath $temporary -Destination $Destination -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
