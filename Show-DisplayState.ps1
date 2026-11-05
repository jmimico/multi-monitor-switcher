# Shows the current display state and which mode it looks like.
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DisplayModes.psm1') -DisableNameChecking

$profile = Get-DisplayModeProfile
$state   = Get-DisplayState
$gdi     = Get-GdiMonitors

Write-Host ""
Write-Host "=== Active displays (display-config view) ==="
$active = $state.paths | Where-Object { $_.active }
foreach ($p in $active) {
    $tag = $null
    foreach ($mp in $profile.monitors.PSObject.Properties) {
        $mfr = Get-EdidInt $mp.Value.edidMfr; $prod = Get-EdidInt $mp.Value.edidProd
        if ($p.mfr -eq $mfr -and $p.prod -eq $prod) { $tag = $mp.Name; break }
    }
    $label = if ($tag) { "{0} ({1})" -f $tag.ToUpper(), $p.name } else { $p.name }
    $mark = ""
    if ($p.x -eq 0 -and $p.y -eq 0) { $mark = "  <-- at (0,0) = primary" }
    Write-Host ("  {0,-28} {1,5}x{2,-5} @ ({3},{4}){5}" -f $label, $p.w, $p.h, $p.x, $p.y, $mark)
}
if (-not $active) { Write-Host "  (none)" }

Write-Host ""
Write-Host "=== GDI monitor view (what Windows desktop sees) ==="
foreach ($g in $gdi) {
    $mark = if ($g.primary) { "  <-- GDI primary" } else { "" }
    Write-Host ("  {0,-16} ({1},{2}) {3}x{4}{5}" -f $g.device, $g.x, $g.y, $g.w, $g.h, $mark)
}

Write-Host ""
Write-Host "=== Mode detection ==="
$inWork = Test-ModeMatches -Profile $profile -Mode work -State $state
$inPersonal = Test-ModeMatches -Profile $profile -Mode personal -State $state
if ($inWork)        { Write-Host "  Currently matches: WORK mode" }
elseif ($inPersonal){ Write-Host "  Currently matches: PERSONAL mode" }
else                { Write-Host "  Currently matches: NEITHER (custom configuration)" }
Write-Host ""
