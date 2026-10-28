namespace UsbDriveManager.Core;

/// <summary>
/// 判断一块磁盘是否通过 USB 挂接。
/// 不能只看 <c>InterfaceType</c>：UASP/SCSI 桥接芯片的移动硬盘会把自己报成
/// <c>SCSI</c>（实例名 <c>SCSI\DISK&amp;...</c>），只有沿 PnP 设备树向上找到
/// <c>USB\VID_...</c> 祖先才能确认。
/// </summary>
public static class UsbAttachment
{
    private const int MaxParentDepth = 6;

    public static bool IsUsbAttached(
        string? pnpDeviceId,
        string? interfaceType,
        Func<string, string?>? parentLookup = null)
    {
        if (string.Equals(interfaceType?.Trim(), "USB", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return false;
        }

        if (pnpDeviceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lookup = parentLookup ?? Native.CfgMgr32.TryGetParentDeviceId;
        var current = pnpDeviceId;

        for (var depth = 0; depth < MaxParentDepth; depth++)
        {
            var parent = lookup(current!);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return false;
            }

            if (parent.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
                || parent.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }
}
