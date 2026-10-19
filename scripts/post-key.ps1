# Post a key press (WM_KEYDOWN + WM_KEYUP) to a window handle.
# Xxx: WPF windows only see key input via PostMessage when the real keyboard cannot reach them
# (the T23/T28 "SendKeys {ESC} cannot close the overlay" issue).
param([Parameter(Mandatory = $true)][long]$Handle, [int]$Vk = 0x1B)
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class PK {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static void Key(IntPtr h, int vk) {
    PostMessage(h, 0x0100, new IntPtr(vk), new IntPtr(0x00010001));
    System.Threading.Thread.Sleep(60);
    PostMessage(h, 0x0101, new IntPtr(vk), new IntPtr(unchecked((int)0xC0010001)));
  }
}
'@
$h = [IntPtr]$Handle
[PK]::Key($h, $Vk)
Write-Host ("POSTED key 0x{0:X} to 0x{1:X}" -f $Vk, $Handle)
