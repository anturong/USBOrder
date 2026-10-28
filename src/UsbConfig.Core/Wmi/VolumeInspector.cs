using UsbDriveManager.Core.Models;

namespace UsbDriveManager.Core.Wmi;

public interface IVolumeInspector
{
    VolumeOccupant Inspect(char letter);
}

/// <summary>
/// 反查目标盘符的占用者：先用 Win32_LogicalDisk 判断有没有卷，
/// 再沿 LogicalDisk → Partition → DiskDrive 关联找出底层磁盘，判断它是不是 USB、属于哪台设备。
/// </summary>
public sealed class WmiVolumeInspector : IVolumeInspector
{
    public VolumeOccupant Inspect(char letter)
    {
        var upper = char.ToUpperInvariant(letter);
        var logical = Wmi.Single(
            $"SELECT DeviceID, DriveType, VolumeName, Size, FileSystem FROM Win32_LogicalDisk WHERE DeviceID='{upper}:'");

        if (logical is null)
        {
            return VolumeOccupant.Free(upper);
        }

        using (logical)
        {
            var driveType = logical.Int32("DriveType");
            var label = logical.Text("VolumeName");
            var size = logical.Number("Size");

            var diskModel = "";
            var diskPnp = "";
            var isUsb = false;
            DeviceIdentity? identity = null;

            if (driveType is 2 or 3)
            {
                foreach (var partition in Wmi.Associated("Win32_LogicalDisk", $"{upper}:", "Win32_LogicalDiskToPartition"))
                {
                    using (partition)
                    {
                        foreach (var disk in Wmi.Associated("Win32_DiskPartition", partition.Text("DeviceID"), "Win32_DiskDriveToDiskPartition"))
                        {
                            using (disk)
                            {
                                diskModel = disk.Text("Model");
                                diskPnp = disk.Text("PNPDeviceID");
                                isUsb = UsbAttachment.IsUsbAttached(diskPnp, disk.Text("InterfaceType"));

                                if (isUsb)
                                {
                                    identity = DeviceIdentityResolver.Resolve(diskPnp, disk.Text("SerialNumber"));
                                }

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

            var description = BuildDescription(upper, driveType, label, diskModel, isUsb);

            return new VolumeOccupant
            {
                Letter = upper,
                Exists = true,
                DriveType = driveType,
                VolumeLabel = label,
                SizeBytes = size,
                IsUsbDisk = isUsb,
                DiskModel = diskModel,
                DiskPnpDeviceId = diskPnp,
                Identity = identity,
                Description = description,
            };
        }
    }

    private static string BuildDescription(char letter, int driveType, string label, string model, bool isUsb)
    {
        var typeText = driveType switch
        {
            2 => isUsb ? "U 盘" : "可移动磁盘",
            3 => "本地磁盘",
            4 => "网络驱动器",
            5 => "光驱",
            6 => "内存盘",
            _ => "未知卷",
        };

        var parts = new List<string> { $"{letter}: {typeText}" };

        if (!string.IsNullOrWhiteSpace(model))
        {
            parts.Add(model);
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            parts.Add($"卷标“{label}”");
        }

        return string.Join(" · ", parts);
    }
}
