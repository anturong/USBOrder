using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;
using UsbDriveManager.Core.Wmi;

namespace UsbDriveManager.Service;

/// <summary>
/// 服务主体逻辑（服务模式与控制台模式共用）：
/// 订阅插拔 → 延迟 500ms → 串行处理 → 决策 → 分配 / 记录 / 弹窗。
/// 另有 10 秒一次的兜底巡检：UASP 盘若挂载时没拿到盘符，不会产生卷变更事件，
/// 只能靠巡检发现并补一个临时盘符。
/// 配置只在启动时加载一次（方案约定：改配置必须重启服务）。
/// </summary>
internal sealed class ServiceHost : IDisposable
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(10);

    private readonly ServiceLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILetterAssigner _assigner = new WmiVolumeAssigner();
    private readonly Dictionary<string, DateTimeOffset> _lastNotify = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan NotifyThrottle = TimeSpan.FromMinutes(2);

    private VolumeChangeWatcher? _watcher;
    private Timer? _reconcileTimer;
    private AssignmentEngine? _engine;
    private AppConfig _config = AppConfig.CreateDefault();
    private bool _disposed;

    public ServiceHost(bool console)
    {
        var fileLog = new FileLogger(prefix: "service");
        _log = new ServiceLog(fileLog, console);
    }

    public void Start()
    {
        _log.Info("USB 盘符管理服务启动");

        try
        {
            AppPaths.EnsureDataDirectory();
            _config = new ConfigStore().LoadOrCreate();
            _log.Info($"配置已加载：{_config.Rules.Count} 条规则（{AppPaths.ConfigFile}）");

            foreach (var issue in ConfigValidator.Validate(_config))
            {
                _log.Warn($"配置校验：{issue}");
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载配置失败，服务将以空规则运行", ex);
            _config = AppConfig.CreateDefault();
        }

        _engine = new AssignmentEngine(new WmiVolumeInspector());

        _watcher = new VolumeChangeWatcher();
        _watcher.VolumeChanged += OnVolumeChanged;

        if (!_watcher.TryStart(out var error))
        {
            _log.Error($"WMI 订阅失败：{error}");
        }
        else
        {
            _log.Info("已订阅 Win32_VolumeChangeEvent（插入/拔出）");
        }

        // 兜底巡检：覆盖“插上了但没拿到盘符、因此没有卷事件”的情况。
        _reconcileTimer = new Timer(
            _ => _ = ProcessAsync("定时巡检", verbose: false),
            state: null,
            dueTime: ReconcileInterval,
            period: ReconcileInterval);

        // 服务重启时已经插着的盘也要处理一遍。
        _ = Task.Run(() => ProcessAsync("启动扫描", verbose: true));
    }

    private void OnVolumeChanged(VolumeChangeEvent e)
    {
        _ = HandleAsync(e);
    }

    private async Task HandleAsync(VolumeChangeEvent e)
    {
        try
        {
            if (e.EventType == VolumeChangeEvent.Removal)
            {
                // 拔盘只记日志，不触发任何分配动作（方案 §3.2）。
                _log.Info($"拔出事件：{e.DriveName}");
                return;
            }

            _log.Info($"插入事件：{e.DriveName}，等待 500ms 让卷就绪");
            await Task.Delay(500).ConfigureAwait(false);
            await ProcessAsync("插入事件", verbose: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error("处理插拔事件失败", ex);
        }
    }

    private async Task ProcessAsync(string reason, bool verbose)
    {
        if (_engine is null)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var disks = new WmiUsbDiskEnumerator().Enumerate();
            if (disks.Count == 0)
            {
                if (verbose)
                {
                    _log.Info($"{reason}：当前没有 USB 磁盘");
                }

                return;
            }

            foreach (var disk in disks)
            {
                ProcessDisk(disk, reason, verbose);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{reason}：枚举 USB 磁盘失败", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ProcessDisk(UsbDiskInfo disk, string reason, bool verbose)
    {
        var decision = _engine!.Decide(disk, _config);

        switch (decision.Action)
        {
            case AssignmentAction.Assign:
                var result = _engine.Apply(decision, _assigner);
                if (result.Success)
                {
                    _log.Info($"{reason}：{disk} — {decision.Message} {result.Message}");
                }
                else
                {
                    _log.Error($"{reason}：{disk} — 分配失败：{result.Message}");
                }

                break;

            case AssignmentAction.RefuseCompetition:
            case AssignmentAction.RefuseConflict:
                HandleRefusal(disk, decision, reason, verbose);
                break;

            case AssignmentAction.RefuseInvalidLetter:
                if (verbose)
                {
                    _log.Error($"{reason}：配置错误 — {decision.Message}");
                }

                break;

            default:
                if (verbose)
                {
                    _log.Info($"{reason}：{disk} — {decision.Message}");
                }

                break;
        }
    }

    private void HandleRefusal(UsbDiskInfo disk, AssignmentDecision decision, string reason, bool verbose)
    {
        // 竞争/冲突失败后，如果这块盘压根没有盘符，补一个空闲临时盘符，
        // 免得盘在资源管理器里“消失”；弹窗里同时告诉用户它现在叫什么。
        char? assigned = null;

        if (!disk.Letters.Any())
        {
            assigned = EnsureTemporaryLetter(disk, reason, verbose);
        }

        var letters = assigned is not null
            ? new[] { $"{assigned}:" }
            : disk.Letters.Select(letter => $"{letter}:").ToArray();

        var suffix = letters.Length > 0
            ? $"本盘当前盘符：{string.Join("、", letters)}。"
            : "本盘当前没有盘符，请手动处理。";

        var message = decision.Message + Environment.NewLine + suffix;
        var kind = decision.Action == AssignmentAction.RefuseCompetition ? "竞争" : "冲突";

        if (!verbose)
        {
            return;
        }

        if (decision.Action == AssignmentAction.RefuseCompetition)
        {
            _log.Warn($"{reason}：{kind} — {message}");
        }
        else
        {
            _log.Error($"{reason}：{kind} — {message}");
        }

        // 同一块盘 2 分钟内只弹一次：分配临时盘符本身会再触发一次卷事件，避免连弹两个框。
        if (ShouldNotify(disk.PnpDeviceId))
        {
            UserNotifier.Notify("USB 盘符分配", message, _log);
        }
    }

    private bool ShouldNotify(string key)
    {
        var now = DateTimeOffset.Now;

        if (_lastNotify.TryGetValue(key, out var last) && now - last < NotifyThrottle)
        {
            return false;
        }

        _lastNotify[key] = now;
        return true;
    }

    private char? EnsureTemporaryLetter(UsbDiskInfo disk, string reason, bool verbose)
    {
        var partition = disk.Partitions.FirstOrDefault(p => p.VolumeId.Length > 0);
        if (partition is null)
        {
            if (verbose)
            {
                _log.Warn($"{reason}：{disk} 没有可用卷，无法分配临时盘符。");
            }

            return null;
        }

        var free = PickFreeLetter(disk);
        if (free is null)
        {
            if (verbose)
            {
                _log.Warn($"{reason}：{disk} 找不到空闲盘符，无法分配临时盘符。");
            }

            return null;
        }

        var result = _assigner.AssignVolume(partition.VolumeId, free.Value);
        if (result.Success)
        {
            _log.Info($"{reason}：{disk} 未获得目标盘符，已临时挂载为 {free}:");
            return free;
        }

        if (verbose)
        {
            _log.Warn($"{reason}：{disk} 临时盘符分配失败：{result.Message}");
        }

        return null;
    }

    private char? PickFreeLetter(UsbDiskInfo disk)
    {
        var used = DriveLetterMap.GetUsedLetters();
        var reserved = new HashSet<char>(
            _config.EnabledRules.Select(rule => char.ToUpperInvariant(rule.TargetLetter)));

        foreach (var candidate in "DEFGHIJKLMNOPQRSTUVWXYZ")
        {
            if (used.Contains(candidate) || reserved.Contains(candidate) || disk.IsAssignedTo(candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _reconcileTimer?.Dispose();
        _reconcileTimer = null;

        if (_watcher is not null)
        {
            _watcher.VolumeChanged -= OnVolumeChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        _log.Info("USB 盘符管理服务停止");
        _log.Dispose();
        _gate.Dispose();
    }
}
