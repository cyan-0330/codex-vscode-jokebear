$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$expected = Join-Path $root 'bin\CodexPet.exe'
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class CompanionWindows {
 public delegate bool Callback(IntPtr window, IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr data);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 public static Dictionary<IntPtr,string> List(int pid) {
  var windows=new Dictionary<IntPtr,string>();
  EnumWindows(delegate(IntPtr window,IntPtr data) { uint id;GetWindowThreadProcessId(window,out id); if(id==pid) { var text=new StringBuilder(256);GetWindowText(window,text,256);windows[window]=text.ToString(); } return true; },IntPtr.Zero);
  return windows;
 }
}
'@
foreach ($process in @(Get-Process CodexPet -ErrorAction SilentlyContinue)) {
    if ($process.Path -ne $expected) { continue }
    $windows = [CompanionWindows]::List($process.Id)
    $approvalTitle = 'Codex ' + [char]0x684c + [char]0x5ba0 + ' ' + [char]0x00b7 + ' ' + [char]0x9700 + [char]0x8981 + [char]0x4f60 + [char]0x5ba1 + [char]0x6279
    if (@($windows.Values | Where-Object { $_ -eq $approvalTitle }).Count) { throw 'An approval is pending. Finish it before updating the companion.' }
    foreach ($item in $windows.GetEnumerator()) {
        if ($item.Value -eq ('Codex ' + [char]0x5c0f + [char]0x732b)) { [void][CompanionWindows]::PostMessage($item.Key,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) }
    }
    if (!$process.WaitForExit(5000)) { throw 'Companion did not exit gracefully.' }
}
# Clean only an orphaned test process created by this project's test harness.
foreach ($process in @(Get-Process ProjectTests -ErrorAction SilentlyContinue)) {
    if ($process.Path -eq (Join-Path $root 'tests\tmp\ProjectTests.exe')) { $process | Stop-Process -Force }
}
$backup = Join-Path $root ('bin\backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($name in 'CodexPet.exe','CodexPet.Hook.exe') {
    if (!(Test-Path -LiteralPath "$root\bin-staging\$name")) { throw "Missing staged binary: $name" }
    Copy-Item -LiteralPath "$root\bin\$name" -Destination "$backup\$name"
    Copy-Item -LiteralPath "$root\bin-staging\$name" -Destination "$root\bin\$name" -Force
}
try {
    $nativePid = (Invoke-RestMethod 'http://127.0.0.1:7777/whoami' -TimeoutSec 2).pid
    $native = Get-Process -Id $nativePid
    if ($native.Path -eq (Join-Path $root 'petdex-runtime\petdex-desktop-win32-x64.exe')) {
        foreach ($item in ([CompanionWindows]::List($nativePid)).GetEnumerator()) {
            if ($item.Value -eq 'Petdex Settings') { [void][CompanionWindows]::PostMessage($item.Key,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) }
        }
    }
} catch { }
& "$root\start.ps1"
Write-Host 'Updated and started JokeBear approval/project companion.'
