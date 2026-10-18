# Verification-only helper: show a deterministic high-frequency texture window.
#
# T17's mosaic must be REAL pixelation (block averaging of the pixels underneath).
# A flat desktop region proves nothing, so this WinForms window (verification scripts
# may use WinForms; the app itself stays WPF + Win32) paints small colour tiles plus
# dense text at a known physical rectangle.
#
# Example:
#   ... texture-window.ps1 -X 350 -Y 250 -Width 760 -Height 620 -Seconds 120
param(
    [int]$X = 350,
    [int]$Y = 250,
    [int]$Width = 760,
    [int]$Height = 620,
    [int]$Seconds = 120
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ZeepCutTexture
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@

[ZeepCutTexture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Bounds = New-Object System.Drawing.Rectangle($X, $Y, $Width, $Height)
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.Text = 'ZeepCutTexture'
$form.BackColor = [System.Drawing.Color]::White

$picture = New-Object System.Windows.Forms.PictureBox
$picture.Dock = [System.Windows.Forms.DockStyle]::Fill
$picture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Normal
$form.Controls.Add($picture)

$form.Show()
[System.Windows.Forms.Application]::DoEvents()

# Force the PHYSICAL rectangle: WinForms Bounds can end up smaller than requested on a
# 125% screen, while SetWindowPos works in device pixels (SWP_NOZORDER = 0x0004).
$hwnd = $form.Handle
[ZeepCutTexture]::SetWindowPos($hwnd, [IntPtr]::Zero, $X, $Y, $Width, $Height, 0x0004) | Out-Null
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 250

$r = New-Object ZeepCutTexture+RECT
[ZeepCutTexture]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$realWidth = $r.Right - $r.Left
$realHeight = $r.Bottom - $r.Top
if ($realWidth -le 0) { $realWidth = $Width }
if ($realHeight -le 0) { $realHeight = $Height }
Write-Host ("TEXTURE WINDOW rect=({0},{1}) {2}x{3} hwnd=0x{4:X}" -f $r.Left, $r.Top, $realWidth, $realHeight, $hwnd.ToInt64())

# --- Paint the texture at the real physical size ---
$bitmap = New-Object System.Drawing.Bitmap($realWidth, $realHeight)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
$g.Clear([System.Drawing.Color]::White)

# 1) small colour tiles (12x12): real pixelation turns these into ~12px blocks of averages
$palette = @(
    [System.Drawing.Color]::FromArgb(220, 40, 40),
    [System.Drawing.Color]::FromArgb(40, 180, 60),
    [System.Drawing.Color]::FromArgb(40, 80, 230),
    [System.Drawing.Color]::FromArgb(240, 200, 20),
    [System.Drawing.Color]::FromArgb(250, 250, 250),
    [System.Drawing.Color]::FromArgb(20, 20, 20)
)
$tile = 12
$border = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 120, 120), 1)
$columns = [int]([Math]::Floor($realWidth / $tile))
for ($ty = 0; $ty -lt 16; $ty++) {
    for ($tx = 0; $tx -lt $columns; $tx++) {
        $color = $palette[($tx + $ty) % $palette.Count]
        $brush = New-Object System.Drawing.SolidBrush($color)
        $g.FillRectangle($brush, ($tx * $tile), ($ty * $tile), $tile, $tile)
        $brush.Dispose()
        $g.DrawRectangle($border, ($tx * $tile), ($ty * $tile), $tile, $tile)
    }
}

# 2) dense text lines (high frequency strokes for the block average)
$font = New-Object System.Drawing.Font('Consolas', 14, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(10, 10, 10))
$lines = @(
    'The quick brown fox jumps over the lazy dog 0123456789',
    'ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz',
    'Zeep Cut T17 mosaic must average THESE pixels',
    'WWWW MMMM IIII WWWW MMMM IIII 8888 0000',
    '/// \\\ ||| === +++ --- *** ### @@@ $$$ %%%',
    'line 06 - mosaic block average check 0123456789',
    'line 07 - quick brown fox jumps over the dog',
    'line 08 - THE QUICK BROWN FOX JUMPS OVER IT',
    'line 09 - pixelation is real, not a pattern',
    'line 10 - red green blue yellow white black',
    'line 11 - 0123456789 0123456789 0123456789',
    'line 12 - end of the texture block'
)
$textTop = 210
foreach ($line in $lines) {
    $g.DrawString($line, $font, $textBrush, 8, $textTop)
    $textTop += 20
}

$g.Dispose()
$picture.Image = $bitmap
$form.Activate()
$form.Refresh()
[System.Windows.Forms.Application]::DoEvents()

$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 50
}

$picture.Image = $null
$bitmap.Dispose()
$font.Dispose()
$textBrush.Dispose()
$border.Dispose()
$form.Close()
Write-Host 'TEXTURE WINDOW CLOSED'
