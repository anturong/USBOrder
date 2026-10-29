#Requires -Version 5.1
<#
.SYNOPSIS
    生成 MSI 安装包：先扫描 dist 生成 WiX 片段，再编译成 MSI。

.DESCRIPTION
    - 前置：先跑 deploy/publish.ps1 产出 dist\；
    - 产物：release\USBOrder-<Version>-win-x64.msi；
    - 安装内容：编辑器 + service\ + wwwroot，开始菜单和桌面快捷方式。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy/build-msi.ps1
    powershell -ExecutionPolicy Bypass -File deploy/build-msi.ps1 -Version 0.1.5
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$NugetSource = '',

    [string]$Version = '0.1.5',

    [string]$Output = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path (Join-Path $root 'release') "USBOrder-$Version-win-x64.msi"
}

$makeWxs = Join-Path $PSScriptRoot 'make-wxs.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $makeWxs
if ($LASTEXITCODE -ne 0) { throw '生成 WiX 片段失败' }

$project = Join-Path $root 'installer\Installer.wixproj'
$arguments = @('build', $project, '-c', $Configuration, '--nologo', "-p:ProductVersion=$Version")
if (-not [string]::IsNullOrWhiteSpace($NugetSource)) {
    $arguments += "-p:RestoreSources=$NugetSource"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '编译 MSI 失败' }

$msi = Join-Path $root "installer\bin\$Configuration\UsbDriveManager.msi"
if (-not (Test-Path $msi)) { throw "没有找到产物：$msi" }

$outputDir = Split-Path $Output -Parent
if (-not (Test-Path $outputDir)) { $null = New-Item -ItemType Directory -Path $outputDir -Force }

Copy-Item -LiteralPath $msi -Destination $Output -Force
Write-Host ''
Write-Host "MSI: $Output"
Write-Host ("大小: {0:N1} MB" -f ((Get-Item $Output).Length / 1MB))
