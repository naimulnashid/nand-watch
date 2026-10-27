<#
.SYNOPSIS
  Registers (or removes) NAND Watch's collector task. NO ELEVATION NEEDED.
  Install.ps1 and Uninstall.ps1 run it for you.

.DESCRIPTION
  ONE TASK, UNELEVATED

    NAND Watch Collector    as you, not elevated, every hour (at :07) and at
                            sign-in. Runs the installed NandWatchCollector.exe,
                            which reads every NVMe drive once and exits:
                            78 ms of CPU and under 30 MB for one reading of
                            both drives, measured 2026-10-24.

  Reading a drive's health log through Windows' own storage query needs no
  Administrator, so nothing here does. (The Samsung's health log, which needs
  Intel's RST pass-through, will get its own elevated task later.)

  StartWhenAvailable: an hour missed while the PC slept runs at wake.
  ExecutionTimeLimit is five minutes: a run takes well under a second, so
  one still going after five has hung. The exe is a WinExe, so no window
  opens. The task lives in the Windows task store on C:, which a reset
  wipes: after a reset, re-run Install.ps1.

  Keep this file pure ASCII: Windows PowerShell 5.1 reads a BOM-less script
  as ANSI.

.PARAMETER Exe
  The installed NandWatchCollector.exe the task runs.
.PARAMETER Start
  Run the collector straight away rather than at the next hour.
#>
param(
  [string]$Exe,
  [switch]$Start,
  [switch]$Unregister
)
$ErrorActionPreference = 'Stop'

$taskName = 'NAND Watch Collector'

if ($Unregister) {
  if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed scheduled task '$taskName'."
  }
  exit 0
}

if (-not $Exe -or -not (Test-Path -LiteralPath $Exe)) { throw "The collector is not at '$Exe'." }

$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -StartWhenAvailable `
  -MultipleInstances IgnoreNew `
  -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

# Seven past the hour, not on it: most scheduled work starts at :00.
# No -RepetitionDuration: on Windows 10 and 11 that repeats indefinitely.
$hourly = New-ScheduledTaskTrigger -Once -At ((Get-Date).Date.AddMinutes(7)) -RepetitionInterval (New-TimeSpan -Hours 1)
$logon = New-ScheduledTaskTrigger -AtLogOn -User $user

Register-ScheduledTask -TaskName $taskName `
  -Action (New-ScheduledTaskAction -Execute $Exe -Argument '--trigger scheduled' -WorkingDirectory (Split-Path $Exe)) `
  -Trigger @($hourly, $logon) `
  -Principal (New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited) `
  -Settings $settings `
  -Description 'Reads the health counters of every NVMe drive into NAND Watch''s history. Runs unelevated and only reads.' `
  -Force | Out-Null
Write-Host "Registered '$taskName' - hourly at :07 and at sign-in, not elevated."

if ($Start) {
  Start-ScheduledTask -TaskName $taskName
  Write-Host 'Started a collection.'
}
exit 0
