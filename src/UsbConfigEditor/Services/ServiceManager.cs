using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using UsbDriveManager.Editor.Native;

namespace UsbDriveManager.Editor.Services;

/// <summary>服务的注册/删除/启停（注册与删除走 SCM P/Invoke，启停走 ServiceController）。</summary>
internal static class ServiceManager
{
    public const string ServiceName = "UsbDriveService";
    public const string DisplayName = "USB 盘符自动管理服务";

    public const string Description = "监听 U 盘/移动硬盘插拔，按规则分配固定盘符；冲突时拒绝并记录日志。";

    private const uint ServiceDelete = 0x00010000;

    public static string InstallDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UsbDriveManager");

    public static string InstalledExePath => Path.Combine(InstallDirectory, "UsbDriveService.exe");

    /// <summary>未安装返回 null。</summary>
    public static ServiceControllerStatus? GetStatus()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsInstalled => GetStatus() is not null;

    public static void Start()
    {
        using var controller = new ServiceController(ServiceName);
        if (controller.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public static void Stop()
    {
        if (!IsInstalled)
        {
            return;
        }

        using var controller = new ServiceController(ServiceName);
        if (controller.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    public static void Install(string executablePath, string serviceAccount, string password)
    {
        var scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerAllAccess);
        if (scManager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "打开服务管理器失败（需要管理员权限）");
        }

        try
        {
            DeleteExistingService(scManager);

            var service = NativeMethods.CreateService(
                scManager,
                ServiceName,
                DisplayName,
                NativeMethods.ServiceAllAccess,
                NativeMethods.ServiceWin32OwnProcess,
                NativeMethods.ServiceAutoStart,
                NativeMethods.ServiceErrorNormal,
                $"\"{executablePath}\"",
                null,
                IntPtr.Zero,
                null,
                serviceAccount,
                password);

            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"创建服务失败（错误 {error}）");
            }

            try
            {
                ApplyAdvancedConfiguration(service);
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scManager);
        }
    }

    public static void Uninstall()
    {
        Stop();

        var scManager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerAllAccess);
        if (scManager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "打开服务管理器失败（需要管理员权限）");
        }

        try
        {
            var service = NativeMethods.OpenService(scManager, ServiceName, ServiceDelete);
            if (service == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (!NativeMethods.DeleteService(service))
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, $"删除服务失败（错误 {error}）");
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scManager);
        }
    }

    private static void DeleteExistingService(IntPtr scManager)
    {
        var existing = NativeMethods.OpenService(scManager, ServiceName, ServiceDelete);
        if (existing == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.DeleteService(existing);
        }
        finally
        {
            NativeMethods.CloseServiceHandle(existing);
        }

        // 删除是延迟生效的，等同名服务彻底消失再创建，避免 1072。
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var probe = NativeMethods.OpenService(scManager, ServiceName, ServiceDelete);
            if (probe == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.CloseServiceHandle(probe);
            Thread.Sleep(300);
        }

        throw new InvalidOperationException("旧服务仍在“标记删除”状态，请重启系统后再试。");
    }

    private static void ApplyAdvancedConfiguration(IntPtr service)
    {
        var description = new NativeMethods.ServiceDescription { Description = Description };
        NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigDescription, ref description);

        var delayed = new NativeMethods.ServiceDelayedAutoStartInfo { DelayedAutostart = true };
        NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigDelayedAutoStart, ref delayed);

        var actions = new[]
        {
            new NativeMethods.ScAction { Type = NativeMethods.ScActionRestart, Delay = 60_000 },
            new NativeMethods.ScAction { Type = NativeMethods.ScActionRestart, Delay = 60_000 },
            new NativeMethods.ScAction { Type = NativeMethods.ScActionRestart, Delay = 120_000 },
        };

        var size = Marshal.SizeOf<NativeMethods.ScAction>();
        var buffer = Marshal.AllocHGlobal(size * actions.Length);

        try
        {
            for (var index = 0; index < actions.Length; index++)
            {
                Marshal.StructureToPtr(actions[index], buffer + (index * size), false);
            }

            var failureActions = new NativeMethods.ServiceFailureActions
            {
                ResetPeriod = 86_400,
                RebootMessage = null,
                Command = null,
                ActionsCount = (uint)actions.Length,
                Actions = buffer,
            };

            NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigFailureActions, ref failureActions);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
