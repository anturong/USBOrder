using System.Text.RegularExpressions;
using UsbDriveManager.Core.Models;

namespace UsbDriveManager.Core;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Message, string? RuleId = null)
{
    public override string ToString() => $"[{Severity}] {Message}";
}

/// <summary>保存配置前的静态校验；运行期匹配不依赖这里，但编辑器据此拦截明显错误。</summary>
public static partial class ConfigValidator
{
    [GeneratedRegex("^[0-9A-F]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Hex4();

    public static IReadOnlyList<ValidationIssue> Validate(AppConfig config)
    {
        var issues = new List<ValidationIssue>();
        if (config?.Rules is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "规则列表缺失。"));
            return issues;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in config.Rules)
        {
            var label = Describe(rule);

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "规则缺少 Id。", rule.Id));
            }
            else if (!seenIds.Add(rule.Id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"{label} 的 Id 重复。", rule.Id));
            }

            if (!Hex4().IsMatch(rule.Vid ?? ""))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"{label} 的 VID 必须是 4 位十六进制。", rule.Id));
            }

            if (!Hex4().IsMatch(rule.Pid ?? ""))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"{label} 的 PID 必须是 4 位十六进制。", rule.Id));
            }

            if (!DriveLetters.IsAssignable(rule.TargetLetter))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"{label} 的目标盘符 {rule.TargetLetter}: 不合法：只允许 D–Z。",
                    rule.Id));
            }

            if (!rule.UsesSerial)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    $"{label} 未填序列号，将按 VID/PID 通配匹配：同型号的盘都会命中。",
                    rule.Id));
            }
        }

        var duplicateTargets = config.Rules
            .GroupBy(r => char.ToUpperInvariant(r.TargetLetter))
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateTargets)
        {
            var names = string.Join("、", group.Select(Describe));
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                $"盘符 {group.Key}: 被多条规则共用（{names}）：同时插入时按先到先得处理。"));
        }

        var duplicateFingerprints = config.Rules
            .GroupBy(
                r => $"{r.Vid?.ToUpperInvariant()}|{r.Pid?.ToUpperInvariant()}|{SerialRules.Normalize(r.Serial).ToUpperInvariant()}",
                StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateFingerprints)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                $"多条规则使用同一设备指纹：{string.Join("、", group.Select(Describe))}。"));
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues)
        => issues.Any(i => i.Severity == ValidationSeverity.Error);

    private static string Describe(DriveRule rule)
    {
        var name = string.IsNullOrWhiteSpace(rule.Name) ? "(未命名)" : rule.Name.Trim();
        var serial = rule.UsesSerial ? rule.Serial : "*";
        return $"{name}[{rule.Vid}:{rule.Pid} SN={serial}]";
    }
}
