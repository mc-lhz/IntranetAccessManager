param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$assets = Join-Path $root 'assets'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw = Split-Path -Parent $csc

if (-not (Test-Path $csc)) {
    throw "找不到 .NET Framework 4.x 编译器: $csc"
}
if ($Clean -and (Test-Path $dist)) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' | ForEach-Object { $_.FullName }
$icon = Join-Path $assets 'app.ico'
$out = Join-Path $dist 'CampusNetLogin.exe'

$refs = @(
    (Join-Path $fw 'System.dll')
    (Join-Path $fw 'System.Core.dll')
    (Join-Path $fw 'System.Drawing.dll')
    (Join-Path $fw 'System.Windows.Forms.dll')
    (Join-Path $fw 'System.Web.Extensions.dll')
    (Join-Path $fw 'System.Security.dll')
)

$argsList = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    '/warn:4'
    '/out:' + $out
    '/win32icon:' + $icon
)
foreach ($r in $refs) { $argsList += '/reference:' + $r }
$argsList += $sources

Write-Host '正在编译...'
& $csc @argsList
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE"
}
if (-not (Test-Path $out)) {
    throw "未生成 $out"
}

$version = (Get-Item $out).VersionInfo.FileVersion
Write-Host ("编译完成: {0} ({1:N0} bytes)" -f $out, (Get-Item $out).Length)