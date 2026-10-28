namespace UsbDriveManager.Core.Models;

/// <summary>配置文件根对象（config.json）。</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public List<DriveRule> Rules { get; set; } = new();

    public static AppConfig CreateDefault() => new();

    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<DriveRule> EnabledRules => Rules.Where(r => r.Enabled);

    public DriveRule? FindMatch(DeviceIdentity device)
        => EnabledRules.FirstOrDefault(r => r.Matches(device));
}
