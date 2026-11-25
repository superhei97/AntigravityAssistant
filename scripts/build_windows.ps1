param(
    [string]$OutExePath = ""
)

$ErrorActionPreference = "Stop"

$ProjectDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$DistDir = Join-Path $ProjectDir "dist"
$OutExe = if ([string]::IsNullOrEmpty($OutExePath)) { Join-Path $DistDir "AntigravityAssistant.exe" } else { $OutExePath }
$ManifestPath = Join-Path $ProjectDir "Windows\app.manifest"
$SourcesDir = Join-Path $ProjectDir "Windows\Sources"

Write-Host "==> Building AntigravityAssistant for Windows (Release, x64)..." -ForegroundColor Cyan

$OutParent = Split-Path -Parent $OutExe
if (-not (Test-Path $OutParent)) {
    New-Item -ItemType Directory -Force -Path $OutParent | Out-Null
} elseif ([string]::IsNullOrEmpty($OutExePath) -and (Test-Path $OutExe)) {
    Get-Process -Name "AntigravityAssistant" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

$FrameworkDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$CscPath = Join-Path $FrameworkDir "csc.exe"
if (-not (Test-Path $CscPath)) {
    throw "Could not find built-in .NET Framework x64 compiler at $CscPath"
}

$WpfDir = Join-Path $FrameworkDir "WPF"
$Refs = @(
    "/r:$FrameworkDir\System.dll",
    "/r:$FrameworkDir\System.Core.dll",
    "/r:$FrameworkDir\System.Drawing.dll",
    "/r:$FrameworkDir\System.Windows.Forms.dll",
    "/r:$FrameworkDir\System.Net.Http.dll",
    "/r:$FrameworkDir\System.Security.dll",
    "/r:$FrameworkDir\System.Xaml.dll",
    "/r:$WpfDir\WindowsBase.dll",
    "/r:$WpfDir\PresentationCore.dll",
    "/r:$WpfDir\PresentationFramework.dll",
    "/r:$WpfDir\UIAutomationClient.dll",
    "/r:$WpfDir\UIAutomationTypes.dll"
)

$SourceFiles = @(Get-ChildItem -Path $SourcesDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName })

# Pass 1: Compile synchronous console bootstrap binary to generate crisp multi-resolution AppIcon.ico
$BootstrapExe = Join-Path $env:TEMP "AntigravityAssistant_bootstrap_$PID.exe"
$IconPath = Join-Path $env:TEMP "AntigravityAssistant_icon_$PID.ico"

$BaseArgs = @(
    "/nologo",
    "/optimize+",
    "/platform:x64",
    "/win32manifest:$ManifestPath"
) + $Refs

& $CscPath @BaseArgs "/target:exe" "/out:$BootstrapExe" @SourceFiles
if ($LASTEXITCODE -ne 0) {
    throw "Bootstrap compilation failed with exit code $LASTEXITCODE"
}

try {
    & $BootstrapExe --export-ico $IconPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IconPath)) {
        throw "Failed to generate AppIcon.ico"
    }

    # Pass 2: Compile final single-file GUI executable with embedded AppIcon.ico & PerMonitorV2 manifest
    & $CscPath @BaseArgs "/target:winexe" "/win32icon:$IconPath" "/out:$OutExe" @SourceFiles
    if ($LASTEXITCODE -ne 0) {
        throw "Final compilation failed with exit code $LASTEXITCODE"
    }
} finally {
    Remove-Item -Force $BootstrapExe -ErrorAction SilentlyContinue
    Remove-Item -Force $IconPath -ErrorAction SilentlyContinue
}

$ExeInfo = Get-Item $OutExe
$SizeKB = [math]::Round($ExeInfo.Length / 1KB, 1)
Write-Host "==> Done! Native Windows executable ready (${SizeKB} KB):" -ForegroundColor Green
Write-Host "    $OutExe"
