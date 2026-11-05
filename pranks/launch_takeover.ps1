$ErrorActionPreference = 'SilentlyContinue'

# ── Resolve path to the HTML file next to this script ────────────────────────
$html = Join-Path $PSScriptRoot 'takeover_screen.html'
$uri  = 'file:///' + $html.Replace('\','/')

# ── Try to find a browser we can drive into kiosk/fullscreen mode ─────────────
$chrome  = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$edge    = @(
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$firefox = @(
    "$env:ProgramFiles\Mozilla Firefox\firefox.exe",
    "$env:ProgramFiles (x86)\Mozilla Firefox\firefox.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

# ── Launch in kiosk mode (full screen, no address bar, no UI chrome) ──────────
if ($chrome) {
    Start-Process $chrome "--kiosk `"$uri`" --noerrdialogs --disable-infobars --no-first-run --disable-session-crashed-bubble"
} elseif ($edge) {
    Start-Process $edge   "--kiosk `"$uri`" --noerrdialogs --disable-infobars --no-first-run"
} elseif ($firefox) {
    # Firefox doesn't have --kiosk on all versions, fall back to fullscreen flag
    Start-Process $firefox "-kiosk `"$uri`""
} else {
    # Last resort: open with default browser and send F11 to go fullscreen
    Start-Process $uri
    Start-Sleep -Milliseconds 1500
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('{F11}')
}
