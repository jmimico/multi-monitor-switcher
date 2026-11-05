# One-click: switch to PERSONAL mode (MSI = main, Acer = extended)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DisplayModes.psm1') -DisableNameChecking
try {
    $r = Set-DisplayMode -Mode personal
    exit 0
} catch {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            "Could not switch to PERSONAL mode.`n`n$($_.Exception.Message)",
            'Display Mode Switcher', 'OK', 'Error') | Out-Null
    } catch {
        Write-Error $_.Exception.Message
    }
    exit 1
}
