# Move keyboard focus to a control by AutomationId (T29 shortcut box needs real key events).
param([Parameter(Mandatory = $true)][string]$Id)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$kids = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($w in $kids) {
    $processId = $w.Current.ProcessId
    $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $proc -or $proc.ProcessName -ne 'ZeepCut') { continue }
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $hit = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($hit) {
        $hit.SetFocus()
        Write-Host ("FOCUS " + $Id + " focused=" + $hit.Current.HasKeyboardFocus)
        exit 0
    }
}
Write-Host ("NOT FOUND " + $Id); exit 1
