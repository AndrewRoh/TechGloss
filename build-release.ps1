#Requires -Version 5.1
<#
.SYNOPSIS
    TechGloss release build script
.DESCRIPTION
    Publishes GlossaryApi and TechGloss.Wpf in Release configuration to dist\.
    Output layout:
        dist\
          api\   <- GlossaryApi (self-contained, win-x64)
          wpf\   <- TechGloss.Wpf (self-contained, win-x64)
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root    = $PSScriptRoot
$DistDir = Join-Path $Root 'dist'
$ApiDist = Join-Path $DistDir 'api'
$WpfDist = Join-Path $DistDir 'wpf'
$SlnFile = Join-Path $Root 'TechGloss.sln'
$ApiProj = Join-Path $Root 'src\TechGloss.GlossaryApi\TechGloss.GlossaryApi.csproj'
$WpfProj = Join-Path $Root 'src\TechGloss.Wpf\TechGloss.Wpf.csproj'

function Write-Step([string]$msg) {
    Write-Host "`n>> $msg" -ForegroundColor Cyan
}

function Assert-DotNet {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error '.NET SDK not found. Install from https://dotnet.microsoft.com/download'
    }
    $ver = dotnet --version
    Write-Host ".NET SDK: $ver"
}

# -- 1. Prerequisites ----------------------------------------------------------
Write-Step 'Checking prerequisites'
Assert-DotNet

# -- 2. Clean dist -------------------------------------------------------------
Write-Step "Initializing dist directory: $DistDir"
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path $ApiDist | Out-Null
New-Item -ItemType Directory -Path $WpfDist | Out-Null

# -- 3. Restore ----------------------------------------------------------------
Write-Step 'NuGet restore'
dotnet restore $SlnFile
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

# -- 4. Publish GlossaryApi ----------------------------------------------------
Write-Step 'Publishing GlossaryApi (Release)'
dotnet publish $ApiProj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $ApiDist `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'GlossaryApi publish failed' }

# -- 5. Publish WPF ------------------------------------------------------------
Write-Step 'Publishing TechGloss.Wpf (Release)'
dotnet publish $WpfProj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $WpfDist `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'TechGloss.Wpf publish failed' }

# -- 6. Done -------------------------------------------------------------------
Write-Host "`n========================================" -ForegroundColor Green
Write-Host ' Build complete!' -ForegroundColor Green
Write-Host "   API : $ApiDist" -ForegroundColor Green
Write-Host "   WPF : $WpfDist" -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green
Write-Host 'Run the app: .\start.bat  (or .\start.ps1)'
