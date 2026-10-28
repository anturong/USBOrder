using System.Runtime.InteropServices;

namespace UsbDriveManager.Editor.Native;

/// <summary>服务安装/账号管理所需的最小 P/Invoke 集合（避免 sc.exe 的转义问题）。</summary>
internal static class NativeMethods
{
    // ---------------- 常量 ----------------

    public const uint ScManagerAllAccess = 0x000F003F;
    public const uint ServiceAllAccess = 0x000F01FF;
    public const uint ServiceWin32OwnProcess = 0x00000010;
    public const uint ServiceAutoStart = 0x00000002;
    public const uint ServiceErrorNormal = 0x00000001;
    public const uint ServiceConfigDescription = 1;
    public const uint ServiceConfigFailureActions = 2;
    public const uint ServiceConfigDelayedAutoStart = 3;
    public const uint ScActionRestart = 1;

    public const int NerrSuccess = 0;
    public const int NerrUserExists = 2224;
    public const int NerrUserNotFound = 2221;
    public const int NerrGroupNotFound = 2220;
    public const int NerrMemberNotFound = 2221;

    public const int UserPrivUser = 1;
    public const int UfScript = 0x0001;
    public const int UfPasswdCantChange = 0x0040;
    public const int UfDontExpirePasswd = 0x10000;

    public const uint ErrorServiceExists = 1073;
    public const uint ErrorServiceDoesNotExist = 1060;
    public const uint ErrorAccessDenied = 5;

    // ---------------- netapi32 ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct UserInfo1
    {
        public string Name;
        public string Password;
        public int PasswordAge;
        public int Privilege;
        public string HomeDir;
        public string Comment;
        public int Flags;
        public string ScriptPath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct UserInfo1003
    {
        public string Password;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LocalGroupMembersInfo3
    {
        public string DomainAndName;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserAdd(string? serverName, int level, ref UserInfo1 buffer, out int parameterError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserSetInfo(string? serverName, string userName, int level, ref UserInfo1003 buffer, out int parameterError);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserDel(string? serverName, string userName);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserGetInfo(string? serverName, string userName, int level, out IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetLocalGroupAddMembers(
        string? serverName,
        string groupName,
        int level,
        ref LocalGroupMembersInfo3 buffer,
        int totalEntries);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetLocalGroupDelMembers(
        string? serverName,
        string groupName,
        int level,
        ref LocalGroupMembersInfo3 buffer,
        int totalEntries);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LocalGroupUsersInfo0
    {
        public string GroupName;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetUserGetLocalGroups(
        string? serverName,
        string userName,
        int level,
        int flags,
        out IntPtr buffer,
        int maxPreferredLength,
        out int entriesRead,
        out int totalEntries);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LogonUser(
        string userName,
        string? domain,
        string password,
        int logonType,
        int logonProvider,
        out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    public const int Logon32LogonInteractive = 2;
    public const int Logon32ProviderDefault = 0;
    public const int LgIncludeIndirect = 1;

    // ---------------- LSA（授予“作为服务登录”） ----------------

    [StructLayout(LayoutKind.Sequential)]
    public struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LsaObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    public const uint PolicyAllAccess = 0x000F0FFF;

    [DllImport("advapi32.dll")]
    public static extern uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes attributes, uint accessMask, out IntPtr policyHandle);

    [DllImport("advapi32.dll")]
    public static extern uint LsaAddAccountRights(IntPtr policyHandle, byte[] accountSid, LsaUnicodeString[] userRights, uint countOfRights);

    [DllImport("advapi32.dll")]
    public static extern uint LsaClose(IntPtr objectHandle);

    [DllImport("advapi32.dll")]
    public static extern uint LsaNtStatusToWinError(uint status);

    // ---------------- SCM ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ServiceDescription
    {
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceDelayedAutoStartInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DelayedAutostart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScAction
    {
        public uint Type;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ServiceFailureActions
    {
        public uint ResetPeriod;
        public string? RebootMessage;
        public string? Command;
        public uint ActionsCount;
        public IntPtr Actions;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateService(
        IntPtr scManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref ServiceDescription info);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref ServiceDelayedAutoStartInfo info);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref ServiceFailureActions info);
}
