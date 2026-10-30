#Requires -Version 5.1
<#
.SYNOPSIS
    用 Inno Setup 编译图形安装包（setup.exe）。

.DESCRIPTION
    - 前置：deploy/publish.ps1 产出 dist\；
    - 依赖：Inno Setup 7（ISCC.exe），默认找 PATH 或 "C:\Program Files\Inno Setup 7\ISCC.exe"；
    - 产物：release\USBOrder-<Version>-setup.exe。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy/build-inno.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = '0.2.0',

    [string]$IsccPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $IsccPath = $command.Source
    }
    else {
        $candidates = @(
            'C:\Program Files\Inno Setup 7\ISCC.exe',
            'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe',
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
        )

        foreach ($candidate in $candidates) {
            if (Test-Path $candidate) {
                $IsccPath = $candidate
                break
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path $IsccPath)) {
    throw '找不到 ISCC.exe，请先安装 Inno Setup 7：https://jrsoftware.org/isdl.php'
}

$iss = Join-Path $root 'installer\usborder.iss'
& $IsccPath $iss "/DAppVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 编译失败' }

$setup = Join-Path (Join-Path $root 'release') "USBOrder-$Version-setup.exe"
if (-not (Test-Path $setup)) { throw "没有找到产物：$setup" }

Write-Host ''
Write-Host "Setup: $setup"
Write-Host ("大小: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
