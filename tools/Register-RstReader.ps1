<#
.SYNOPSIS
  Installs (or removes) the elevated RST reader and its task. MUST RUN
  ELEVATED: Install.ps1 -RstReader and Uninstall.ps1 start it for you,
  behind one UAC prompt.

.DESCRIPTION
  THE ONE ELEVATED PIECE

    NAND Watch RST Reader   as SYSTEM, once a day at 12:37 (or at the next
                            start when the PC was off), and whenever the app's
                            "Read now" starts it. Runs
                            %ProgramFiles%\NAND Watch\NandWatchRstReader.exe,
                            which sends each NVMe drive behind Intel RST one
                            command, Get Log Page 02h (the health log), and
                            writes what came back to
                            %ProgramData%\NAND Watch\rst\rst-health.json.

  Why each choice:
  - The exe lives under %ProgramFiles%, which only administrators can write,
    so nothing running as the user can replace what runs elevated. Never the
    per-user install in %LOCALAPPDATA%.
  - The copy is checked against the hash Install.ps1 computed of the build,
    so a file swapped between the build and this copy is refused.
  - The task runs as SYSTEM, not as the user elevated: a task run as the user
    gets the user's environment variables, and a few of them
    (DOTNET_STARTUP_HOOKS, CORECLR_PROFILER...) make a .NET process load code
    of the user's choosing. SYSTEM's environment only administrators can set.
  - Its arguments are fixed here. It reads nothing a user can write.
  - The output folder is created with its ACL in ONE call (SYSTEM and
    Administrators write, the user reads), inheritance off: a folder created
    first and locked second leaves a gap for planted links. The ACL is read
    back afterwards, and registration stops if it is not the one set here.
  - The task's DACL also lets the installing user READ and RUN it, so the app
    can start it with no prompt. Running it is all that permission allows.

  Keep this file pure ASCII, in Windows PowerShell 5.1 syntax.

.PARAMETER Source
  The published reader (Install.ps1's publish\RstReader).
.PARAMETER ExpectedHash
  The SHA-256 of NandWatchRstReader.exe as Install.ps1 built it.
.PARAMETER UserSid
  The account allowed to start the task (the installing user, not the admin
  whose credentials approved the prompt).
#>
param(
  [string]$Source,
  [string]$ExpectedHash,
  [string]$UserSid,
  [switch]$Unregister
)
$ErrorActionPreference = 'Stop'

$taskName = 'NAND Watch RST Reader'
$programDir = Join-Path $env:ProgramFiles 'NAND Watch'
$dataRoot = Join-Path $env:ProgramData 'NAND Watch'
$rstDir = Join-Path $dataRoot 'rst'
$exeName = 'NandWatchRstReader.exe'
$admins = [Security.Principal.SecurityIdentifier]'S-1-5-32-544'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Register-RstReader.ps1 must run elevated. Run Install.ps1 -RstReader, which asks for elevation for this step only.' }

function Remove-Dir([string]$dir) {
  # rd, not Remove-Item -Recurse: rd removes a junction instead of following it.
  if (Test-Path -LiteralPath $dir) {
    & cmd.exe /d /c rd /s /q "`"$dir`"" | Out-Null
    if (Test-Path -LiteralPath $dir) { throw "Could not remove $dir." }
  }
}

# Only a folder Administrators own (or SYSTEM, or TrustedInstaller) is removed.
function Test-AdminOwned([string]$dir) {
  if (-not (Test-Path -LiteralPath $dir)) { return $false }
  $owner = (Get-Acl -LiteralPath $dir).GetOwner([Security.Principal.SecurityIdentifier]).Value
  return $owner -in @('S-1-5-32-544', 'S-1-5-18', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
}

if ($Unregister) {
  if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed scheduled task '$taskName'."
  }
  foreach ($dir in @($programDir, $dataRoot)) {
    if (Test-AdminOwned $dir) { Remove-Dir $dir; Write-Host "Removed $dir." }
  }
  exit 0
}

$built = Join-Path $Source $exeName
if (-not (Test-Path -LiteralPath $built)) { throw "No $exeName in $Source." }
if (-not $ExpectedHash) { throw '-ExpectedHash is required.' }
if (-not $UserSid) { throw '-UserSid is required.' }

# ---- 1. The program, under Program Files --------------------------------------
# A fresh folder inherits Program Files' ACL: Administrators write, Users read.
if (Test-Path -LiteralPath $programDir) {
  if (-not (Test-AdminOwned $programDir)) { throw "$programDir exists and is not owned by Administrators. Remove it and re-run." }
  Remove-Dir $programDir
}
New-Item -ItemType Directory -Path $programDir | Out-Null
if ((Get-Item -LiteralPath $programDir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$programDir is a link." }
Copy-Item (Join-Path $Source '*') $programDir -Recurse -Force
$exe = Join-Path $programDir $exeName
$copied = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
if ($copied -ne $ExpectedHash) {
  Remove-Dir $programDir
  throw "The copied $exeName is not the one Install.ps1 built (hash $copied, expected $ExpectedHash). Nothing was installed."
}
Write-Host "Installed $exe"

# ---- 2. The output folder, created with its ACL in one call -------------------
if (Test-Path -LiteralPath $dataRoot) {
  if (-not (Test-AdminOwned $dataRoot)) { throw "$dataRoot exists and is not owned by Administrators. Remove it and re-run." }
  Remove-Dir $dataRoot
}
$security = New-Object System.Security.AccessControl.DirectorySecurity
$security.SetAccessRuleProtection($true, $false)
$security.SetOwner($admins)
$inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
$none = [System.Security.AccessControl.PropagationFlags]::None
foreach ($grant in @(
    @('S-1-5-18', 'FullControl'),       # SYSTEM: the reader writes here
    @('S-1-5-32-544', 'FullControl'),   # Administrators
    @($UserSid, 'ReadAndExecute')       # the collector reads the report
  )) {
  $security.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        [Security.Principal.SecurityIdentifier]$grant[0],
        [System.Security.AccessControl.FileSystemRights]$grant[1],
        $inherit, $none, [System.Security.AccessControl.AccessControlType]::Allow)))
}
[System.IO.Directory]::CreateDirectory($dataRoot, $security) | Out-Null
$made = Get-Acl -LiteralPath $dataRoot
if (-not $made.AreAccessRulesProtected -or $made.GetOwner([Security.Principal.SecurityIdentifier]) -ne $admins -or
    (Get-Item -LiteralPath $dataRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
  throw "$dataRoot was not created with the ACL set here (something else made it first). Remove it and re-run."
}
[System.IO.Directory]::CreateDirectory($rstDir) | Out-Null
Write-Host "Created $rstDir (SYSTEM and Administrators write, the user reads)"

# ---- 3. The task: SYSTEM, daily, startable by the user -------------------------
Register-ScheduledTask -TaskName $taskName `
  -Action (New-ScheduledTaskAction -Execute $exe -Argument "--out `"$rstDir`"" -WorkingDirectory $programDir) `
  -Trigger (New-ScheduledTaskTrigger -Daily -At '12:37') `
  -Principal (New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest) `
  -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5)) `
  -Description 'Reads the health log of NVMe drives behind Intel RST for NAND Watch: one Get Log Page command per drive, once a day. The only elevated part of NAND Watch.' `
  -Force | Out-Null

# Let the installing user read and run it (GR GX), so the app can start it
# without a prompt. Through Task Scheduler's COM API: the cmdlets cannot.
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$task = $service.GetFolder('\').GetTask($taskName)
$dacl = $task.GetSecurityDescriptor(4)   # DACL_SECURITY_INFORMATION
if ($dacl -notmatch [regex]::Escape(";;;$UserSid)")) {
  $task.SetSecurityDescriptor($dacl + "(A;;GRGX;;;$UserSid)", 0)
}
$next = (Get-ScheduledTaskInfo -TaskName $taskName).NextRunTime
Write-Host "Registered '$taskName' - as SYSTEM, daily at 12:37, startable by you. Next run $next."
exit 0
