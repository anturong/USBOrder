namespace UsbDriveManager.Core.Models;

/// <summary>配置文件根对象（config.json）。</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public List<DriveRule> Rules { get; set; } = new();

    /// <summary>分配/导入时跳过 EFI/ESP 系统分区（默认开启）。</summary>
    public bool HideEfiPartitions { get; set; } = true;

    /// <summary>分配/导入时跳过无法识别的分区（无文件系统或容量为 0，默认开启；光驱永远豁免）。</summary>
    public bool HideUnrecognizedPartitions { get; set; } = true;

    public static AppConfig CreateDefault() => new();

    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DriveRule> EnabledRules => Rules.Where(r => r.Enabled);

    public DriveRule? FindMatch(DeviceIdentity device)
        => EnabledRules.FirstOrDefault(r => r.Matches(device));
}
