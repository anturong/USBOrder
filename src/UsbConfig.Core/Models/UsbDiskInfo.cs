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
    public char? Letter { get; init; }

    public string VolumeId { get; init; } = "";

    public string Label { get; init; } = "";

    public ulong SizeBytes { get; init; }

    public string FileSystem { get; init; } = "";

    public override string ToString()
        => $"{(Letter.HasValue ? $"{Letter}:" : "<无盘符>")} {Label} {FileSystem} {DriveLetters.FormatSize(SizeBytes)}".Trim();
}
