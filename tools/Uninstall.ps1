<#
.SYNOPSIS
  Removes NAND Watch installed by Install.ps1.

.DESCRIPTION
  Removes the scheduled task first (so nothing starts a collection meanwhile),
  closes the app and waits for a collection in flight, both found by the path
  they run from, never by name, then removes the program folder, the Start
  menu shortcut and the Installed apps entry, and the per-user keys the app
  wrote: its notification identity, the nandwatch: link a notification opens,
  and start at sign-in (only when it points at this install). Nothing needs
  administrator approval.

  THE HISTORY IS NEVER TOUCHED. The data folder (the database and its
  backup) is the thing this app exists to keep; delete it by hand if you mean
  to. -RemoveData removes only %LOCALAPPDATA%\NAND Watch: the pointer to the
  data folder and the logs.

  Install.ps1 copies this script into the program folder. Run from there, it
  first copies itself to %TEMP% and hands over, because it is about to delete
  the folder it lives in.

  Keep this file pure ASCII.
#>
param(
  [switch]$RemoveData,
  # No final pause - for Install.ps1 -Uninstall and scripted use.
  [switch]$Quiet
)
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\NAND Watch'
$collector = Join-Path $target 'NandWatchCollector.exe'
$app = Join-Path $target 'NandWatch.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'NAND Watch.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NandWatch'
$localDir = Join-Path $env:LOCALAPPDATA 'NAND Watch'
$taskName = 'NAND Watch Collector'

if ($PSCommandPath -and $PSCommandPath.StartsWith($target, [StringComparison]::OrdinalIgnoreCase)) {
  $copy = Join-Path $env:TEMP "NandWatch-Uninstall-$PID.ps1"
  Copy-Item $PSCommandPath $copy -Force
  $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$copy`"")
  if ($RemoveData) { $forward += '-RemoveData' }
  if ($Quiet) { $forward += '-Quiet' }
  Set-Location $env:TEMP
  $p = Start-Process powershell.exe -ArgumentList $forward -Wait -PassThru
  Remove-Item $copy -Force -ErrorAction SilentlyContinue
  exit $p.ExitCode
}

try {
  # The RST reader is under Program Files and runs as SYSTEM: removing it needs
  # one UAC prompt, and only when it was installed (Install.ps1 -RstReader).
  $rstTask = 'NAND Watch RST Reader'
  if (Get-ScheduledTask -TaskName $rstTask -ErrorAction SilentlyContinue) {
    $register = Join-Path $target 'Register-RstReader.ps1'
    if (-not (Test-Path $register)) { $register = Join-Path $PSScriptRoot 'Register-RstReader.ps1' }
    if (Test-Path $register) {
      Write-Host 'Removing the RST reader from Program Files: Windows will ask for administrator approval.'
      $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$register`"", '-Unregister')
      if ($p.ExitCode -ne 0) { Write-Host "The RST reader was not removed (exit $($p.ExitCode)); remove the '$rstTask' task and $env:ProgramFiles\NAND Watch by hand." -ForegroundColor Yellow }
    }
  }
  if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed scheduled task '$taskName'."
  }
  Get-Process NandWatch -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $app) } |
    ForEach-Object {
      Write-Host "Closing the running app (PID $($_.Id))"
      Stop-Process -Id $_.Id -Force
      $_.WaitForExit(5000) | Out-Null
    }
  Get-Process NandWatchCollector -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $collector) } |
    ForEach-Object { $_.WaitForExit(30000) | Out-Null }

  if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
  if (Test-Path $uninstallKey) { Remove-Item $uninstallKey -Recurse -Force }
  # Written by the app and the collector (NandWatch.Windows Notify\Toasts.cs, State\StartupRegistration.cs).
  foreach ($key in 'HKCU:\Software\Classes\AppUserModelId\NaimulNashid.NandWatch', 'HKCU:\Software\Classes\nandwatch') {
    if (Test-Path $key) { Remove-Item $key -Recurse -Force }
  }
  $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  $run = (Get-ItemProperty -Path $runKey -Name 'NAND Watch' -ErrorAction SilentlyContinue).'NAND Watch'
  if ($run -and $run.IndexOf($app, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Remove-ItemProperty -Path $runKey -Name 'NAND Watch' }
  if (Test-Path $target) { Remove-Item $target -Recurse -Force }

  $dataDir = $null
  $location = Join-Path $localDir 'location.json'
  if (Test-Path $location) { $dataDir = (Get-Content $location -Raw | ConvertFrom-Json).dataDir }
  if ($RemoveData -and (Test-Path $localDir)) {
    Remove-Item $localDir -Recurse -Force
    Write-Host "Removed $localDir"
  }
  Write-Host 'NAND Watch is uninstalled.' -ForegroundColor Green
  if ($dataDir) { Write-Host "Your history in $dataDir was kept. Reinstalling with -DataDir at it continues it." }
  $code = 0
}
catch {
  Write-Host "Uninstall failed: $($_.Exception.Message)" -ForegroundColor Red
  $code = 1
}

if (-not $Quiet) { Start-Sleep -Seconds 5 }
exit $code
