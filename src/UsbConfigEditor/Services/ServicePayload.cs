using System.ComponentModel;
using System.Diagnostics;

namespace UsbDriveManager.Editor.Services;

/// <summary>把服务程序文件复制到 Program Files\UsbDriveManager 后再注册。</summary>
internal static class ServicePayload
{
    /// <summary>优先用编辑器同目录下的 service 子目录，其次用编辑器所在目录。</summary>
    public static string ResolveSourceDirectory()
    {
        var sibling = Path.Combine(AppContext.BaseDirectory, "service");
        if (File.Exists(Path.Combine(sibling, "UsbDriveService.exe")))
        {
            return sibling;
        }

        return AppContext.BaseDirectory;
    }

    public static string PrepareInstallFiles()
    {
        var source = ResolveSourceDirectory();
        var sourceExe = Path.Combine(source, "UsbDriveService.exe");

        if (!File.Exists(sourceExe))
        {
            throw new FileNotFoundException(
                $"未找到服务程序：{sourceExe}\n把 UsbDriveService.exe 放在编辑器的 service 子目录，或与编辑器同目录。");
        }

        var target = ServiceManager.InstallDirectory;
        Directory.CreateDirectory(target);

        // 编辑器就是从目标目录的上一级运行时，source 和 target 可能重合，直接跳过复制。
        if (string.Equals(
                Path.GetFullPath(source).TrimEnd('\\'),
                Path.GetFullPath(target).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(target, "UsbDriveService.exe");
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                File.Copy(file, destination, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new IOException($"复制文件失败：{destination}\n（{ex.Message}）", ex);
            }
        }

        return Path.Combine(target, "UsbDriveService.exe");
    }
}
