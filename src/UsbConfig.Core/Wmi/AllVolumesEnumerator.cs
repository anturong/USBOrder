using System.Management;

namespace UsbDriveManager.Core.Wmi;

/// <summary>系统上一个已挂载的卷（内置硬盘 / U 盘 / 移动硬盘 / 网络盘 / 光驱等）。</summary>
public sealed class VolumeInfo
{
    /// <summary>盘符，如 'C'。</summary>
    public char Letter { get; init; }

    /// <summary>Win32_LogicalDisk.DriveType：2 可移动、3 本地、4 网络、5 光驱。</summary>
    public int DriveType { get; init; }

    public string Label { get; init; } = "";

    public ulong SizeBytes { get; init; }

    public ulong FreeBytes { get; init; }

    /// <summary>占用卷所在磁盘是否为 USB 磁盘（U 盘 / 移动硬盘）。</summary>
    public bool IsUsb { get; init; }

    public string DiskModel { get; init; } = "";

    public string FileSystem { get; init; } = "";
}

/// <summary>
/// 枚举系统上所有已挂载的逻辑卷（不限于 USB），供概览页展示全部盘符、
/// 名称与空间占用。判 USB 与 <see cref="UsbAttachment"/> 一致：沿 PnP 树向上找 USB 祖先。
/// </summary>
public sealed class WmiAllVolumesEnumerator
{
    public IReadOnlyList<VolumeInfo> Enumerate()
    {
        var result = new List<VolumeInfo>();

        foreach (var logical in Wmi.Query(
                     "SELECT DeviceID, DriveType, VolumeName, Size, FreeSpace, FileSystem FROM Win32_LogicalDisk"))
        {
            using (logical)
            {
                var deviceId = logical.Text("DeviceID");
                if (deviceId.Length < 2 || deviceId[1] != ':' || !char.IsLetter(deviceId[0]))
                {
                    continue;
                }

                var letter = char.ToUpperInvariant(deviceId[0]);
                var driveType = logical.Int32("DriveType");

                var diskModel = "";
                var isUsb = false;

                // 只有本地磁盘(3)和可移动磁盘(2)才需要反查底层磁盘型号与 USB 归属。
                if (driveType is 2 or 3)
                {
                    foreach (var partition in Wmi.Associated(
                                 "Win32_LogicalDisk", $"{letter}:", "Win32_LogicalDiskToPartition"))
                    {
                        using (partition)
                        {
                            foreach (var disk in Wmi.Associated(
                                         "Win32_DiskPartition", partition.Text("DeviceID"),
                                         "Win32_DiskDriveToDiskPartition"))
                            {
                                using (disk)
                                {
                                    diskModel = disk.Text("Model");
                                    isUsb = UsbAttachment.IsUsbAttached(
                                        disk.Text("PNPDeviceID"), disk.Text("InterfaceType"));
                                    break;
                                }
                            }
                        }

                        if (isUsb)
                        {
                            break;
                        }
                    }
                }

                result.Add(new VolumeInfo
                {
                    Letter = letter,
                    DriveType = driveType,
                    Label = logical.Text("VolumeName"),
                    SizeBytes = logical.Number("Size"),
                    FreeBytes = logical.Number("FreeSpace"),
                    IsUsb = isUsb,
                    DiskModel = diskModel,
                    FileSystem = logical.Text("FileSystem"),
                });
            }
        }

        result.Sort((a, b) => a.Letter.CompareTo(b.Letter));
        return result;
    }
}
