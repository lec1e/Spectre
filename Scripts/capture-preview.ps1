Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
public class Cap4 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public static IntPtr Largest(int pid) {
    IntPtr best = IntPtr.Zero; int area = 0;
    EnumWindows((h, l) => {
      uint wpid; GetWindowThreadProcessId(h, out wpid);
      if (wpid == (uint)pid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        int a = (r.Right-r.Left)*(r.Bottom-r.Top);
        if (a > area && r.Right-r.Left > 400) { area = a; best = h; }
      }
      return true;
    }, IntPtr.Zero);
    return best;
  }
  public static void Capture(IntPtr hwnd, string path) {
    RECT r; GetWindowRect(hwnd, out r);
    int w = r.Right-r.Left, ht = r.Bottom-r.Top;
    SetForegroundWindow(hwnd); Thread.Sleep(250);
    using (var bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb))
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr hdc = g.GetHdc();
      PrintWindow(hwnd, hdc, 2);
      g.ReleaseHdc(hdc);
      bmp.Save(path, ImageFormat.Png);
    }
  }
}
"@ -ReferencedAssemblies System.Drawing

$bin = "c:\Users\6xesh\Documents\scripts\vscode\Roblox\Eclipse\Froststrap\bin\Release\net10.0"
$exe = Join-Path $bin "Spectre.exe"
$out = "c:\Users\6xesh\Documents\scripts\vscode\Roblox\Eclipse\.github\preview"
Copy-Item -Force (Join-Path $bin "Eclipse.exe") $exe

function Stop-App {
  Get-Process -Name Spectre,Eclipse -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
}

$pages = @(
  @{ P="home"; F="home.png" },
  @{ P="games"; F="games.png" },
  @{ P="library"; F="library.png" },
  @{ P="custommods"; F="mods.png" },
  @{ P="settings"; F="settings.png" }
)

foreach ($item in $pages) {
  Stop-App
  $proc = Start-Process -FilePath $exe -ArgumentList @("-settings","-page",$item.P) -WorkingDirectory $bin -PassThru
  $done = $false
  for ($i = 0; $i -lt 22; $i++) {
    Start-Sleep -Seconds 1
    $h = [Cap4]::Largest($proc.Id)
    if ($h -ne [IntPtr]::Zero) {
      Start-Sleep -Seconds 3
      [Cap4]::Capture($h, (Join-Path $out $item.F))
      Write-Host "ok $($item.F) $((Get-Item (Join-Path $out $item.F)).Length)"
      $done = $true
      break
    }
  }
  if (-not $done) { Write-Host "fail $($item.F)" }
}

Stop-App
Copy-Item -Force (Join-Path $out "home.png") (Join-Path $out "app-preview.png")
Get-ChildItem $out -Filter *.png | Format-Table Name, Length
