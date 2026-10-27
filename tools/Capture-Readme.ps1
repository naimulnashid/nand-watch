<#
.SYNOPSIS
  Regenerates the README's screenshots, in both themes, on invented data.

.DESCRIPTION
  Publishes the app as Install.ps1 does (into artifacts\capture, gitignored),
  writes a fresh invented history into demo-data\ (gitignored) with
  `nandwatch demo-data`, then captures each page at 1440 DIPs wide, dark and
  light, and converts them to WebP in docs\screenshots\. The PNGs stay in
  screenshots\ (gitignored).

  Every figure is invented: two "Example" drives. The app runs against the
  demo's own folders (NANDWATCH_DATA_DIR and NANDWATCH_LOCAL_DIR) and its own
  single-instance key, so the real history is never touched, and
  NANDWATCH_DEBUG_VIEW keeps it from reading the real drives on open.

  Two things on screen are NOT from the demo, so a capture must leave them
  out, and every image must be looked at before it is committed: Sync
  Status reads the real scheduled tasks (its Intel RST panel) and names the
  folders in use at its foot. It is captured as one screen from the top.

  Run it with Windows PowerShell 5.1. Keep this file pure ASCII.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Capture-Readme.ps1
#>
param([int]$Width = 1440)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-Native([string]$exe, [string[]]$arguments) {
  # Native commands run outside 'Stop': their stderr would otherwise become a
  # terminating error even when they succeed. Judged by the exit code.
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  & $exe @arguments | Out-Null
  $code = $LASTEXITCODE
  $ErrorActionPreference = $saved
  if ($code -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $code" }
}

$app = Join-Path $root 'artifacts\capture'
Remove-Item -Recurse -Force $app -ErrorAction SilentlyContinue
Write-Host 'Publishing the app...'
Invoke-Native 'dotnet' @('publish', (Join-Path $root 'src\NandWatch.App\NandWatch.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', '-o', $app, '--nologo', '-v', 'q')
$exe = Join-Path $app 'NandWatch.exe'

# A fresh history each time, so its dates end today.
$demo = Join-Path $root 'demo-data'
Remove-Item -Recurse -Force $demo -ErrorAction SilentlyContinue
Write-Host 'Writing the invented history...'
Invoke-Native 'dotnet' @('run', '--project', (Join-Path $root 'src\NandWatch.Cli'), '--', 'demo-data', $demo)
$env:NANDWATCH_DATA_DIR = Join-Path $demo 'data'
$env:NANDWATCH_LOCAL_DIR = Join-Path $demo 'local'

# name = NANDWATCH_DEBUG_VIEW, and whether the whole page is captured.
# Drive 1 is the demo's first drive, the one nearer its rating.
$pages = @(
  @('overview', 'overview,0', $true),
  @('drive', 'drive,1,0', $true),
  @('events', 'events,0', $true),
  @('sync', 'sync,0', $false)
)

$pairs = @()
foreach ($theme in 'dark', 'light') {
  # The theme is an app setting, read at launch from the demo's local folder.
  [IO.File]::WriteAllText((Join-Path $env:NANDWATCH_LOCAL_DIR 'app-settings.json'), "{ `"Theme`": `"$theme`" }")
  foreach ($page in $pages) {
    $name = if ($theme -eq 'dark') { $page[0] } else { "$($page[0])-light" }
    $png = Join-Path $root "screenshots\readme-$name.png"
    $env:NANDWATCH_DEBUG_VIEW = $page[1]
    $sizing = @()
    if ($page[2]) { $env:NANDWATCH_DEBUG_FULLPAGE = "$Width"; $sizing = @('-AppSized') }
    else { $env:NANDWATCH_DEBUG_FULLPAGE = $null; $sizing = @('-Width', "$Width", '-Height', '900') }
    # A process each: Capture-Window's Add-Type cannot define its type twice in one session.
    Invoke-Native 'powershell' (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Capture-Window.ps1'), '-Exe', $exe, '-Out', $png) + $sizing + @('-WaitSeconds', '8', '-Close'))
    Start-Sleep -Milliseconds 800
    $pairs += @($png, (Join-Path $root "docs\screenshots\$name.webp"))
  }
}

Invoke-Native 'dotnet' (@('run', (Join-Path $PSScriptRoot 'to-webp.cs'), '--') + $pairs)
Write-Host "Done: docs\screenshots\. Look at every image before committing it." -ForegroundColor Green
