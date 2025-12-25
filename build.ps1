<#
.SYNOPSIS
    TabletModeSwitcher Build Script
.DESCRIPTION
    Build and package the application
#>

param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller,
    [switch]$SelfContained,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$ProjectName = "TabletModeSwitcher"
$PublishDir = Join-Path $ProjectRoot "publish"
$InstallerDir = Join-Path $ProjectRoot "installer"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  $ProjectName Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Create directories
if (-not (Test-Path $InstallerDir)) {
    New-Item -ItemType Directory -Path $InstallerDir | Out-Null
}

# Step 1: Build
if (-not $SkipBuild) {
    Write-Host "[1/3] Building project..." -ForegroundColor Yellow

    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
    }

    $publishArgs = @(
        "publish"
        "-c", $Configuration
        "-r", $Runtime
        "-o", $PublishDir
        "--self-contained", $(if ($SelfContained) { "true" } else { "false" })
    )

    Write-Host "  dotnet $($publishArgs -join ' ')" -ForegroundColor Gray

    Push-Location $ProjectRoot
    & dotnet @publishArgs
    $buildResult = $LASTEXITCODE
    Pop-Location

    if ($buildResult -ne 0) {
        Write-Host "  Build failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "  Build succeeded: $PublishDir" -ForegroundColor Green
} else {
    Write-Host "[1/3] Skip build" -ForegroundColor Gray
}

# Step 2: Show files
Write-Host ""
Write-Host "[2/3] Published files:" -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Get-ChildItem -Path $PublishDir | ForEach-Object {
        $size = "{0:N0} KB" -f ($_.Length / 1KB)
        Write-Host ("  {0,-40} {1,12}" -f $_.Name, $size) -ForegroundColor Gray
    }
}

# Step 3: Create installer
if (-not $SkipInstaller) {
    Write-Host ""
    Write-Host "[3/3] Creating installer..." -ForegroundColor Yellow

    $isccPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    $isccPath = $null
    foreach ($path in $isccPaths) {
        if (Test-Path $path) {
            $isccPath = $path
            break
        }
    }

    if ($isccPath) {
        $issFile = Join-Path $ProjectRoot "installer.iss"

        if (Test-Path $issFile) {
            Write-Host "  Using Inno Setup: $isccPath" -ForegroundColor Gray

            & $isccPath $issFile

            if ($LASTEXITCODE -eq 0) {
                $installerFile = Get-ChildItem -Path $InstallerDir -Filter "*.exe" |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if ($installerFile) {
                    Write-Host "  Installer created: $($installerFile.FullName)" -ForegroundColor Green
                }
            } else {
                Write-Host "  Failed to create installer!" -ForegroundColor Red
            }
        }
    } else {
        Write-Host "  Inno Setup not found, skipping installer" -ForegroundColor Yellow
        Write-Host "  Download: https://jrsoftware.org/isdl.php" -ForegroundColor Gray
    }
} else {
    Write-Host ""
    Write-Host "[3/3] Skip installer" -ForegroundColor Gray
}

# Done
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Done!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output:" -ForegroundColor White
Write-Host "  Portable: $PublishDir" -ForegroundColor Gray
Write-Host "  Installer: $InstallerDir" -ForegroundColor Gray
Write-Host ""
