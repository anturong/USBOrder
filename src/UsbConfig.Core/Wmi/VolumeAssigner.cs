using System.Management;

namespace UsbDriveManager.Core.Wmi;

public sealed record LetterAssignResult(bool Success, string Message)
{
    public static LetterAssignResult Ok(string message) => new(true, message);

    public static LetterAssignResult Fail(string message) => new(false, message);
}

public interface ILetterAssigner
{
    /// <summary>把 <paramref name="currentLetter"/> 上的卷改派到 <paramref name="targetLetter"/>。</summary>
    LetterAssignResult Assign(char targetLetter, char? currentLetter);

    /// <summary>
    /// 按卷标识（<c>\\?\Volume{guid}\</c>）改派/分配盘符。
    /// 这是唯一能处理“当前没有盘符”的卷的路径。
    /// </summary>
    LetterAssignResult AssignVolume(string volumeId, char targetLetter);
}

/// <summary>
/// 用 Win32_Volume.Put() 改盘符；检查+赋值在同一把跨进程命名互斥锁内完成，
/// 避免服务与编辑器的现场操作互相踩踏。
/// </summary>
public sealed class WmiVolumeAssigner : ILetterAssigner
{
    private const string GlobalGateName = @"Global\UsbDriveManager.LetterAssign";
    private const string LocalGateName = @"UsbDriveManager.LetterAssign";
    private static readonly Mutex Gate = CreateGate();

    public LetterAssignResult Assign(char targetLetter, char? currentLetter)
    {
        var target = char.ToUpperInvariant(targetLetter);

        if (!DriveLetters.IsAssignable(target))
        {
            return LetterAssignResult.Fail($"目标盘符 {target}: 不合法：只允许 D–Z（A/B 为软驱保留，C 为系统盘）。");
        }

        if (currentLetter is null)
        {
            return LetterAssignResult.Fail("该卷当前没有盘符，无法按盘符定位；请改用卷标识分配。");
        }

        var current = char.ToUpperInvariant(currentLetter.Value);
        if (current == target)
        {
            return LetterAssignResult.Fail($"卷已经在 {target}: 上，无需改派。");
        }

        var volume = Wmi.Single($"SELECT DeviceID FROM Win32_Volume WHERE DriveLetter='{current}:'");
        if (volume is null)
        {
            return LetterAssignResult.Fail($"找不到盘符 {current}: 对应的卷。");
        }

        string volumeId;
        using (volume)
        {
            volumeId = volume.Text("DeviceID");
        }

        return AssignVolume(volumeId, target);
    }

    public LetterAssignResult AssignVolume(string volumeId, char targetLetter)
    {
        var target = char.ToUpperInvariant(targetLetter);

        if (!DriveLetters.IsAssignable(target))
        {
            return LetterAssignResult.Fail($"目标盘符 {target}: 不合法：只允许 D–Z（A/B 为软驱保留，C 为系统盘）。");
        }

        if (string.IsNullOrWhiteSpace(volumeId))
        {
            return LetterAssignResult.Fail("卷标识为空，无法分配盘符。");
        }

        if (!TryEnterGate(TimeSpan.FromSeconds(15), out var gateError))
        {
            return LetterAssignResult.Fail(gateError ?? "等待盘符分配锁超时。");
        }

        try
        {
            var volume = Wmi.Single($"SELECT * FROM Win32_Volume WHERE DeviceID=\"{Wmi.Escape(volumeId)}\"");
            if (volume is null)
            {
                return LetterAssignResult.Fail($"找不到卷：{volumeId}");
            }

            using (volume)
            {
                volume["DriveLetter"] = $"{target}:";
                volume.Put();
            }

            return LetterAssignResult.Ok($"盘符已设为 {target}:");
        }
        catch (ManagementException ex)
        {
            return LetterAssignResult.Fail($"分配盘符失败（{target}:）：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return LetterAssignResult.Fail($"分配盘符需要管理员权限：{ex.Message}");
        }
        finally
        {
            Gate.ReleaseMutex();
        }
    }

    private static bool TryEnterGate(TimeSpan timeout, out string? error)
    {
        error = null;

        try
        {
            if (Gate.WaitOne(timeout))
            {
                return true;
            }

            error = "等待盘符分配锁超时（另一个操作正在进行）。";
            return false;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
        catch (ObjectDisposedException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Mutex CreateGate()
    {
        try
        {
            return new Mutex(initiallyOwned: false, GlobalGateName);
        }
        catch (UnauthorizedAccessException)
        {
            return new Mutex(initiallyOwned: false, LocalGateName);
        }
        catch (IOException)
        {
            return new Mutex(initiallyOwned: false, LocalGateName);
        }
    }
}
