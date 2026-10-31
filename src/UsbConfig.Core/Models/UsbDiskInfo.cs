namespace UsbDriveManager.Core.Models;

/// <summary>一块当前已插入的 USB 磁盘（可能包含多个分区）。</summary>
public sealed class UsbDiskInfo
{
    public string DeviceId { get; init; } = "";

    public string PnpDeviceId { get; init; } = "";

    public string Model { get; init; } = "";

    public ulong SizeBytes { get; init; }

    public DeviceIdentity Identity { get; init; } = DeviceIdentity.Empty;

    public IReadOnlyList<UsbPartition> Partitions { get; init; } = Array.Empty<UsbPartition>();

    public IEnumerable<char> Letters => Partitions.Where(p => p.Letter.HasValue).Select(p => p.Letter!.Value);

    public UsbPartition? PrimaryPartition => Partitions.FirstOrDefault(p => p.Letter.HasValue) ?? Partitions.FirstOrDefault();

    public bool IsAssignedTo(char letter) => Letters.Contains(char.ToUpperInvariant(letter));

    public override string ToString()
    {
        var letters = string.Join(",", Letters.Select(l => $"{l}:"));
        return $"{Model} [{Identity.DisplayId}] {(letters.Length == 0 ? "<无盘符>" : letters)}";
    }
}

/// <summary>USB 磁盘上的一个分区及其当前盘符。</summary>
public sealed class UsbPartition
{
    /// <summary>EFI 系统分区（ESP）的 GPT 分区类型 GUID。</summary>
    public static readonly Guid EfiSystemPartitionGuid = new("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");

    public char? Letter { get; init; }

    public string VolumeId { get; init; } = "";

    public string Label { get; init; } = "";

    public ulong SizeBytes { get; init; }

    public string FileSystem { get; init; } = "";

    /// <summary>GPT 分区类型 GUID（从 MSFT_Partition.GptType 读取；MBR 磁盘或读取失败时为空）。</summary>
    public Guid GptType { get; init; }

    /// <summary>是否为 EFI/ESP 系统分区。</summary>
    public bool IsEfi => GptType == EfiSystemPartitionGuid;

    /// <summary>
    /// 是否为「无法识别」分区：Windows 读不出文件系统（FileSystem 为空）。
    /// 典型情况：Linux/未知格式分区被 Windows 挂了盘符，打开时报「需要格式化」；
    /// 或根本没有文件系统的幽灵分区。正常分区（NTFS/FAT/exFAT）FileSystem 一定有值。
    /// 光驱不在 USB 磁盘枚举范围内，不会误伤。
    /// </summary>
    public bool IsUnrecognized => string.IsNullOrWhiteSpace(FileSystem);

    /// <summary>是否属于应当跳过的分区（EFI/ESP 或无法识别）。</summary>
    public bool ShouldSkip => IsEfi || IsUnrecognized;

    public override string ToString()
        => $"{(Letter.HasValue ? $"{Letter}:" : "<无盘符>")} {Label} {FileSystem} {DriveLetters.FormatSize(SizeBytes)}".Trim();
}
