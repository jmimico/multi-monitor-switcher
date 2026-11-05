# Creates desktop shortcuts for one-click mode switching.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
$ws = New-Object -ComObject WScript.Shell

$shortcuts = @(
    @{
        Name = 'Work Mode';  Script = 'Set-WorkMode.ps1';   Icon = "$env:SystemRoot\System32\imageres.dll,109"
    },
    @{
        Name = 'Personal Mode'; Script = 'Set-PersonalMode.ps1'; Icon = "$env:SystemRoot\System32\imageres.dll,108"
    }
)

foreach ($s in $shortcuts) {
    $lnkPath = Join-Path $desktop ($s.Name + '.lnk')
    $lnk = $ws.CreateShortcut($lnkPath)
    $lnk.TargetPath = Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $lnk.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $here $s.Script) + '"'
    $lnk.WorkingDirectory = $here
    $lnk.IconLocation = $s.Icon
    $lnk.Description = "Switch monitors to $($s.Name) (Acer=main, MSI=off / MSI=main, Acer=extended)"
    $lnk.Save()
    Write-Host "Created: $lnkPath"
}
Write-Host ""
Write-Host "Done. Double-click the desktop shortcuts to switch modes."
