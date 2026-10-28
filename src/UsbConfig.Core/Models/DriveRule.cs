using System.Text.Json.Serialization;

namespace UsbDriveManager.Core.Models;

/// <summary>
/// 一条盘符分配规则：VID/PID 必填，序列号留空表示只按 VID/PID 匹配（精度较低，编辑器会提示）。
/// </summary>
public sealed class DriveRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>便于运维识别的名称，可为空。</summary>
    public string Name { get; set; } = "";

    /// <summary>4 位十六进制，大小写不敏感。</summary>
    public string Vid { get; set; } = "";

    /// <summary>4 位十六进制，大小写不敏感。</summary>
    public string Pid { get; set; } = "";

    /// <summary>设备序列号；空表示通配。</summary>
    public string Serial { get; set; } = "";

    /// <summary>目标盘符，D–Z。</summary>
    public char TargetLetter { get; set; } = 'F';

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public bool UsesSerial => !string.IsNullOrWhiteSpace(Serial);

    /// <summary>按 VID/PID（+ 可选序列号）判断设备是否命中本规则。</summary>
    public bool Matches(DeviceIdentity device)
    {
        if (device is null)
        {
            return false;
        }

        if (!string.Equals(Vid.Trim(), device.Vid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(Pid.Trim(), device.Pid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!UsesSerial)
        {
            return true;
        }

        return string.Equals(SerialRules.Normalize(Serial), device.Serial, StringComparison.OrdinalIgnoreCase);
    }

    public DriveRule Clone() => (DriveRule)MemberwiseClone();
}
