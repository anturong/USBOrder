using System.Runtime.InteropServices;
using System.Text;

namespace UsbDriveManager.Core.Native;

/// <summary>
/// Configuration Manager 只读封装：沿设备树向上找父节点，用于拿到 USB 设备的 VID/PID。
/// 只用 CM_* 的查询 API，不修改设备树。
/// </summary>
internal static class CfgMgr32
{
    private const int CR_SUCCESS = 0;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
    private const int MaxDeviceIdLength = 512;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = false)]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, StringBuilder buffer, int bufferLen, uint ulFlags);

    /// <summary>返回父节点实例名；失败返回 null。</summary>
    public static string? TryGetParentDeviceId(string deviceInstanceId)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceId))
        {
            return null;
        }

        try
        {
            if (CM_Locate_DevNodeW(out var node, deviceInstanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            {
                return null;
            }

            if (CM_Get_Parent(out var parent, node, 0) != CR_SUCCESS)
            {
                return null;
            }

            var buffer = new StringBuilder(MaxDeviceIdLength);
            if (CM_Get_Device_IDW(parent, buffer, buffer.Capacity, 0) != CR_SUCCESS)
            {
                return null;
            }

            var id = buffer.ToString();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
