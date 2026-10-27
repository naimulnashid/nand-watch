<#
.SYNOPSIS
  Builds the release download: one zip that installs NAND Watch with no
  .NET SDK and no clone.

.DESCRIPTION
  Publishes the same self-contained x64 builds as Install.ps1 into
  artifacts\NAND Watch\ (gitignored), laid out as Install.ps1 expects a
  release zip to be:

    app\                     NandWatch.exe (the app) and NandWatchCollector.exe
    cli\                     nandwatch.exe, the command line
    rst\                     NandWatchRstReader.exe, the optional Intel RST
                             reader (installed only with -RstReader)
    Install.ps1, Uninstall.ps1, Register-Tasks.ps1, Register-RstReader.ps1
    LICENSE, THIRD-PARTY-NOTICES.md (CrystalDiskInfo's notice must ship)

  then zips the folder as artifacts\NandWatch-<version>-win-x64.zip and
  prints its size and SHA-256, with the RST reader's SHA-256 for the release
  notes. The version is Directory.Build.props'.

  Refuses a build without NandWatch.pri, which would crash at startup, and
  one where the command line's files replaced the app's (see Install.ps1).

  Keep this file pure ASCII, in Windows PowerShell 5.1 syntax.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Package-Release.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

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

$out = Join-Path $root 'artifacts'
# The folder inside the zip is "NAND Watch", so unzipping gives one tidy folder.
$staging = Join-Path $out 'NAND Watch'
$zip = Join-Path $out "NandWatch-$version-win-x64.zip"
Remove-Item -Recurse -Force $staging, $zip -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

$app = Join-Path $staging 'app'
$publishes = @(
  @('src\NandWatch.App\NandWatch.App.csproj', $app, $true),
  @('src\NandWatch.Collector\NandWatch.Collector.csproj', $app, $true),
  @('src\NandWatch.Cli\NandWatch.Cli.csproj', (Join-Path $staging 'cli'), $true),
  # Its project already makes it one self-contained file.
  @('src\NandWatch.RstReader\NandWatch.RstReader.csproj', (Join-Path $staging 'rst'), $false)
)
foreach ($publish in $publishes) {
  Write-Host "Publishing $($publish[0])..."
  $arguments = @('publish', (Join-Path $root $publish[0]), '-c', 'Release', '-o', $publish[1], '--nologo', '-v', 'q')
  if ($publish[2]) { $arguments += @('-r', 'win-x64', '--self-contained') }
  Invoke-Native 'dotnet' $arguments
}

foreach ($file in 'app\NandWatch.exe', 'app\NandWatchCollector.exe', 'cli\nandwatch.exe', 'rst\NandWatchRstReader.exe') {
  if (-not (Test-Path (Join-Path $staging $file))) { throw "The publish produced no $file." }
}
# Compared with case: the command line's assembly is 'nandwatch' (see Install.ps1).
$appAssembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $app 'NandWatch.dll')).Name
if ($appAssembly -cne 'NandWatch') { throw "app\NandWatch.dll is not the app's (its assembly is '$appAssembly')." }
if (-not (Test-Path (Join-Path $app 'NandWatch.pri'))) {
  throw 'The published build has no NandWatch.pri, so it would crash at startup. Check EnableMsixTooling in NandWatch.App.csproj.'
}

foreach ($script in 'Install.ps1', 'Uninstall.ps1', 'Register-Tasks.ps1', 'Register-RstReader.ps1') {
  Copy-Item (Join-Path $PSScriptRoot $script) (Join-Path $staging $script)
}
foreach ($doc in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
  Copy-Item (Join-Path $root $doc) (Join-Path $staging $doc)
}

Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal

$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$rstHash = (Get-FileHash (Join-Path $staging 'rst\NandWatchRstReader.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output ("{0}  {1:N0} MB  sha256 {2}" -f $zip, ((Get-Item $zip).Length / 1MB), $zipHash)
Write-Output "rst\NandWatchRstReader.exe  sha256 $rstHash"
