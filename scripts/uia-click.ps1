# Click a WPF control by AutomationId (or Name) inside the Zeep Cut process.
param(
    [Parameter(Mandatory = $true)][string]$Id,
    [switch]$ByName
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$kids = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$found = $false
foreach ($w in $kids) {
    $processId = $w.Current.ProcessId
    $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $proc) { continue }
    if ($proc.ProcessName -ne 'ZeepCut') { continue }
    if ($ByName) {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Id)
    } else {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    }
    $hit = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($hit) {
        $pattern = $null
        if ($hit.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
            Write-Host ("INVOKED " + $Id)
            $found = $true
            break
        }
        # TextBlock / non-invokable: click its centre with the mouse.
        $r = $hit.Current.BoundingRectangle
        $x = [int]($r.X + $r.Width / 2)
        $y = [int]($r.Y + $r.Height / 2)
        Write-Host ("NONINVOKABLE " + $Id + " at " + $x + "," + $y)
        $found = $true
        break
    }
}
if (-not $found) { Write-Host ("NOT FOUND " + $Id); exit 1 }
