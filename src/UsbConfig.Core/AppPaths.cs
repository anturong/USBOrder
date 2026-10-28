namespace UsbDriveManager.Core;

/// <summary>
/// 固定路径约定：配置与日志统一放在 ProgramData\UsbDriveManager。
/// 环境变量 <c>USBDRIVEMANAGER_DATA</c> 可覆盖根目录（测试/绿色部署用）。
/// </summary>
public static class AppPaths
{
    public const string DataDirEnvironmentVariable = "USBDRIVEMANAGER_DATA";
    public const string DefaultDataFolderName = "UsbDriveManager";

    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return Path.GetFullPath(overridden.Trim());
            }

            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, DefaultDataFolderName);
        }
    }

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

    public static void EnsureLogDirectory() => Directory.CreateDirectory(LogDirectory);
}
