param(
    [string]$DotnetPath = "dotnet"
)

$ErrorActionPreference = "Stop"

# Detect if default dotnet has SDK installed
$HasSdk = $false
try {
    $sdks = & $DotnetPath --list-sdks 2>$null
    if ($sdks -and $sdks.Count -gt 0) { $HasSdk = $true }
} catch {}

if (-not $HasSdk) {
    if (Test-Path "D:\Codex\dotnet\dotnet.exe") {
        $DotnetPath = "D:\Codex\dotnet\dotnet.exe"
    } else {
        Write-Error "Could not find a valid .NET SDK. Please install .NET 10 SDK."
        exit 1
    }
}

Write-Host "===> Using .NET SDK: $DotnetPath" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$DistDir = Join-Path $ScriptDir "dist"
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path "$DistDir\portable" -Force | Out-Null
New-Item -ItemType Directory -Path "$DistDir\standalone" -Force | Out-Null

Write-Host "===> Building 1: Lightweight Portable (Framework-dependent, ~1MB)..." -ForegroundColor Yellow
& $DotnetPath publish LinkFlow.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$DistDir\portable"

Write-Host "===> Building 2: Standalone Green (Self-contained, ReadyToRun)..." -ForegroundColor Yellow
& $DotnetPath publish LinkFlow.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$DistDir\standalone"

Write-Host "===> Compressing Release Archives..." -ForegroundColor Green
Compress-Archive -Path "$DistDir\portable\*" -DestinationPath "$DistDir\LinkFlow-portable-x64.zip" -Force
Compress-Archive -Path "$DistDir\standalone\*" -DestinationPath "$DistDir\LinkFlow-standalone-x64.zip" -Force

Write-Host "`n[SUCCESS] Release packages created at: $DistDir" -ForegroundColor Green
Get-ChildItem $DistDir | Select-Object Name, Length, LastWriteTime
