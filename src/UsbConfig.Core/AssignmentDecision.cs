using UsbDriveManager.Core.Models;

namespace UsbDriveManager.Core;

/// <summary>一次插入事件的决策结果；由 <see cref="AssignmentEngine"/> 产出，服务与编辑器共同消费。</summary>
public enum AssignmentAction
{
    /// <summary>可以分配。</summary>
    Assign,

    /// <summary>没有规则命中，静默跳过。</summary>
    SkipNoRule,

    /// <summary>已经在该盘符上，无需动作。</summary>
    SkipAlreadyAssigned,

    /// <summary>目标盘符非法（A/B/C 或非字母）。</summary>
    RefuseInvalidLetter,

    /// <summary>目标盘符被规则内的另一台设备占用（先到先得）。</summary>
    RefuseCompetition,

    /// <summary>目标盘符被无关卷占用（真冲突，需人工处理）。</summary>
    RefuseConflict,
}

public sealed class AssignmentDecision
{
    public required AssignmentAction Action { get; init; }

    public required UsbDiskInfo Disk { get; init; }

    public char? TargetLetter { get; init; }

    public DriveRule? Rule { get; init; }

    public VolumeOccupant? Occupant { get; init; }

    public string Message { get; init; } = "";

    /// <summary>是否需要给当前登录用户弹系统消息框。</summary>
    public bool NotifyUser => Action is AssignmentAction.RefuseCompetition or AssignmentAction.RefuseConflict;

    public bool ShouldAssign => Action == AssignmentAction.Assign;

    public override string ToString() => $"{Action}: {Message}";
}

/// <summary>盘符合法性规则：只允许 D–Z（A/B 为软驱保留、C 为系统盘）。</summary>
public static class DriveLetters
{
    public static bool IsAssignable(char letter)
    {
        var upper = char.ToUpperInvariant(letter);
        return upper is >= 'D' and <= 'Z';
    }

    /// <summary>把用户输入规整为单个大写字母；失败返回 false。</summary>
    public static bool TryNormalize(string? text, out char letter)
    {
        letter = '\0';
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith(':'))
        {
            trimmed = trimmed[..^1].Trim();
        }

        if (trimmed.Length != 1 || !char.IsLetter(trimmed[0]))
        {
            return false;
        }

        letter = char.ToUpperInvariant(trimmed[0]);
        return true;
    }

    public static string ToDisplay(char letter) => $"{char.ToUpperInvariant(letter)}:";

    public static string FormatSize(ulong bytes)
    {
        if (bytes == 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}
