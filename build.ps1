<#
.SYNOPSIS
    TabletModeSwitcher 打包脚本
.DESCRIPTION
    编译项目并创建安装程序
.NOTES
    需要安装:
    - .NET 8 SDK
    - Inno Setup 6 (可选，用于创建安装程序)
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
$AssetsDir = Join-Path $ProjectRoot "assets"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  $ProjectName 打包脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 创建必要的目录
if (-not (Test-Path $AssetsDir)) {
    New-Item -ItemType Directory -Path $AssetsDir | Out-Null
}
if (-not (Test-Path $InstallerDir)) {
    New-Item -ItemType Directory -Path $InstallerDir | Out-Null
}

# 步骤 1: 创建图标文件 (如果不存在)
$IconPath = Join-Path $AssetsDir "icon.ico"
if (-not (Test-Path $IconPath)) {
    Write-Host "[1/4] 创建默认图标..." -ForegroundColor Yellow

    # 使用 PowerShell 创建一个简单的 ICO 文件
    # 这是一个 16x16 的蓝色图标的二进制数据
    $icoData = @(
        0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00,
        0x68, 0x04, 0x00, 0x00, 0x16, 0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x10, 0x00,
        0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    )

    # 填充像素数据 (16x16 蓝色方块)
    $pixels = @()
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            if ($x -ge 1 -and $x -le 14 -and $y -ge 4 -and $y -le 13) {
                # 蓝色 (BGRA)
                $pixels += @(0xFF, 0x90, 0x1E, 0xFF)  # DodgerBlue
            } else {
                # 透明
                $pixels += @(0x00, 0x00, 0x00, 0x00)
            }
        }
    }

    # 创建 AND 掩码
    $andMask = @()
    for ($i = 0; $i -lt 64; $i++) {
        $andMask += 0x00
    }

    $fullData = $icoData + $pixels + $andMask
    [System.IO.File]::WriteAllBytes($IconPath, [byte[]]$fullData)

    Write-Host "  已创建默认图标: $IconPath" -ForegroundColor Green
} else {
    Write-Host "[1/4] 图标文件已存在" -ForegroundColor Green
}

# 步骤 2: 编译项目
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "[2/4] 编译项目..." -ForegroundColor Yellow

    # 清理发布目录
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

    if (-not $SelfContained) {
        $publishArgs += "-p:PublishSingleFile=true"
    }

    Write-Host "  执行: dotnet $($publishArgs -join ' ')" -ForegroundColor Gray

    Push-Location $ProjectRoot
    & dotnet @publishArgs
    $buildResult = $LASTEXITCODE
    Pop-Location

    if ($buildResult -ne 0) {
        Write-Host "  编译失败!" -ForegroundColor Red
        exit 1
    }

    Write-Host "  编译成功: $PublishDir" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[2/4] 跳过编译" -ForegroundColor Gray
}

# 步骤 3: 显示发布文件
Write-Host ""
Write-Host "[3/4] 发布文件:" -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Get-ChildItem -Path $PublishDir -Recurse | ForEach-Object {
        $size = if ($_.PSIsContainer) { "<DIR>" } else { "{0:N0} KB" -f ($_.Length / 1KB) }
        Write-Host ("  {0,-40} {1,12}" -f $_.Name, $size) -ForegroundColor Gray
    }
} else {
    Write-Host "  发布目录不存在" -ForegroundColor Red
}

# 步骤 4: 创建安装程序
if (-not $SkipInstaller) {
    Write-Host ""
    Write-Host "[4/4] 创建安装程序..." -ForegroundColor Yellow

    # 查找 Inno Setup 编译器
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
            Write-Host "  使用 Inno Setup: $isccPath" -ForegroundColor Gray
            Write-Host "  编译脚本: $issFile" -ForegroundColor Gray

            & $isccPath $issFile

            if ($LASTEXITCODE -eq 0) {
                $installerFile = Get-ChildItem -Path $InstallerDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if ($installerFile) {
                    Write-Host "  安装程序已创建: $($installerFile.FullName)" -ForegroundColor Green
                }
            } else {
                Write-Host "  创建安装程序失败!" -ForegroundColor Red
            }
        } else {
            Write-Host "  找不到 Inno Setup 脚本文件: $issFile" -ForegroundColor Red
        }
    } else {
        Write-Host "  未找到 Inno Setup，跳过安装程序创建" -ForegroundColor Yellow
        Write-Host "  下载 Inno Setup: https://jrsoftware.org/isdl.php" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  您可以手动使用以下文件:" -ForegroundColor Yellow
        Write-Host "  - 便携版: $PublishDir\$ProjectName.exe" -ForegroundColor Gray
    }
} else {
    Write-Host ""
    Write-Host "[4/4] 跳过安装程序创建" -ForegroundColor Gray
}

# 完成
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  打包完成!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "输出目录:" -ForegroundColor White
Write-Host "  便携版: $PublishDir" -ForegroundColor Gray
Write-Host "  安装程序: $InstallerDir" -ForegroundColor Gray
Write-Host ""
Write-Host "使用方法:" -ForegroundColor White
Write-Host "  1. 直接运行便携版 (需要管理员权限)" -ForegroundColor Gray
Write-Host "  2. 运行安装程序进行安装" -ForegroundColor Gray
Write-Host ""
