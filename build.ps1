param(
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "== 校园网自动登录（Python 版）打包 =="
Write-Host "Python: $((Get-Command python).Source)"

if (-not (Test-Path "assets\app.ico")) {
    throw "缺少 assets\app.ico"
}

$iconPath = (Resolve-Path "assets\app.ico").Path
$pyinstallerArgs = @(
    "-m", "PyInstaller",
    "--noconfirm",
    "--onefile",
    "--windowed",
    "--name", "CampusNetLogin",
    "--icon", $iconPath,
    "--version-file", (Resolve-Path "version_info.txt").Path,
    "--add-data", "$iconPath;assets",
    "--hidden-import", "pystray._win32",
    "--collect-submodules", "pystray",
    "--distpath", "dist",
    "--workpath", "build",
    "--specpath", "build",
    "main.py"
)
if (-not $NoClean) {
    $pyinstallerArgs = @("-m", "PyInstaller", "--clean") + $pyinstallerArgs[2..($pyinstallerArgs.Count - 1)]
}

& python @pyinstallerArgs
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller 打包失败，退出码 $LASTEXITCODE"
}

$exe = Join-Path $PSScriptRoot "dist\CampusNetLogin.exe"
if (-not (Test-Path $exe)) {
    throw "未生成 $exe"
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "打包完成: $exe ($size MB)"
Write-Host "下一步:"
Write-Host "  1. 双击 dist\CampusNetLogin.exe 首次设置"
Write-Host "  2. 或运行 dist\CampusNetLogin.exe --diagnose 生成诊断报告"