using System.Text.RegularExpressions;

namespace UsbDriveManager.Core;

/// <summary>
/// 设备指纹：VID/PID + 序列号。序列号可能为空或不可靠（杂牌盘常见全零），
/// 因此单独用 <see cref="HasUsableSerial"/> 暴露给编辑器做精度提示。
/// </summary>
public sealed class DeviceIdentity
{
    public static DeviceIdentity Empty { get; } = new();

    public string Vid { get; init; } = "";

    public string Pid { get; init; } = "";

    public string Serial { get; init; } = "";

    /// <summary>来自 USBSTOR 实例名 VEN_ 段的厂商串，仅用于显示。</summary>
    public string Vendor { get; init; } = "";

    /// <summary>来自 USBSTOR 实例名 PROD_ 段的产品串，仅用于显示。</summary>
    public string Product { get; init; } = "";

    public bool HasVidPid => Vid.Length == 4 && Pid.Length == 4;

    public bool HasUsableSerial => SerialRules.IsUsable(Serial);

    public string DisplayId => HasVidPid ? $"{Vid}:{Pid}" : "未知";

    public override string ToString()
        => $"VID_{Vid}&PID_{Pid}" + (HasUsableSerial ? $" SN={Serial}" : " SN=<无>");
}

/// <summary>序列号规范化与可用性判定。</summary>
public static class SerialRules
{
    /// <summary>
    /// 去掉首尾空白与 USBSTOR 实例名尾部的 “&amp;0”（同一个盘在不同机器上可能带或不带）。
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var value = raw.Trim();

        if (value.EndsWith("&0", StringComparison.Ordinal))
        {
            value = value[..^2].Trim();
        }

        return value;
    }

    /// <summary>
    /// 空串、纯零、Windows 生成的总线相对实例号（如 <c>6&amp;29B12556</c>）、
    /// 以及明显是固件垃圾的值（如这个读卡器上报的 <c>[</c>）都视为不可用。
    /// </summary>
    public static bool IsUsable(string? raw)
    {
        var value = Normalize(raw);
        if (value.Length < 4)
        {
            return false;
        }

        // 含 & 的是 PnP 实例名片段（6&29B12556），不是设备上报的序列号。
        if (value.Contains('&'))
        {
            return false;
        }

        var hasAlphanumeric = false;

        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                hasAlphanumeric = true;
                continue;
            }

            if (ch is ' ' or '-' or '_' or '.')
            {
                continue;
            }

            // 出现标点/括号等垃圾字符，判定为不可靠。
            return false;
        }

        return hasAlphanumeric;
    }
}

/// <summary>从 PnP 设备实例名里解析 VID/PID、厂商与序列号。</summary>
public static partial class PnpIdParser
{
    [GeneratedRegex(@"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex VidPidRegex();

    /// <summary>
    /// 解析 USBSTOR 实例名，例如：
    /// <c>USBSTOR\DISK&amp;VEN_SANDISK&amp;PROD_CRUZER&amp;REV_1.00\4C530001234567890123&amp;0</c>。
    /// </summary>
    public static (string Vendor, string Product, string Serial) FromStorageInstanceId(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return ("", "", "");
        }

        var segments = instanceId.Trim().Split('\\');
        var serial = segments.Length >= 2 ? SerialRules.Normalize(segments[^1]) : "";

        var vendor = "";
        var product = "";

        if (segments.Length >= 2)
        {
            foreach (var token in segments[1].Split('&'))
            {
                if (token.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase))
                {
                    vendor = token[4..];
                }
                else if (token.StartsWith("PROD_", StringComparison.OrdinalIgnoreCase))
                {
                    product = token[5..];
                }
            }
        }

        return (vendor, product, serial);
    }

    /// <summary>从 <c>USB\VID_0781&amp;PID_5581\...</c> 这类实例名解析 VID/PID。</summary>
    public static bool TryParseVidPid(string? deviceId, out string vid, out string pid)
    {
        vid = "";
        pid = "";

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var match = VidPidRegex().Match(deviceId);
        if (!match.Success)
        {
            return false;
        }

        vid = match.Groups[1].Value.ToUpperInvariant();
        pid = match.Groups[2].Value.ToUpperInvariant();
        return true;
    }

    /// <summary>取实例名最后一段（通常是序列号）。</summary>
    public static string Tail(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return "";
        }

        var segments = deviceId.Trim().Split('\\');
        return segments.Length == 0 ? "" : SerialRules.Normalize(segments[^1]);
    }
}

/// <summary>
/// 把磁盘的 PnP 实例名解析成设备指纹。VID/PID 在父级 USB 设备节点上，
/// 因此默认通过 Configuration Manager 向上走设备树（最多 4 层）查找。
/// </summary>
public static class DeviceIdentityResolver
{
    private const int MaxParentDepth = 4;

    public static DeviceIdentity Resolve(
        string? pnpDeviceId,
        string? wmiSerialNumber,
        Func<string, string?>? parentLookup = null)
    {
        var (vendor, product, storageSerial) = PnpIdParser.FromStorageInstanceId(pnpDeviceId);

        var serial = SerialRules.Normalize(wmiSerialNumber);
        if (!SerialRules.IsUsable(serial))
        {
            serial = storageSerial;
        }

        var vid = "";
        var pid = "";

        if (!PnpIdParser.TryParseVidPid(pnpDeviceId, out vid, out pid))
        {
            var lookup = parentLookup ?? Native.CfgMgr32.TryGetParentDeviceId;
            var current = pnpDeviceId;

            for (var depth = 0; depth < MaxParentDepth && !string.IsNullOrWhiteSpace(current); depth++)
            {
                var parent = lookup(current!);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    break;
                }

                if (PnpIdParser.TryParseVidPid(parent, out vid, out pid))
                {
                    if (!SerialRules.IsUsable(serial))
                    {
                        serial = PnpIdParser.Tail(parent);
                    }

                    break;
                }

                current = parent;
            }
        }

        return new DeviceIdentity
        {
            Vid = vid,
            Pid = pid,
            // 不可靠的序列号直接清空：宁可退化为 VID/PID 匹配，也不要让垃圾值参与精确匹配。
            Serial = SerialRules.IsUsable(serial) ? serial : "",
            Vendor = vendor,
            Product = product,
        };
    }
}
