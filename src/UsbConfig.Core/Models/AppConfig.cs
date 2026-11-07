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

    /// <summary>
    /// 先用完整指纹（VID/PID + 序列号）精确匹配；未命中时再做一次「稳定序列号」回退。
    /// 换 USB 口后 PnP 父设备树可能漂移，VID/PID 会解析到不同祖先，但设备固件上报的
    /// 序列号与端口无关。回退要求两侧序列号都稳定、且该序列号在启用规则里唯一，
    /// 避免桥接芯片的固定占位串把不同设备认成同一台。
    /// </summary>
    public DriveRule? FindMatch(DeviceIdentity device)
    {
        if (device is null)
        {
            return null;
        }

        var exact = EnabledRules.FirstOrDefault(r => r.Matches(device));
        if (exact is not null)
        {
            return exact;
        }

        if (!device.HasUsableSerial || !SerialRules.IsStable(device.Serial))
        {
            return null;
        }

        var candidates = EnabledRules
            .Where(r => r.UsesSerial
                        && SerialRules.IsStable(r.Serial)
                        && string.Equals(SerialRules.Normalize(r.Serial), device.Serial,
                            StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }
}
