#Requires -RunAsAdministrator
<#
.SYNOPSIS
    停止并卸载 UsbDriveService；默认保留 ProgramData 下的配置与日志。

.DESCRIPTION
    只删除服务定义（以及可选的专用账号），不会动 C:\ProgramData\UsbDriveManager，
    因此重装后规则还在。需要连数据一起清掉时加 -RemoveData。

.EXAMPLE
    .\Uninstall-UsbDriveService.ps1
    .\Uninstall-UsbDriveService.ps1 -RemoveAccount -RemoveData
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'UsbDriveService',

    [string]$AccountName = 'UsbDriveMgrSvc',

    [switch]$RemoveAccount,

    [switch]$RemoveData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text)
{
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Sc
{
    param([Parameter(Mandatory)][string[]]$Arguments)

    & sc.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "sc.exe $($Arguments -join ' ') 失败，退出码 $LASTEXITCODE"
    }
}

Write-Step "停止服务 $ServiceName"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service)
{
    Write-Host '    服务不存在，跳过'
}
else
{
    if ($service.Status -ne 'Stopped')
    {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    }

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline)
    {
        $current = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $current -or $current.Status -eq 'Stopped')
        {
            break
        }

        Start-Sleep -Milliseconds 500
    }

    if ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) -and
        (Get-Service -Name $ServiceName).Status -ne 'Stopped')
    {
        throw '服务在 30 秒内未停止，放弃删除。检查是否有分配操作卡住。'
    }

    Write-Step "删除服务定义"
    Invoke-Sc -Arguments @('delete', $ServiceName)

    for ($i = 0; $i -lt 30; $i++)
    {
        if ($null -eq (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue))
        {
            break
        }

        Start-Sleep -Seconds 1
    }

    if ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue))
    {
        throw '服务仍处于“标记删除”状态，请重启后再试。'
    }

    Write-Host '    已删除'
}

if ($RemoveAccount)
{
    Write-Step "移除专用账号 $AccountName"

    $members = @(Get-LocalGroupMember -Group 'Administrators' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    $domainAccount = "$env:COMPUTERNAME\$AccountName"

    if ($members -contains $domainAccount)
    {
        Remove-LocalGroupMember -Group 'Administrators' -Member $AccountName
        Write-Host '    已从 Administrators 移除'
    }

    if ($null -ne (Get-LocalUser -Name $AccountName -ErrorAction SilentlyContinue))
    {
        Remove-LocalUser -Name $AccountName
        Write-Host '    已删除本地账号'
    }
    else
    {
        Write-Host '    账号不存在，跳过'
    }
}

if ($RemoveData)
{
    Write-Step '删除数据目录'

    $dataDir = Join-Path $env:ProgramData 'UsbDriveManager'
    if (Test-Path -LiteralPath $dataDir)
    {
        Remove-Item -LiteralPath $dataDir -Recurse -Force
        Write-Host "    已删除 $dataDir"
    }
    else
    {
        Write-Host '    数据目录不存在，跳过'
    }
}

Write-Host ''
Write-Host '卸载完成。'
if (-not $RemoveData)
{
    Write-Host "配置与日志保留在：$(Join-Path $env:ProgramData 'UsbDriveManager')"
}
