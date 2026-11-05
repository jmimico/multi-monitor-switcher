# DisplayModes.psm1 - Work/Personal monitor mode switcher
#
# Wraps the Win32 Display Configuration (CCD) API (DisplayModes.cs) and drives it
# from a profile (profiles.json) describing which monitor is the MSI, which is the
# Acer, and what each mode (work / personal) should look like.

$ErrorActionPreference = 'Stop'

if (-not ('DisplayModes.Core' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'DisplayModes.cs')
}

# --- GDI view helper (verification + primary-monitor fallback) ----------------
# NOTE: uses System.Windows.Forms.Screen rather than a direct GetMonitorInfo
# P/Invoke; on this machine the raw GetMonitorInfoW call returns E_INVALIDARG
# from console-launched processes while WinForms works fine.

function Get-DisplayModeProfile {
    param([string]$Path = (Join-Path $PSScriptRoot 'profiles.json'))
    if (-not (Test-Path $Path)) { throw "Profile not found: $Path" }
    Get-Content $Path -Raw | ConvertFrom-Json
}

function Get-DisplayState {
    <# Returns the live display configuration as an object with a .paths array.
       Each path: index, active, srcId, tgtId, srcModeIdx, x, y, w, h, name, mfr, prod. #>
    $json = [DisplayModes.Core]::QueryStateJson()
    $obj = $json | ConvertFrom-Json
    return $obj
}

function Get-GdiMonitors {
    <# GDI view: array of {device, x, y, w, h, primary}. #>
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
        [pscustomobject]@{
            device  = $_.DeviceName
            x = $_.Bounds.X
            y = $_.Bounds.Y
            w = $_.Bounds.Width
            h = $_.Bounds.Height
            primary = $_.Primary
        }
    }
}

function Get-EdidInt {
    <# Convert a hex EDID id string (e.g. '549') to its decimal value. #>
    param([string]$Hex) [Convert]::ToInt32($Hex, 16)
}

function Test-ModeMatches {
    <# Given the desired recipe entries and a state object, report whether every
       entry already matches. Returns $true when nothing would change. #>
    param($Profile, $Mode, $State)
    $spec = $Profile.modes.$Mode
    foreach ($r in $spec.paths) {
        $mon = $Profile.monitors.$($r.monitor)
        $mfr = Get-EdidInt $mon.edidMfr; $prod = Get-EdidInt $mon.edidProd
        $match = $State.paths | Where-Object { $_.mfr -eq $mfr -and $_.prod -eq $prod } |
            Select-Object -First 1
        if ($r.active) {
            if (-not $match) { return $false }
            if (-not $match.active) { return $false }
            if ($match.x -ne $r.position[0] -or $match.y -ne $r.position[1]) { return $false }
        } else {
            if ($match -and $match.active) { return $false }
        }
    }
    return $true
}

function Set-DisplayMode {
    <# Applies the 'work' or 'personal' display mode.
       - NoSave: apply for this session only, do not persist across reboot. #>
    param(
        [Parameter(Mandatory)][ValidateSet('work', 'personal')] [string]$Mode,
        [switch]$NoSave,
        [string]$ProfilePath = (Join-Path $PSScriptRoot 'profiles.json')
    )

    $profile = Get-DisplayModeProfile -Path $ProfilePath
    $spec = $profile.modes.$Mode
    if (-not $spec) { throw "Mode '$Mode' is not defined in the profile." }

    $before = Get-DisplayState

    if (Test-ModeMatches -Profile $profile -Mode $Mode -State $before) {
        Write-Host "Already in '$Mode' mode - nothing to do."
        return [pscustomobject]@{ Mode = $Mode; Applied = $false; AlreadyInMode = $true; Verified = $true; Details = @() }
    }

    # build identity map:  tag=EDIDmfr:EDIDprod
    $identParts = @()
    foreach ($p in $profile.monitors.PSObject.Properties) {
        $identParts += "$($p.Name)=$($p.Value.edidMfr):$($p.Value.edidProd)"
    }
    $identityMap = $identParts -join ';'

    # build recipe:  tag=on@x,y | tag=off
    $recipeParts = @()
    foreach ($r in $spec.paths) {
        if ($r.active) { $recipeParts += "$($r.monitor)=on@$($r.position[0]),$($r.position[1])" }
        else           { $recipeParts += "$($r.monitor)=off" }
    }
    $recipe = $recipeParts -join ';'

    Write-Host "Switching to '$Mode' mode: $recipe"

    $afterJson = $null; $errMsg = $null
    $rc = [DisplayModes.Core]::ApplyMode($identityMap, $recipe, (-not $NoSave), [ref]$afterJson, [ref]$errMsg)

    $result = [pscustomobject]@{
        Mode = $Mode; Applied = ($rc -eq 0); AlreadyInMode = ($rc -eq 100)
        Verified = $false; Details = @()
    }

    if ($rc -ne 0 -and $rc -ne 100) {
        throw "SetDisplayConfig failed (code $rc): $errMsg"
    }
    if ($rc -eq 100) { return $result }

    # ------- verify -------
    Start-Sleep -Milliseconds 700
    $after = $afterJson | ConvertFrom-Json

    # 1) CCD-level check: active set + positions
    foreach ($r in $spec.paths) {
        $mon = $profile.monitors.$($r.monitor)
        $mfr = Get-EdidInt $mon.edidMfr; $prod = Get-EdidInt $mon.edidProd
        $m = $after.paths | Where-Object { $_.mfr -eq $mfr -and $_.prod -eq $prod } |
            Where-Object { $_.active } | Select-Object -First 1
        if ($r.active) {
            if (-not $m) { $result.Details += "FAIL: $($r.monitor) not active after apply"; continue }
            if ($m.x -ne $r.position[0] -or $m.y -ne $r.position[1]) {
                $result.Details += "FAIL: $($r.monitor) at ($($m.x),$($m.y)), expected ($($r.position[0]),$($r.position[1]))"
            } else {
                $result.Details += "OK: $($r.monitor) active at ($($m.x),$($m.y)) $($m.w)x$($m.h)"
            }
        } else {
            if ($m) { $result.Details += "FAIL: $($r.monitor) still active after apply" }
            else    { $result.Details += "OK: $($r.monitor) turned off" }
        }
    }

    # 2) GDI-level check: which monitor is actually primary
    $gdi = Get-GdiMonitors
    $expectedPrimary = $spec.paths | Where-Object { $_.active } |
        Where-Object { $_.position[0] -eq 0 -and $_.position[1] -eq 0 } | Select-Object -First 1
    $gdiPrimary = $gdi | Where-Object { $_.primary } | Select-Object -First 1

    if ($expectedPrimary -and $gdiPrimary) {
        $expMon = $profile.monitors.$($expectedPrimary.monitor)
        $emfr = Get-EdidInt $expMon.edidMfr; $eprod = Get-EdidInt $expMon.edidProd
        # match the GDI monitor at the expected primary position to the CCD state
        $ok = $false
        foreach ($gm in $gdi) {
            $gmMon = $after.paths | Where-Object { $_.active -and $_.x -eq $gm.x -and $_.y -eq $gm.y } | Select-Object -First 1
            if ($gmMon -and $gmMon.mfr -eq $emfr -and $gmMon.prod -eq $eprod -and $gm.primary) { $ok = $true; break }
        }
        if ($ok) {
            $result.Details += "OK: primary display is $($expectedPrimary.monitor) ($($gdiPrimary.device))"
        } else {
            $result.Details += "WARN: GDI primary is '$($gdiPrimary.device)' at ($($gdiPrimary.x),$($gdiPrimary.y)), expected $($expectedPrimary.monitor). Trying CDS_SET_PRIMARY fallback..."
            # find the GDI device of the expected primary monitor
            $dev = $null
            foreach ($gm in $gdi) {
                $gmMon = $after.paths | Where-Object { $_.active -and $_.x -eq $gm.x -and $_.y -eq $gm.y } | Select-Object -First 1
                if ($gmMon -and $gmMon.mfr -eq $emfr -and $gmMon.prod -eq $eprod) { $dev = $gm.device; break }
            }
            if ($dev) {
                $perr = $null
                [void][DisplayModes.Core]::SetPrimaryGdi($dev, [ref]$perr)
                Start-Sleep -Milliseconds 700
                $gdi = Get-GdiMonitors
                $gdiPrimary = $gdi | Where-Object { $_.primary } | Select-Object -First 1
                if ($gdiPrimary -and $gdiPrimary.device -eq $dev) {
                    $result.Details += "OK: primary corrected via CDS_SET_PRIMARY on $dev"
                    $ok = $true
                } else {
                    $result.Details += "FAIL: could not correct primary (last: $($gdiPrimary.device))"
                }
            }
        }
    }

    $result.Verified = -not ($result.Details | Where-Object { $_ -like 'FAIL*' })
    foreach ($d in $result.Details) { Write-Host "  $d" }
    if (-not $result.Verified) { throw "Mode applied but verification failed. Run Show-DisplayState.ps1 to inspect." }
    return $result
}

Export-ModuleMember -Function Get-DisplayModeProfile, Get-DisplayState, Get-GdiMonitors, Get-EdidInt, Test-ModeMatches, Set-DisplayMode
