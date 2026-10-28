namespace UsbDriveManager.Core.Wmi;

/// <summary>当前系统上已被占用的盘符（来自 Win32_LogicalDisk）。</summary>
public static class DriveLetterMap
{
    public static HashSet<char> GetUsedLetters()
    {
        var used = new HashSet<char>();

        foreach (var logical in Wmi.Query("SELECT DeviceID FROM Win32_LogicalDisk"))
        {
            using (logical)
            {
                var deviceId = logical.Text("DeviceID");
                if (deviceId.Length >= 2 && deviceId[1] == ':' && char.IsLetter(deviceId[0]))
                {
                    used.Add(char.ToUpperInvariant(deviceId[0]));
                }
            }
        }

        return used;
    }
}
