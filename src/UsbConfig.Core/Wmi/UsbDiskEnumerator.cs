using System.Management;
using UsbDriveManager.Core.Models;

namespace UsbDriveManager.Core.Wmi;

public interface IUsbDiskEnumerator
{
    IReadOnlyList<UsbDiskInfo> Enumerate();
}

/// <summary>
/// 枚举通过 USB 挂接的磁盘及其分区/卷。
/// 判据不只看 InterfaceType：UASP/SCSI 桥接的移动硬盘会把自己报成 SCSI，
/// 必须沿 PnP 设备树找到 USB 祖先（见 <see cref="UsbAttachment"/>）。
/// 分区即便没有盘符也会被枚举出来（按容量匹配 Win32_Volume），以便后续分配。
/// </summary>
public sealed class WmiUsbDiskEnumerator : IUsbDiskEnumerator
{
    public IReadOnlyList<UsbDiskInfo> Enumerate()
    {
        var disks = new List<UsbDiskInfo>();
        var accessPaths = LoadPartitionAccessPaths();

        using var searcher = new ManagementObjectSearcher(
            Wmi.Scope,
            "SELECT DeviceID, Model, PNPDeviceID, SerialNumber, Size, InterfaceType, MediaType FROM Win32_DiskDrive");

        foreach (ManagementObject disk in searcher.Get())
        {
            using (disk)
            {
                var deviceId = disk.Text("DeviceID");
                var pnpDeviceId = disk.Text("PNPDeviceID");

                if (!UsbAttachment.IsUsbAttached(pnpDeviceId, disk.Text("InterfaceType")))
                {
                    continue;
                }

                var identity = DeviceIdentityResolver.Resolve(pnpDeviceId, disk.Text("SerialNumber"));
                var partitions = new List<UsbPartition>();

                foreach (ManagementObject partition in Wmi.Associated("Win32_DiskDrive", deviceId, "Win32_DiskDriveToDiskPartition"))
                {
                    using (partition)
                    {
                        var partitionId = partition.Text("DeviceID");
                        var partitionSize = partition.Number("Size");
                        var diskIndex = partition.Int32("DiskIndex");
                        var partitionIndex = partition.Int32("Index");

                        char? letter = null;
                        var label = "";
                        var fileSystem = "";

                        var logical = Wmi.Associated("Win32_DiskPartition", partitionId, "Win32_LogicalDiskToPartition").FirstOrDefault();
                        if (logical is not null)
                        {
                            using (logical)
                            {
                                letter = ParseLetter(logical.Text("DeviceID"));
                                label = logical.Text("VolumeName");
                                fileSystem = logical.Text("FileSystem");
                            }
                        }

                        var volumeId = "";
                        if (letter.HasValue)
                        {
                            volumeId = FindVolumeId(letter.Value);
                        }

                        if (volumeId.Length == 0
                            && accessPaths.TryGetValue((diskIndex, partitionIndex + 1), out var accessPath))
                        {
                            volumeId = accessPath;
                        }

                        if (volumeId.Length == 0)
                        {
                            volumeId = FindVolumeIdBySize(partitionSize);
                        }

                        partitions.Add(new UsbPartition
                        {
                            Letter = letter,
                            VolumeId = volumeId,
                            Label = label,
                            SizeBytes = partitionSize,
                            FileSystem = fileSystem,
                        });
                    }
                }

                disks.Add(new UsbDiskInfo
                {
                    DeviceId = deviceId,
                    PnpDeviceId = pnpDeviceId,
                    Model = disk.Text("Model"),
                    SizeBytes = disk.Number("Size"),
                    Identity = identity,
                    Partitions = partitions,
                });
            }
        }

        return disks;
    }

    private static char? ParseLetter(string deviceId)
    {
        if (deviceId.Length >= 2 && deviceId[1] == ':' && char.IsLetter(deviceId[0]))
        {
            return char.ToUpperInvariant(deviceId[0]);
        }

        return null;
    }

    /// <summary>
    /// 从存储命名空间的 MSFT_Partition 拿每个分区对应的卷 GUID。
    /// 这是无盘符分区唯一可靠的定位方式——Win32_Volume.Capacity 与分区 Size 会有几 KB 差异，按容量匹配不可靠。
    /// </summary>
    private static Dictionary<(int Disk, int Partition), string> LoadPartitionAccessPaths()
    {
        var map = new Dictionary<(int Disk, int Partition), string>();

        try
        {
            foreach (var partition in Wmi.QueryInScope(
                         @"root\Microsoft\Windows\Storage",
                         "SELECT DiskNumber, PartitionNumber, AccessPaths FROM MSFT_Partition"))
            {
                using (partition)
                {
                    var diskNumber = partition.Int32("DiskNumber");
                    var partitionNumber = partition.Int32("PartitionNumber");

                    if (partition["AccessPaths"] is string[] paths)
                    {
                        // AccessPaths 里可能同时有 "U:\" 和 "\\?\Volume{guid}\"；
                        // 只有后者能喂给 Win32_Volume.DeviceID。
                        var volumePath = paths.FirstOrDefault(
                            p => p.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(volumePath))
                        {
                            map[(diskNumber, partitionNumber)] = volumePath;
                        }
                    }
                }
            }
        }
        catch
        {
            // 存储命名空间不可用时退回按盘符/容量匹配。
        }

        return map;
    }

    private static string FindVolumeId(char letter)
    {
        var volume = Wmi.Single($"SELECT DeviceID FROM Win32_Volume WHERE DriveLetter='{letter}:'");
        if (volume is null)
        {
            return "";
        }

        using (volume)
        {
            return volume.Text("DeviceID");
        }
    }

    private static string FindVolumeIdBySize(ulong size)
    {
        if (size == 0)
        {
            return "";
        }

        var volume = Wmi.Single($"SELECT DeviceID FROM Win32_Volume WHERE DriveLetter IS NULL AND Capacity={size}");
        if (volume is null)
        {
            return "";
        }

        using (volume)
        {
            return volume.Text("DeviceID");
        }
    }
}
