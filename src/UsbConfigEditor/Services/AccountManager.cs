using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using UsbDriveManager.Core;
using UsbDriveManager.Editor.Native;

namespace UsbDriveManager.Editor.Services;

/// <summary>本地服务账号的创建、密码、组与服务登录权。</summary>
internal static class AccountManager
{
    private const int ErrorMemberInAlias = 1378;

    public static string DomainAccount(string account)
        => account.Contains('\\') ? account : $"{Environment.MachineName}\\{account}";

    public static string UserPart(string account)
        => account.Contains('\\') ? account[(account.LastIndexOf('\\') + 1)..] : account;

    public static void ValidateLogon(string account, string password)
    {
        var qualified = DomainAccount(account);
        var split = qualified.IndexOf('\\');
        var domain = qualified[..split];
        var user = qualified[(split + 1)..];

        if (!NativeMethods.LogonUser(user, domain, password, NativeMethods.Logon32LogonInteractive, NativeMethods.Logon32ProviderDefault, out var token))
        {
            var error = Marshal.GetLastWin32Error();
            var message = error switch
            {
                1326 => "账号或密码不正确。",
                1331 => "该账号已被禁用。",
                1907 => "该账号的密码已过期，请先改密。",
                _ => $"账号验证失败（错误 {error}）。",
            };

            throw new Win32Exception(error, message);
        }

        NativeMethods.CloseHandle(token);
    }

    public static bool IsAdministrator(string account)
    {
        var status = NativeMethods.NetUserGetLocalGroups(
            null,
            UserPart(account),
            0,
            NativeMethods.LgIncludeIndirect,
            out var buffer,
            0,
            out var entriesRead,
            out _);

        if (status != NativeMethods.NerrSuccess || buffer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var size = Marshal.SizeOf<NativeMethods.LocalGroupUsersInfo0>();

            for (var index = 0; index < entriesRead; index++)
            {
                var info = Marshal.PtrToStructure<NativeMethods.LocalGroupUsersInfo0>(buffer + (index * size));
                if (string.Equals(info.GroupName, "Administrators", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        finally
        {
            NativeMethods.NetApiBufferFree(buffer);
        }

        return false;
    }

    public static bool PasswordNeverExpires(string account)
    {
        var status = NativeMethods.NetUserGetInfo(null, UserPart(account), 1, out var buffer);

        if (status != NativeMethods.NerrSuccess || buffer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var info = Marshal.PtrToStructure<NativeMethods.UserInfo1>(buffer);
            return (info.Flags & NativeMethods.UfDontExpirePasswd) != 0;
        }
        finally
        {
            NativeMethods.NetApiBufferFree(buffer);
        }
    }

    /// <summary>禁止该账号交互式登录（只对新建的服务专用账号使用，切勿用在人的账号上）。</summary>
    public static void DenyInteractiveLogon(string account)
        => GrantRights(account, "SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight");

    public static bool Exists(string account)
    {
        var status = NativeMethods.NetUserGetInfo(null, UserPart(account), 1, out var buffer);
        if (buffer != IntPtr.Zero)
        {
            NativeMethods.NetApiBufferFree(buffer);
        }

        return status == NativeMethods.NerrSuccess;
    }

    /// <summary>不存在则创建（密码永不过期、不可改密），已存在则重置密码。</summary>
    public static void EnsureAccount(string account, string password)
    {
        if (Exists(account))
        {
            SetPassword(account, password);
            return;
        }

        var info = new NativeMethods.UserInfo1
        {
            Name = account,
            Password = password,
            Privilege = NativeMethods.UserPrivUser,
            Comment = "USB 盘符自动管理服务专用账号",
            HomeDir = "",
            ScriptPath = "",
            Flags = NativeMethods.UfScript | NativeMethods.UfDontExpirePasswd | NativeMethods.UfPasswdCantChange,
        };

        var status = NativeMethods.NetUserAdd(null, 1, ref info, out var parameterError);
        if (status != NativeMethods.NerrSuccess)
        {
            throw new Win32Exception(status, $"创建本地账号失败（NetUserAdd 返回 {status}，参数 {parameterError}）");
        }
    }

    public static void SetPassword(string account, string password)
    {
        var info = new NativeMethods.UserInfo1003 { Password = password };
        var status = NativeMethods.NetUserSetInfo(null, account, 1003, ref info, out var parameterError);
        if (status != NativeMethods.NerrSuccess)
        {
            throw new Win32Exception(status, $"设置账号密码失败（NetUserSetInfo 返回 {status}，参数 {parameterError}）");
        }
    }

    public static void EnsureAdministrators(string account)
    {
        var member = new NativeMethods.LocalGroupMembersInfo3 { DomainAndName = DomainAccount(account) };
        var status = NativeMethods.NetLocalGroupAddMembers(null, "Administrators", 3, ref member, 1);

        if (status == ErrorMemberInAlias)
        {
            return;
        }

        if (status != NativeMethods.NerrSuccess)
        {
            throw new Win32Exception(status, $"把账号加入 Administrators 失败（返回 {status}）");
        }
    }

    public static void RemoveFromAdministrators(string account)
    {
        var member = new NativeMethods.LocalGroupMembersInfo3 { DomainAndName = DomainAccount(account) };
        NativeMethods.NetLocalGroupDelMembers(null, "Administrators", 3, ref member, 1);
    }

    /// <summary>授予“作为服务登录”（SeServiceLogonRight），重复授予无副作用。</summary>
    public static void GrantServiceLogonRight(string account)
        => GrantRights(account, "SeServiceLogonRight");

    /// <summary>给账号授予一个或多个本地安全特权；重复授予无副作用。</summary>
    public static void GrantRights(string account, params string[] rightNames)
    {
        var sid = (SecurityIdentifier)new NTAccount(DomainAccount(account)).Translate(typeof(SecurityIdentifier));
        var sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);

        var rights = new NativeMethods.LsaUnicodeString[rightNames.Length];
        var buffers = new IntPtr[rightNames.Length];

        try
        {
            for (var index = 0; index < rightNames.Length; index++)
            {
                var name = rightNames[index];
                buffers[index] = Marshal.StringToHGlobalUni(name);
                rights[index].Buffer = buffers[index];
                rights[index].Length = (ushort)(name.Length * 2);
                rights[index].MaximumLength = (ushort)((name.Length + 1) * 2);
            }

            var attributes = new NativeMethods.LsaObjectAttributes
            {
                Length = Marshal.SizeOf<NativeMethods.LsaObjectAttributes>(),
            };

            var status = NativeMethods.LsaOpenPolicy(IntPtr.Zero, ref attributes, NativeMethods.PolicyAllAccess, out var policy);
            if (status != 0)
            {
                throw new Win32Exception((int)NativeMethods.LsaNtStatusToWinError(status), "打开本地安全策略失败");
            }

            try
            {
                status = NativeMethods.LsaAddAccountRights(policy, sidBytes, rights, (uint)rights.Length);
                if (status != 0)
                {
                    throw new Win32Exception((int)NativeMethods.LsaNtStatusToWinError(status), "授予安全特权失败");
                }
            }
            finally
            {
                NativeMethods.LsaClose(policy);
            }
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }

    /// <summary>给数据目录加服务账号的 Modify 权限（ProgramData 由安装者创建）。</summary>
    public static void EnsureDataDirectoryAcl(string account)
    {
        var directory = AppPaths.DataDirectory;
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "logs"));

        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(
            DomainAccount(account),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

        security.AddAccessRule(rule);
        info.SetAccessControl(security);
    }
}
