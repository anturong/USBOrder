using UsbDriveManager.Core.Models;
using UsbDriveManager.Core.Wmi;

namespace UsbDriveManager.Core;

/// <summary>
/// 冲突决策与分配：纯逻辑，不直接碰 WMI——依赖 <see cref="IVolumeInspector"/> /
/// <see cref="ILetterAssigner"/>，便于用假实现覆盖所有分支做单元测试。
/// </summary>
public sealed class AssignmentEngine
{
    private readonly IVolumeInspector _inspector;

    public AssignmentEngine(IVolumeInspector inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    /// <summary>根据配置与目标盘符的占用情况给出决策。</summary>
    public AssignmentDecision Decide(UsbDiskInfo disk, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(config);

        var rule = config.FindMatch(disk.Identity);
        if (rule is null)
        {
            return new AssignmentDecision
            {
                Action = AssignmentAction.SkipNoRule,
                Disk = disk,
                Message = $"设备 {disk.Identity.DisplayId} 未命中任何规则。",
            };
        }

        var target = char.ToUpperInvariant(rule.TargetLetter);

        if (!DriveLetters.IsAssignable(target))
        {
            return new AssignmentDecision
            {
                Action = AssignmentAction.RefuseInvalidLetter,
                Disk = disk,
                Rule = rule,
                TargetLetter = target,
                Message = $"规则 {DescribeRule(rule)} 的目标盘符 {target}: 不合法：只允许 D–Z。",
            };
        }

        if (disk.IsAssignedTo(target))
        {
            return new AssignmentDecision
            {
                Action = AssignmentAction.SkipAlreadyAssigned,
                Disk = disk,
                Rule = rule,
                TargetLetter = target,
                Message = $"设备已在 {target}:，无需调整。",
            };
        }

        var occupant = _inspector.Inspect(target);

        if (!occupant.Exists)
        {
            return new AssignmentDecision
            {
                Action = AssignmentAction.Assign,
                Disk = disk,
                Rule = rule,
                TargetLetter = target,
                Occupant = occupant,
                Message = $"分配 {target}: 给 {disk.Identity.DisplayId}。",
            };
        }

        var occupantIsRuleDevice = occupant.IsUsbDisk
                                   && occupant.Identity is not null
                                   && config.FindMatch(occupant.Identity) is not null;

        if (occupantIsRuleDevice)
        {
            return new AssignmentDecision
            {
                Action = AssignmentAction.RefuseCompetition,
                Disk = disk,
                Rule = rule,
                TargetLetter = target,
                Occupant = occupant,
                Message = $"{target}: 已被规则内的另一台设备占用（{occupant.Description}），按先到先得保持系统临时盘符。",
            };
        }

        return new AssignmentDecision
        {
            Action = AssignmentAction.RefuseConflict,
            Disk = disk,
            Rule = rule,
            TargetLetter = target,
            Occupant = occupant,
            Message = $"{target}: 被无关卷占用（{occupant.Description}），拒绝分配，请管理员调整配置或释放该盘符。",
        };
    }

    /// <summary>执行分配（仅 <see cref="AssignmentAction.Assign"/> 会真正改盘符）。</summary>
    public LetterAssignResult Apply(AssignmentDecision decision, ILetterAssigner assigner)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(assigner);

        if (!decision.ShouldAssign || decision.TargetLetter is null)
        {
            return LetterAssignResult.Fail("当前决策不需要分配。");
        }

        // 优先选已经有盘符的分区（多分区盘保持原有语义），否则选第一个有卷标识的分区。
        var partition = decision.Disk.Partitions.FirstOrDefault(p => p.Letter.HasValue && p.VolumeId.Length > 0)
                        ?? decision.Disk.Partitions.FirstOrDefault(p => p.VolumeId.Length > 0);

        if (partition is null)
        {
            return LetterAssignResult.Fail("找不到可分配的卷：磁盘可能未分区、未格式化，或卷信息尚未就绪。");
        }

        return assigner.AssignVolume(partition.VolumeId, decision.TargetLetter.Value);
    }

    private static string DescribeRule(DriveRule rule)
    {
        var name = string.IsNullOrWhiteSpace(rule.Name) ? "(未命名)" : rule.Name.Trim();
        var serial = rule.UsesSerial ? rule.Serial : "*";
        return $"{name}[{rule.Vid}:{rule.Pid} SN={serial}]";
    }
}
