using System.Management;

namespace UsbDriveManager.Core.Wmi;

/// <summary>WMI 查询小工具：转义、单值查询、关联查询。</summary>
internal static class Wmi
{
    public const string Scope = @"root\CIMV2";

    public static IReadOnlyList<ManagementObject> Query(string wql) => QueryInScope(Scope, wql);

    /// <summary>在指定命名空间执行查询（如 root\Microsoft\Windows\Storage 的 MSFT_Partition）。</summary>
    public static IReadOnlyList<ManagementObject> QueryInScope(string scope, string wql)
    {
        var searcher = new ManagementObjectSearcher(scope, wql);
        var results = new List<ManagementObject>();

        foreach (ManagementObject item in searcher.Get())
        {
            results.Add(item);
        }

        return results;
    }

    public static ManagementObject? Single(string wql)
        => Query(wql).FirstOrDefault();

    /// <summary>
    /// ASSOCIATORS OF 查询。WQL 里反斜杠要成对写，且实例路径必须用双引号包起来
    /// （单引号 + 转义的反斜杠会被当成字面量而报 “Not found”）。
    /// </summary>
    public static IReadOnlyList<ManagementObject> Associated(string className, string deviceId, string associationClass)
    {
        var escaped = Escape(deviceId);
        var wql = $"ASSOCIATORS OF {{{className}.DeviceID=\"{escaped}\"}} WHERE AssocClass={associationClass}";
        return Query(wql);
    }

    public static string Escape(string? value)
        => (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public static string Text(this ManagementBaseObject obj, string property)
        => obj[property]?.ToString()?.Trim() ?? "";

    public static ulong Number(this ManagementBaseObject obj, string property)
    {
        var value = obj[property];
        if (value is null)
        {
            return 0;
        }

        return ulong.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    public static int Int32(this ManagementBaseObject obj, string property)
    {
        var value = obj[property];
        if (value is null)
        {
            return 0;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }
}
