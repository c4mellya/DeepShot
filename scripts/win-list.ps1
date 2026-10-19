# List top-level windows of the Zeep Cut process (name | class | rect) for verification scripts.
# Reads nothing from the app: pure UIA + Win32 enumeration.
param([string]$Filter = 'ZeepCut')
$code = @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class WinList {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static string Dump(uint pid) {
    var sb = new StringBuilder();
    EnumWindows(delegate (IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return true;
      if (!IsWindowVisible(h)) return true;
      var t = new StringBuilder(256); GetWindowText(h, t, 256);
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      RECT r; GetWindowRect(h, out r);
      sb.AppendLine(string.Format("WIN | {0} | {1} | {2},{3} {4}x{5} hwnd=0x{6:X}", t, c, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, h.ToInt64()));
      return true;
    }, IntPtr.Zero);
    return sb.ToString();
  }
}
"@
if (-not ([System.Management.Automation.PSTypeName]'WinList').Type) { Add-Type -TypeDefinition $code }
$proc = Get-Process $Filter -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Host 'APP NOT RUNNING'; exit 1 }
Write-Host ("PID " + $proc.Id)
Write-Host ([WinList]::Dump([uint32]$proc.Id))
