#Requires -Version 5.1
<#
.SYNOPSIS
    TechGloss startup script
.DESCRIPTION
    1. Starts dist\api\TechGloss.GlossaryApi.exe in the background
    2. Waits for /health to respond (up to 30 seconds)
    3. Launches dist\wpf\TechGloss.Wpf.exe in the foreground
    4. Stops the API process when the WPF app exits
.PARAMETER SkipBuild
    Skip build even if dist\ is missing.
#>

param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root         = $PSScriptRoot
$DistDir      = Join-Path $Root 'dist'
$ApiExe       = Join-Path $DistDir 'api\TechGloss.GlossaryApi.exe'
$WpfExe       = Join-Path $DistDir 'wpf\TechGloss.Wpf.exe'
$ApiHealthUrl = 'http://127.0.0.1:5000/health'
$ApiLogFile   = Join-Path $DistDir 'api\api.log'

function Write-Step([string]$msg) {
    Write-Host "`n>> $msg" -ForegroundColor Cyan
}

# -- 1. Ensure dist exists -----------------------------------------------------
if (-not $SkipBuild) {
    if (-not (Test-Path $ApiExe) -or -not (Test-Path $WpfExe)) {
        Write-Step 'dist not found. Running build first...'
        & "$Root\build-release.ps1"
    }
}

if (-not (Test-Path $ApiExe)) { throw "GlossaryApi executable not found: $ApiExe`nRun build-release.ps1 first." }
if (-not (Test-Path $WpfExe)) { throw "TechGloss.Wpf executable not found: $WpfExe`nRun build-release.ps1 first." }

# -- 2. Start GlossaryApi ------------------------------------------------------
Write-Step "Starting GlossaryApi: $ApiExe"

$apiWorkDir = Split-Path $ApiExe
$apiProc = Start-Process `
    -FilePath $ApiExe `
    -WorkingDirectory $apiWorkDir `
    -RedirectStandardOutput $ApiLogFile `
    -RedirectStandardError  "$apiWorkDir\api-err.log" `
    -PassThru `
    -WindowStyle Hidden

Write-Host "  PID: $($apiProc.Id)  Log: $ApiLogFile"

# -- 3. Wait for health check --------------------------------------------------
Write-Step "Waiting for API health check ($ApiHealthUrl)"
$timeout  = 30
$interval = 1
$elapsed  = 0
$ready    = $false

while ($elapsed -lt $timeout) {
    Start-Sleep -Seconds $interval
    $elapsed += $interval

    if ($apiProc.HasExited) {
        throw "GlossaryApi exited unexpectedly. Log: $ApiLogFile"
    }

    try {
        $resp = Invoke-WebRequest -Uri $ApiHealthUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) { $ready = $true; break }
    } catch { <# not ready yet #> }

    Write-Host "  Waiting... ($elapsed / $timeout sec)"
}

if (-not $ready) {
    Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
    throw "GlossaryApi did not respond within ${timeout}s. Log: $ApiLogFile"
}

Write-Host '  API is ready.' -ForegroundColor Green

# -- 4. Launch WPF (foreground) ------------------------------------------------
Write-Step "Launching TechGloss.Wpf: $WpfExe"
Start-Process -FilePath $WpfExe -WorkingDirectory (Split-Path $WpfExe) -PassThru | Out-Null

Write-Step 'WPF app closed. Stopping API...'

# -- 5. Stop API ---------------------------------------------------------------
if (-not $apiProc.HasExited) {
    Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue
    Write-Host "  GlossaryApi (PID $($apiProc.Id)) stopped."
}

Write-Host "`nShutdown complete." -ForegroundColor Yellow
