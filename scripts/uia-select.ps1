# Select an item in a WPF ComboBox by AutomationId (T30 theme / T31 language verification).
param(
    [Parameter(Mandatory = $true)][string]$Id,
    [Parameter(Mandatory = $true)][string]$Item,
    [switch]$ShowList
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$kids = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($w in $kids) {
    $processId = $w.Current.ProcessId
    $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $proc -or $proc.ProcessName -ne 'ZeepCut') { continue }
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $combo = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $combo) { continue }

    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 400

    $names = @()
    $all = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $names += $e.Current.Name }
    }
    Write-Host ("ITEMS: " + ($names -join ' | '))
    if ($ShowList) { $expand.Collapse(); Write-Host 'LIST ONLY'; exit 0 }

    $hit = $null
    foreach ($e in $all) {
        if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $e.Current.Name -eq $Item) { $hit = $e; break }
    }
    if (-not $hit) { $expand.Collapse(); Write-Host ("ITEM NOT FOUND: " + $Item); exit 1 }
    $sel = $hit.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sel.Select()
    Start-Sleep -Milliseconds 300
    $expand.Collapse()
    Write-Host ("SELECTED {0} -> {1}" -f $Id, $Item)
    exit 0
}
Write-Host ("COMBO NOT FOUND " + $Id); exit 1
