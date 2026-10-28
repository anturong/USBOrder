namespace UsbDriveManager.Core.Models;

/// <summary>目标盘符当前占用者。</summary>
public sealed class VolumeOccupant
{
    public char Letter { get; init; }

    public bool Exists { get; init; }

    /// <summary>Win32_LogicalDisk.DriveType：2 可移动、3 本地、4 网络、5 光驱。</summary>
    public int DriveType { get; init; }

    public string VolumeLabel { get; init; } = "";

    public ulong SizeBytes { get; init; }

    /// <summary>占用卷所在磁盘是否为 USB 磁盘。</summary>
    public bool IsUsbDisk { get; init; }

    public string DiskModel { get; init; } = "";

    public string DiskPnpDeviceId { get; init; } = "";

    public DeviceIdentity? Identity { get; init; }

    public string Description { get; init; } = "";

    public static VolumeOccupant Free(char letter)
        => new() { Letter = letter, Exists = false, Description = "空闲" };

    public string DriveTypeText => DriveType switch
    {
        2 => "可移动磁盘",
        3 => "本地磁盘",
        4 => "网络驱动器",
        5 => "光驱",
        6 => "内存盘",
        _ => "未知卷",
    };
}
