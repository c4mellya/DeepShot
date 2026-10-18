# Capture the real desktop to a PNG so an agent can look at it.
param(
    [string]$Out = '',
    [int]$ScreenIndex = -1   # -1 = all screens combined
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = Join-Path $env:TEMP 'zeepcut-screen.png'
}
$screens = [System.Windows.Forms.Screen]::AllScreens
if ($screens.Count -eq 0) { Write-Host 'no screens' -ForegroundColor Red; exit 1 }
if ($ScreenIndex -ge 0) {
    if ($ScreenIndex -ge $screens.Count) { Write-Host "screen index out of range ($($screens.Count))" -ForegroundColor Red; exit 1 }
    $r = $screens[$ScreenIndex].Bounds
} else {
    $x1 = ($screens | ForEach-Object { $_.Bounds.Left } | Measure-Object -Minimum).Minimum
    $y1 = ($screens | ForEach-Object { $_.Bounds.Top } | Measure-Object -Minimum).Minimum
    $x2 = ($screens | ForEach-Object { $_.Bounds.Right } | Measure-Object -Maximum).Maximum
    $y2 = ($screens | ForEach-Object { $_.Bounds.Bottom } | Measure-Object -Maximum).Maximum
    $r = New-Object System.Drawing.Rectangle($x1, $y1, ($x2 - $x1), ($y2 - $y1))
}
$bmp = New-Object System.Drawing.Bitmap($r.Width, $r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("SAVED {0} ({1}x{2}) origin=({3},{4})" -f $Out, $r.Width, $r.Height, $r.Left, $r.Top)
