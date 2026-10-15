param([Parameter(Mandatory=$true)][int]$GamePid, [Parameter(Mandatory=$true)][string]$ExpectedExecutable)
$ErrorActionPreference='Stop'
$process=Get-Process -Id $GamePid
if ($process.Path -ne [IO.Path]::GetFullPath($ExpectedExecutable)) { throw 'Refusing to close another executable.' }
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class FixtureWindow {
 public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
 public static int Close(uint pid) {
  int count=0;
  EnumWindows((hwnd,data)=> { uint owner; GetWindowThreadProcessId(hwnd,out owner); if(owner==pid && PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero)) count++; return true; },IntPtr.Zero);
  return count;
 }
}
"@
$count=[FixtureWindow]::Close([uint32]$GamePid)
if (!$count) { throw 'No game window accepted a close request.' }
if (!$process.WaitForExit(15000)) { throw 'Game did not exit; no forced termination performed.' }
Write-Output 'Isolated game closed normally.'
