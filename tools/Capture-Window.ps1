<#
.SYNOPSIS
  Launches the app, captures its window to a PNG, and optionally closes it
  again. AI Usage Native's script, pointed at NAND Watch.

.DESCRIPTION
  Used to see the UI without a person at the screen, and by
  Capture-Readme.ps1 for the README's screenshots. Captures with
  PrintWindow's PW_RENDERFULLCONTENT flag, which is what includes WinUI's
  composition-rendered content; a plain screen grab would also pick up any
  window lying on top of it.

  Screenshots go to screenshots/, which is gitignored: pointed at a real
  history, they show real drives.

  Run it with Windows PowerShell 5.1, which has System.Drawing built in.
  Keep this file pure ASCII.
#>
param(
  [string]$Exe,
  [string]$Out = 'screenshots\window.png',
  # In DIPs: scaled by the window's own DPI, so a capture covers the same
  # layout at any display scale.
  [int]$Width = 1440,
  [int]$Height = 900,
  [int]$WaitSeconds = 6,
  # Leave the size to the app (NANDWATCH_DEBUG_FULLPAGE grows the window to
  # the page's height) and capture once it has stopped changing.
  [switch]$AppSized,
  [switch]$Close
)

$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class WindowCapture {
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

  public static string Size(IntPtr h) { RECT r; GetWindowRect(h, out r); return (r.R - r.L) + "x" + (r.B - r.T); }

  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

  // Cropped to the client area: the window rectangle also holds Windows 11's
  // invisible resize borders, which PrintWindow draws as a dark frame. The app
  // draws its own title bar, so the client area is everything that is seen.
  public static void Save(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    RECT c; GetClientRect(h, out c);
    var origin = new POINT(); ClientToScreen(h, ref origin);
    using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr dc = g.GetHdc();
      PrintWindow(h, dc, 2); // PW_RENDERFULLCONTENT
      g.ReleaseHdc(dc);
      var area = new Rectangle(origin.X - r.L, origin.Y - r.T, c.R - c.L, c.B - c.T);
      area.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
      using (var client = bmp.Clone(area, bmp.PixelFormat)) client.Save(path, ImageFormat.Png);
    }
  }
}
'@

# Without this PowerShell is DPI-unaware, so on a scaled display the window
# rectangle comes back in logical pixels and the capture is cropped. -4 is
# DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2. -Width/-Height are then physical.
[void][WindowCapture]::SetProcessDpiAwarenessContext([IntPtr]::new(-4))

$proc = Start-Process -FilePath $Exe -PassThru
$deadline = (Get-Date).AddSeconds(30)
while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 250
  $proc.Refresh()
  if ($proc.HasExited) { throw "The app exited with code $($proc.ExitCode) before showing a window." }
}
if ($proc.MainWindowHandle -eq 0) { throw 'No window appeared within 30 seconds.' }

$h = $proc.MainWindowHandle
if (-not $AppSized) {
  $scale = [WindowCapture]::GetDpiForWindow($h) / 96.0
  [void][WindowCapture]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, [int]($Width * $scale), [int]($Height * $scale), 0x0004) # SWP_NOZORDER
}
[void][WindowCapture]::SetForegroundWindow($h)
Start-Sleep -Seconds $WaitSeconds
if ($AppSized) {
  # Settled = the same size for two seconds running.
  $last = ''; $since = Get-Date; $deadline = (Get-Date).AddSeconds(40)
  while ((Get-Date) -lt $deadline) {
    $now = [WindowCapture]::Size($h)
    if ($now -ne $last) { $last = $now; $since = Get-Date }
    elseif (((Get-Date) - $since).TotalSeconds -ge 2) { break }
    Start-Sleep -Milliseconds 250
  }
  Write-Output "Window settled at $last"
}

$full = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force (Split-Path $full) | Out-Null
[WindowCapture]::Save($h, $full)
Write-Output "Saved $full (PID $($proc.Id))"

if ($Close) {
  # By PID, never by name: another copy may be the one someone is using.
  Stop-Process -Id $proc.Id -Force
}
