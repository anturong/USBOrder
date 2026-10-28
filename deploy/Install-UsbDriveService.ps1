#Requires -RunAsAdministrator
<#
.SYNOPSIS
    安装 / 更新 UsbDriveService：创建专用服务账号、授予“作为服务登录”、注册服务并启动。

.DESCRIPTION
    与 docs/service-startup.md 的配置一一对应：
      - 启动类型 Automatic + 延迟启动
      - 专用本地账号（Administrators、密码永不过期、作为服务登录）
      - 失败恢复：60s / 60s / 120s，1 天重置计数
      - ProgramData\UsbDriveManager 显式 ACL（服务账号可写、管理员完全控制）

    服务已存在时会先停止并删除再重建（配置在 ProgramData 里，不受影响），
    这样避免用 sc.exe 改密码带来的转义问题（方案坑位 #9）。

.EXAMPLE
    .\Install-UsbDriveService.ps1
    .\Install-UsbDriveService.ps1 -ServiceExe .\src\UsbDriveService\bin\Release\net8.0-windows\UsbDriveService.exe
#>
[CmdletBinding()]
param(
    [string]$ServiceExe = (Join-Path $PSScriptRoot '..\src\UsbDriveService\bin\Release\net8.0-windows\UsbDriveService.exe'),

    [string]$InstallDir = (Join-Path $env:ProgramFiles 'UsbDriveManager'),

    [switch]$SkipCopy,

    [string]$ServiceName = 'UsbDriveService',

    [string]$DisplayName = 'USB 盘符自动管理服务',

    [string]$Description = '监听 U 盘/移动硬盘插拔，按规则分配固定盘符；冲突时拒绝并记录日志。',

    [string]$AccountName = 'UsbDriveMgrSvc',

    [SecureString]$AccountPassword,

    [string]$DataDir = (Join-Path $env:ProgramData 'UsbDriveManager')
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

# ---------------------------------------------------------------- 0. 前置检查

Write-Step '前置检查'

if (-not (Test-Path -LiteralPath $ServiceExe))
{
    throw "找不到服务可执行文件：$ServiceExe`n先执行：dotnet build .\UsbDriveManager.sln -c Release"
}

$ServiceExe = (Resolve-Path -LiteralPath $ServiceExe).Path

# 先把已运行的服务停掉：否则 Program Files 里的 exe/dll 被占用，复制会失败。
$running = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $running -and $running.Status -ne 'Stopped')
{
    Write-Step "停止现有服务 $ServiceName"

    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

    $stopDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $stopDeadline)
    {
        $current = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $current -or $current.Status -eq 'Stopped')
        {
            break
        }

        Start-Sleep -Milliseconds 500
    }
}

if (-not $SkipCopy)
{
    Write-Step "复制程序到 $InstallDir"

    $sourceDir = Split-Path -Parent $ServiceExe
    $null = New-Item -ItemType Directory -Path $InstallDir -Force

    & robocopy $sourceDir $InstallDir /E /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8)
    {
        throw "复制程序文件失败（robocopy 退出码 $LASTEXITCODE）"
    }

    $ServiceExe = Join-Path $InstallDir 'UsbDriveService.exe'
    Write-Host "    源目录：$sourceDir"
    Write-Host "    目标  ：$InstallDir"
}

if (-not $AccountPassword)
{
    $AccountPassword = Read-Host -AsSecureString "为服务账号 $AccountName 设置密码"
}

if ($AccountPassword.Length -lt 12)
{
    Write-Warning '服务账号密码长度小于 12 位，建议使用更强的密码。'
}

$domainAccount = "$env:COMPUTERNAME\$AccountName"

# ---------------------------------------------------------------- 1. 服务账号

Write-Step "准备服务账号 $domainAccount"

$account = Get-LocalUser -Name $AccountName -ErrorAction SilentlyContinue
if ($null -eq $account)
{
    New-LocalUser -Name $AccountName `
        -Password $AccountPassword `
        -PasswordNeverExpires `
        -UserMayNotChangePassword `
        -Description 'USB 盘符自动管理服务专用账号' | Out-Null
    Write-Host "    已创建本地账号 $AccountName"
}
else
{
    # 注意：Set-LocalUser 在 Windows PowerShell 5.1 没有 -UserMayNotChangePassword，
    # 该属性只在创建时设置；这里只轮换密码并确保不过期。
    Set-LocalUser -Name $AccountName `
        -Password $AccountPassword `
        -PasswordNeverExpires $true
    Write-Host "    已更新账号密码与过期策略"
}

$administrators = @(Get-LocalGroupMember -Group 'Administrators' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
if ($administrators -notcontains $domainAccount)
{
    Add-LocalGroupMember -Group 'Administrators' -Member $AccountName
    Write-Host '    已加入 Administrators 组'
}
else
{
    Write-Host '    已在 Administrators 组'
}

# ---------------------------------------------------------------- 2. 作为服务登录

Write-Step '授予 SeServiceLogonRight（作为服务登录）'

if (-not ('UsbDriveManager.ServiceRights' -as [type]))
{
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace UsbDriveManager
{
    public static class ServiceRights
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LSA_OBJECT_ATTRIBUTES
        {
            public int Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint LsaOpenPolicy(IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES attributes, uint accessMask, out IntPtr policyHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint LsaAddAccountRights(IntPtr policyHandle, byte[] accountSid, LSA_UNICODE_STRING[] userRights, uint countOfRights);

        [DllImport("advapi32.dll")]
        private static extern uint LsaClose(IntPtr objectHandle);

        [DllImport("advapi32.dll")]
        private static extern uint LsaNtStatusToWinError(uint status);

        private const uint PolicyAllAccess = 0x00F0FFF;

        public static void GrantLogonAsService(string accountName)
        {
            SecurityIdentifier sid = (SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
            byte[] sidBytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(sidBytes, 0);

            const string right = "SeServiceLogonRight";
            LSA_UNICODE_STRING[] rights = new LSA_UNICODE_STRING[1];
            rights[0].Buffer = Marshal.StringToHGlobalUni(right);
            rights[0].Length = (ushort)(right.Length * 2);
            rights[0].MaximumLength = (ushort)((right.Length + 1) * 2);

            LSA_OBJECT_ATTRIBUTES attributes = new LSA_OBJECT_ATTRIBUTES();
            attributes.Length = Marshal.SizeOf(typeof(LSA_OBJECT_ATTRIBUTES));

            IntPtr policyHandle;
            uint status = LsaOpenPolicy(IntPtr.Zero, ref attributes, PolicyAllAccess, out policyHandle);
            if (status != 0)
            {
                throw new Win32Exception((int)LsaNtStatusToWinError(status));
            }

            try
            {
                status = LsaAddAccountRights(policyHandle, sidBytes, rights, 1);
                if (status != 0)
                {
                    throw new Win32Exception((int)LsaNtStatusToWinError(status));
                }
            }
            finally
            {
                LsaClose(policyHandle);
                Marshal.FreeHGlobal(rights[0].Buffer);
            }
        }
    }
}
'@
}

[UsbDriveManager.ServiceRights]::GrantLogonAsService($domainAccount)
Write-Host '    已授予'

# ---------------------------------------------------------------- 3. 数据目录与 ACL

Write-Step "准备数据目录 $DataDir"

$null = New-Item -ItemType Directory -Path $DataDir -Force
$null = New-Item -ItemType Directory -Path (Join-Path $DataDir 'logs') -Force

$acl = Get-Acl -LiteralPath $DataDir
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $domainAccount,
    [System.Security.AccessControl.FileSystemRights]::Modify,
    [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
    [System.Security.AccessControl.PropagationFlags]::None,
    [System.Security.AccessControl.AccessControlType]::Allow)

$acl.SetAccessRule($rule)
Set-Acl -LiteralPath $DataDir -AclObject $acl
Write-Host '    已设置服务账号 Modify 权限'

# ---------------------------------------------------------------- 4. 重建服务

Write-Step "注册服务 $ServiceName"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing)
{
    Write-Host '    检测到同名服务，先停止并删除'
    if ($existing.Status -ne 'Stopped')
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

    Invoke-Sc -Arguments @('delete', $ServiceName)

    for ($i = 0; $i -lt 30; $i++)
    {
        if ($null -eq (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue))
        {
            break
        }

        Start-Sleep -Seconds 1
    }
}

$credential = New-Object System.Management.Automation.PSCredential($domainAccount, $AccountPassword)

New-Service -Name $ServiceName `
    -BinaryPathName "`"$ServiceExe`"" `
    -DisplayName $DisplayName `
    -StartupType Automatic `
    -Credential $credential | Out-Null

Invoke-Sc -Arguments @('config', $ServiceName, 'start=', 'delayed-auto')
Invoke-Sc -Arguments @('description', $ServiceName, $Description)
Invoke-Sc -Arguments @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/60000/restart/60000/restart/120000')

Write-Host '    已注册（延迟自动启动 + 失败恢复 60s/60s/120s）'

# ---------------------------------------------------------------- 5. 启动

Write-Step '启动服务'

Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$service = Get-Service -Name $ServiceName
Write-Host ''
Write-Host "服务名   : $($service.Name)"
Write-Host "显示名   : $($service.DisplayName)"
Write-Host "状态     : $($service.Status)"
Write-Host "启动类型 : $($service.StartType)（已设置延迟启动）"
Write-Host "账号     : $domainAccount"
Write-Host "程序     : $ServiceExe"
Write-Host "日志目录 : $(Join-Path $DataDir 'logs')"

if ($service.Status -ne 'Running')
{
    throw "服务未进入 Running，当前状态：$($service.Status)。检查账号密码、SeServiceLogonRight 与事件日志。"
}
