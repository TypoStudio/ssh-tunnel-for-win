# build-setup.ps1
# Inno Setup installer (.exe) build script
# Requirements: .NET 8 SDK, Inno Setup 6

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectDir = "$PSScriptRoot\..\SSHTunnel4Win"
$PublishDir = Join-Path $env:TEMP "SSHTunnel4Win-publish"

Write-Host "=== SSH Tunnel Manager Setup Build ===" -ForegroundColor Cyan

# Find Inno Setup compiler
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup 6 not found. Installing via winget..." -ForegroundColor Yellow
    winget install --exact --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        Write-Host "Error: Inno Setup installation failed." -ForegroundColor Red
        Write-Host "  Download manually: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        exit 1
    }
}
Write-Host "Using Inno Setup: $iscc" -ForegroundColor Gray

# 1. dotnet publish
Write-Host "`n[1/2] Publishing app..." -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish "$ProjectDir\SSHTunnel4Win.csproj" -c $Configuration -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed." -ForegroundColor Red
    exit 1
}

# 2. Read version from csproj
$version = (Select-Xml -Path "$ProjectDir\SSHTunnel4Win.csproj" -XPath "//Version").Node.InnerText

# 3. Run Inno Setup compiler
Write-Host "`n[2/2] Building installer..." -ForegroundColor Cyan
& $iscc "$PSScriptRoot\setup.iss" /DMyAppVersion=$version /DPublishDir=$PublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Inno Setup build failed." -ForegroundColor Red
    exit 1
}

# Cleanup
Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue

$setupFile = Join-Path $PSScriptRoot "SSHTunnel4Win-$version-setup.exe"
$size = [math]::Round((Get-Item $setupFile).Length / 1MB, 1)
Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "Setup: $setupFile ($size MB)" -ForegroundColor Green
