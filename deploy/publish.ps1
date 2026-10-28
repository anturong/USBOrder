#Requires -Version 5.1
<#
.SYNOPSIS
    一键产出发布包：编辑器（自包含）+ service 子目录（自包含）。

.DESCRIPTION
    产物结构：
        dist\
        ├── UsbConfigEditor.exe      编辑器（含 wwwroot、运行时）
        └── service\
            └── UsbDriveService.exe  服务（编辑器安装时复制到 Program Files）

    两个程序都是 win-x64 自包含，目标机不需要装 .NET 8。

.EXAMPLE
    .\deploy\publish.ps1
    .\deploy\publish.ps1 -NugetSource https://repo.huaweicloud.com/repository/nuget/v3/index.json
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$Output = '',

    [string]$NugetSource = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'dist'
}

$editorProject = Join-Path $root 'src\UsbConfigEditor\UsbConfigEditor.csproj'
$serviceProject = Join-Path $root 'src\UsbDriveService\UsbDriveService.csproj'

function Write-Step([string]$Text) {
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Publish([string]$Project, [string]$Target) {
    if (-not [string]::IsNullOrWhiteSpace($NugetSource)) {
        & dotnet restore $Project -r win-x64 -s $NugetSource --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "还原失败：$Project" }
    }

    $arguments = @(
        'publish', $Project,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-o', $Target,
        '--nologo'
    )

    if (-not [string]::IsNullOrWhiteSpace($NugetSource)) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$Project" }
}

Write-Step "清理输出目录 $Output"
if (Test-Path $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

$null = New-Item -ItemType Directory -Path $Output -Force
$serviceOutput = Join-Path $Output 'service'

Write-Step '发布编辑器（自包含 win-x64）'
Invoke-Publish $editorProject $Output

Write-Step '发布服务（自包含 win-x64）'
Invoke-Publish $serviceProject $serviceOutput

# 发布目录里会有一些调试符号，交付包不需要。
Get-ChildItem -Path $Output -Recurse -Include *.pdb -File | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Step '校验产物'

$editorExe = Join-Path $Output 'UsbConfigEditor.exe'
$serviceExe = Join-Path $serviceOutput 'UsbDriveService.exe'
$wwwroot = Join-Path $Output 'wwwroot\index.html'

foreach ($required in @($editorExe, $serviceExe, $wwwroot)) {
    if (-not (Test-Path $required)) {
        throw "产物缺失：$required"
    }
}

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host "编辑器   : $editorExe"
Write-Host "服务     : $serviceExe"
Write-Host "前端资源 : $wwwroot"
Write-Host ("总大小   : {0:N1} MB" -f ($size / 1MB))
Write-Host "输出目录 : $Output"
