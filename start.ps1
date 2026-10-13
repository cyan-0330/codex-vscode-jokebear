$ErrorActionPreference = 'Stop'
$configPath = Join-Path $PSScriptRoot 'petdex-integration.json'
if (!(Test-Path -LiteralPath $configPath)) { throw 'Run install.cmd first.' }
& (Join-Path $PSScriptRoot 'scripts\detect-vscode.ps1')
$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$petdex = Join-Path $PSScriptRoot 'petdex-runtime\petdex-desktop-win32-x64.exe'
$companion = Join-Path $PSScriptRoot 'bin\CodexPet.exe'
if (!(Test-Path -LiteralPath $companion)) { & (Join-Path $PSScriptRoot 'build.ps1') }
if (!(Test-Path -LiteralPath $petdex)) { throw 'Run install.cmd to prepare Petdex Desktop.' }
# Close Pet leaves the native server alive. Detect its actual visible window
# so start.cmd can reopen it without requiring a separate Quit action.
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PetStartupWindows {
 delegate bool Callback(IntPtr w,IntPtr d);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback cb,IntPtr d);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 public static bool Visible(int pid) { bool found=false;EnumWindows(delegate(IntPtr w,IntPtr d){uint p;GetWindowThreadProcessId(w,out p);var t=new StringBuilder(256);GetWindowText(w,t,256);if(p==pid&&IsWindowVisible(w)&&t.ToString()=="Petdex")found=true;return true;},IntPtr.Zero);return found; }
}
'@
$running = $false
try {
    $nativeId = (Invoke-RestMethod -Uri 'http://127.0.0.1:7777/whoami' -TimeoutSec 2).pid
    $running = [PetStartupWindows]::Visible($nativeId)
    if (!$running) {
        $nativeProcess = Get-Process -Id $nativeId
        if ($nativeProcess.Path -eq $petdex) { Stop-Process -Id $nativeId; [void]$nativeProcess.WaitForExit(5000) }
        else { throw 'An existing Petdex instance has no visible pet. Quit it from its tray first.' }
    }
} catch [System.Net.WebException] { }
if (!$running) {
    $launch = New-Object Diagnostics.ProcessStartInfo
    $launch.FileName = $petdex; $launch.WorkingDirectory = Split-Path $petdex; $launch.UseShellExecute = $false; $launch.CreateNoWindow = $true; $launch.WindowStyle = 'Hidden'
    $launch.EnvironmentVariables.Remove('ELECTRON_RUN_AS_NODE')
    $launch.EnvironmentVariables.Remove('PETDEX_PET')
    [void][Diagnostics.Process]::Start($launch)
}
Start-Process -FilePath $companion -WorkingDirectory $PSScriptRoot -WindowStyle Hidden | Out-Null
