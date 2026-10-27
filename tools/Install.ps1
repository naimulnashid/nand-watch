<#
.SYNOPSIS
  Builds NAND Watch and installs it for the current user, with its hourly
  task. No administrator approval at any step.

.DESCRIPTION
  0. In a release zip the builds are already made: app\ (the app and the
     collector), cli\ and rst\ lie beside this script, and step 1 copies
     them instead of publishing. Anywhere else this is a clone, and it builds.
  1. Publishes self-contained Release builds of NandWatch.exe (the app) and
     NandWatchCollector.exe (what the task runs) into
     %LOCALAPPDATA%\Programs\NAND Watch, and nandwatch.exe (the command line)
     into its cli\ subfolder. The command line cannot share the app's folder:
     Windows file names ignore case, so its nandwatch.exe, .dll, .deps.json
     and .runtimeconfig.json would replace the app's NandWatch ones. A running
     app is closed and a collection in flight is waited for, both found by the
     path they run from, never by name.
  2. Adds "NAND Watch" to the Start menu and to Settings -> Apps -> Installed
     apps (per user), with Uninstall.ps1 copied into the program folder as its
     uninstall command, so it can be removed without this repo.
  3. Points the collector at its data folder (-DataDir), unless one is
     already set. Never on the system drive: a Windows reset would erase it.
     When -DataDir is on the system drive or a drive this PC lacks, nothing
     is set and the app asks for a folder when it first opens.
  4. Registers the hourly task (tools\Register-Tasks.ps1) and runs one
     collection now.
  5. Only with -RstReader: publishes the elevated RST reader and, behind ONE
     UAC prompt, installs it under %ProgramFiles%\NAND Watch with its daily
     task (tools\Register-RstReader.ps1). It reads the health log of a drive
     Intel RST will not pass on to a standard user. Check it first: run the
     published reader once from an elevated terminal and compare
     `nandwatch rst-show` with HWiNFO64 (see the README).

  Re-running updates the installed copy and re-registers the task. After a
  Windows reset, re-running it with -DataDir at the same folder is the whole
  recovery.

  Keep this file pure ASCII, in Windows PowerShell 5.1 syntax.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Install.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Install.ps1    # inside an extracted release zip
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Install.ps1 -RstReader
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Install.ps1 -Uninstall
#>
param(
  [string]$DataDir = 'D:\PersistentData\nand-watch',
  [switch]$Uninstall,
  [switch]$RemoveData,
  [switch]$SkipTasks,
  [switch]$RstReader
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $env:LOCALAPPDATA 'Programs\NAND Watch'
$collector = Join-Path $target 'NandWatchCollector.exe'
$app = Join-Path $target 'NandWatch.exe'
$cliDir = Join-Path $target 'cli'
$cli = Join-Path $cliDir 'nandwatch.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'NAND Watch.lnk'
$localDir = Join-Path $env:LOCALAPPDATA 'NAND Watch'

if ($Uninstall) {
  & (Join-Path $PSScriptRoot 'Uninstall.ps1') -RemoveData:$RemoveData -Quiet
  return
}

function Invoke-Native([string]$exe, [string[]]$arguments) {
  # Native commands run outside 'Stop': their stderr would otherwise become a
  # terminating error even when they succeed. Judged by the exit code.
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  & $exe @arguments
  $code = $LASTEXITCODE
  $ErrorActionPreference = $saved
  if ($code -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $code" }
}

# ---- 1. Publish and copy ------------------------------------------------------
# A release zip (tools\Package-Release.ps1) is this script and its helpers
# beside app\, cli\ and rst\, the published builds. Anywhere else this is a
# clone, and it builds.
$packaged = Test-Path (Join-Path $PSScriptRoot 'app\NandWatch.exe')
if ($packaged) {
  $staging = Join-Path $PSScriptRoot 'app'
  $cliStaging = Join-Path $PSScriptRoot 'cli'
  Write-Host "Installing the build in $staging"
} else {
  $staging = Join-Path $root 'publish\NandWatch'
  $cliStaging = Join-Path $root 'publish\NandWatchCli'
  foreach ($dir in $staging, $cliStaging) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
  Write-Host 'Publishing Release builds...'
  # The app and the collector share a folder; the command line gets its own
  # (see the description above).
  $publishes = @(
    @('src\NandWatch.App\NandWatch.App.csproj', $staging),
    @('src\NandWatch.Collector\NandWatch.Collector.csproj', $staging),
    @('src\NandWatch.Cli\NandWatch.Cli.csproj', $cliStaging)
  )
  foreach ($publish in $publishes) {
    Invoke-Native 'dotnet' @('publish', (Join-Path $root $publish[0]), '-c', 'Release', '-r', 'win-x64', '--self-contained', '-o', $publish[1], '--nologo', '-v', 'q')
  }
}
foreach ($file in (Join-Path $staging 'NandWatch.exe'), (Join-Path $staging 'NandWatchCollector.exe'), (Join-Path $cliStaging 'nandwatch.exe')) {
  if (-not (Test-Path $file)) { throw "The publish produced no $file." }
}
# NandWatch.dll must be the app's own assembly. Compared with case: the
# command line's is 'nandwatch', and once it overwrote the app's files here,
# so the Start menu entry ran the command line.
$appAssembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $staging 'NandWatch.dll')).Name
if ($appAssembly -cne 'NandWatch' -or -not (Test-Path (Join-Path $staging 'Microsoft.WinUI.dll'))) {
  throw "The published NandWatch.exe is not the app (its assembly is '$appAssembly'). Something else was published over it."
}
# Without its own resources.pri the app dies at startup (0xC000027B): what a
# publish without EnableMsixTooling produced in the siblings. Never install that.
if (-not (Test-Path (Join-Path $staging 'NandWatch.pri'))) {
  throw 'The published build has no NandWatch.pri, so it would crash at startup. Check EnableMsixTooling in NandWatch.App.csproj.'
}

# The app is closed and a collection in flight (under a second) is waited
# for, each found by the path it runs from.
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

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $staging '*') $target -Recurse -Force
New-Item -ItemType Directory -Force $cliDir | Out-Null
Copy-Item (Join-Path $cliStaging '*') $cliDir -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') (Join-Path $target 'Uninstall.ps1') -Force
Copy-Item (Join-Path $PSScriptRoot 'Register-Tasks.ps1') (Join-Path $target 'Register-Tasks.ps1') -Force
# The uninstall needs it to remove the RST reader, from the program folder.
Copy-Item (Join-Path $PSScriptRoot 'Register-RstReader.ps1') (Join-Path $target 'Register-RstReader.ps1') -Force
# Explorer marks every file it extracts from a downloaded zip as downloaded,
# and SmartScreen then stops the app at each launch. Running this script is
# the decision to install it, as it is for any installer: clear the mark on
# the installed copy only.
if ($packaged) { Get-ChildItem $target -Recurse -File | Unblock-File }

# ---- 2. Start menu and Installed apps -------------------------------------------
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $app
$link.WorkingDirectory = $target
$link.IconLocation = "$app,0"
$link.Description = 'A permanent history of your NVMe drives'' health, and how long they will last'
$link.Save()

$bytes = (Get-ChildItem $target -Recurse | Measure-Object Length -Sum).Sum
$version = ([version][Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion).ToString(3)
$uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $target 'Uninstall.ps1')`""
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NandWatch'
New-Item -Path $key -Force | Out-Null
$entry = @{
  DisplayName = 'NAND Watch'
  DisplayVersion = [string]$version
  Publisher = 'Naimul Nashid'
  DisplayIcon = "$app,0"
  InstallLocation = $target
  UninstallString = $uninstallCommand
  QuietUninstallString = "$uninstallCommand -Quiet"
  URLInfoAbout = 'https://github.com/naimulnashid/nand-watch'
  InstallDate = (Get-Date -Format 'yyyyMMdd')
}
foreach ($name in $entry.Keys) { New-ItemProperty -Path $key -Name $name -Value $entry[$name] -PropertyType String -Force | Out-Null }
# No Modify or Repair buttons: re-running this script is the repair.
foreach ($name in 'NoModify', 'NoRepair') { New-ItemProperty -Path $key -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }
New-ItemProperty -Path $key -Name 'EstimatedSize' -Value ([int]($bytes / 1KB)) -PropertyType DWord -Force | Out-Null

# ---- 3. The data folder -------------------------------------------------------
$location = Join-Path $localDir 'location.json'
if (Test-Path $location) {
  Write-Host "Data folder already set: $((Get-Content $location -Raw | ConvertFrom-Json).dataDir)"
} elseif ($DataDir) {
  # Not set here is not a failure: until a folder is set the collector logs
  # "no data folder" and stops, and the app asks for one when it opens, where
  # the system drive can be chosen deliberately on a one-drive PC.
  $systemRoot = [IO.Path]::GetPathRoot($env:SystemRoot)
  $dataRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($DataDir))
  if ($dataRoot -ieq $systemRoot) {
    Write-Host "Not setting ${DataDir}: it is on the system drive, which a reset erases. The app will ask on first run, or re-run with -DataDir on another drive." -ForegroundColor Yellow
  } elseif (-not (Test-Path -LiteralPath $dataRoot)) {
    Write-Host "Not setting ${DataDir}: there is no $dataRoot drive. The app will ask on first run, or re-run with -DataDir." -ForegroundColor Yellow
  } else {
    New-Item -ItemType Directory -Force $DataDir | Out-Null
    New-Item -ItemType Directory -Force $localDir | Out-Null
    $json = @{ dataDir = [IO.Path]::GetFullPath($DataDir); allowSystemDrive = $false } | ConvertTo-Json
    # BOM-less: Windows PowerShell's utf8 writes a BOM.
    [IO.File]::WriteAllText($location, $json, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Data folder: $DataDir"
  }
}

# ---- 4. The task, and a first collection ----------------------------------------
if (-not $SkipTasks) {
  & (Join-Path $target 'Register-Tasks.ps1') -Exe $collector -Start
  if ($LASTEXITCODE -ne 0) { throw "Registering the task failed (exit $LASTEXITCODE). Nothing will be collected until it succeeds: re-run this script." }
  # The first run creates the database; show its line from the log.
  $log = Join-Path $localDir 'logs\collector.log'
  for ($i = 0; $i -lt 20 -and -not (Test-Path $log); $i++) { Start-Sleep -Milliseconds 500 }
  Start-Sleep -Seconds 2
  if (Test-Path $log) { Write-Host ('First collection: ' + (Get-Content $log -Tail 1)) }
  else { Write-Host "No collector log yet. Check with: & '$cli' stats" -ForegroundColor Yellow }
}

# ---- 5. The RST reader, only when asked: the one elevated piece -----------------
if ($RstReader) {
  if ($packaged) {
    $rstStaging = Join-Path $PSScriptRoot 'rst'
    Get-ChildItem $rstStaging -Recurse -File | Unblock-File
  } else {
    $rstStaging = Join-Path $root 'publish\RstReader'
    if (Test-Path $rstStaging) { Remove-Item $rstStaging -Recurse -Force }
    Write-Host 'Publishing the RST reader...'
    Invoke-Native 'dotnet' @('publish', (Join-Path $root 'src\NandWatch.RstReader\NandWatch.RstReader.csproj'), '-c', 'Release', '-o', $rstStaging, '--nologo', '-v', 'q')
  }
  $rstExe = Join-Path $rstStaging 'NandWatchRstReader.exe'
  if (-not (Test-Path $rstExe)) { throw "There is no NandWatchRstReader.exe in $rstStaging." }
  $hash = (Get-FileHash -LiteralPath $rstExe -Algorithm SHA256).Hash
  $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  $elevated = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $PSScriptRoot 'Register-RstReader.ps1')`"",
    '-Source', "`"$rstStaging`"", '-ExpectedHash', $hash, '-UserSid', $sid)
  Write-Host 'Installing the RST reader under Program Files: Windows will ask for administrator approval.'
  $p = Start-Process powershell.exe -Verb RunAs -ArgumentList $elevated -Wait -PassThru
  if ($p.ExitCode -ne 0) { throw "Installing the RST reader failed (exit $($p.ExitCode)). The rest of NAND Watch is installed and works without it." }
  Write-Host 'The RST reader is installed. Its first read lands at the next collection, or press Read now in the app.' -ForegroundColor Green
}

Write-Host ("Installed to {0} ({1:N0} MB), with a Start menu shortcut and an Installed apps entry. The command line is {2}." -f $target, ($bytes / 1MB), $cli) -ForegroundColor Green
