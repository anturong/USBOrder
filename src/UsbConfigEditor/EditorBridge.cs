using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;
using UsbDriveManager.Core.Wmi;
using UsbDriveManager.Editor.Services;

namespace UsbDriveManager.Editor;

/// <summary>
/// WebView2 ↔ C# 的最小 JSON-RPC 桥：
/// 前端 postMessage({id, method, params})，这里回 {id, ok, result|error}。
/// 所有耗时操作放到线程池，避免阻塞 UI 线程。
/// </summary>
internal sealed class EditorBridge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly WebView2 _webView;

    public EditorBridge(WebView2 webView) => _webView = webView;

    public void Attach()
    {
        _webView.CoreWebView2.WebMessageReceived += OnMessage;
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? id = null;

        try
        {
            var request = JsonNode.Parse(e.WebMessageAsJson) as JsonObject;
            id = request?["id"]?.GetValue<string>();
            var method = request?["method"]?.GetValue<string>() ?? "";
            var parameters = request?["params"] as JsonObject;

            var result = await Task.Run(() => DispatchAsync(method, parameters)).ConfigureAwait(true);
            Post(new { id, ok = true, result });
        }
        catch (Exception ex)
        {
            Post(new { id, ok = false, error = Unwrap(ex).Message });
        }
    }

    private void Post(object payload)
        => _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json));

    private static Exception Unwrap(Exception ex)
    {
        while (ex is AggregateException { InnerException: not null } aggregate)
        {
            ex = aggregate.InnerException;
        }

        return ex;
    }

    internal static object? DispatchAsync(string method, JsonObject? parameters) => method switch
    {
        "getStatus" => GetStatus(),
        "listDevices" => ListDevices(),
        "listVolumes" => ListVolumes(),
        "loadConfig" => LoadConfig(),
        "saveConfig" => SaveConfig(parameters),
        "assignLetter" => AssignLetter(parameters),
        "installService" => InstallService(parameters),
        "uninstallService" => UninstallService(),
        "startService" => StartService(),
        "stopService" => StopService(),
        "openFolder" => OpenFolder(parameters),
        _ => throw new InvalidOperationException($"未知方法：{method}"),
    };

    // ---------------- 查询 ----------------

    private static object GetStatus()
    {
        var status = ServiceManager.GetStatus();
        var config = TryLoadConfig();

        return new
        {
            installed = status is not null,
            state = status?.ToString() ?? "NotInstalled",
            exePath = ServiceManager.InstalledExePath,
            account = ReadServiceAccount(),
            configPath = AppPaths.ConfigFile,
            logPath = AppPaths.LogDirectory,
            ruleCount = config?.Rules.Count ?? 0,
        };
    }

    private static object ListDevices()
    {
        var config = new ConfigStore().Load();
        var engine = new AssignmentEngine(new WmiVolumeInspector());
        var disks = new WmiUsbDiskEnumerator().Enumerate();

        var items = new List<object>();

        foreach (var disk in disks)
        {
            var decision = engine.Decide(disk, config);
            var volumeId = disk.Partitions.FirstOrDefault(p => p.VolumeId.Length > 0)?.VolumeId ?? "";

            items.Add(new
            {
                id = disk.PnpDeviceId,
                name = disk.Model,
                vid = disk.Identity.Vid,
                pid = disk.Identity.Pid,
                serial = disk.Identity.HasUsableSerial ? disk.Identity.Serial : "",
                serialUsable = disk.Identity.HasUsableSerial,
                letters = disk.Letters.Select(letter => letter.ToString()).ToArray(),
                volumeId,
                partitions = disk.Partitions.Count,
                sizeBytes = disk.SizeBytes,
                status = MapStatus(decision.Action),
                note = decision.Message,
                targetLetter = decision.TargetLetter?.ToString() ?? "",
            });
        }

        return new
        {
            devices = items,
            usedLetters = DriveLetterMap.GetUsedLetters().Select(letter => letter.ToString()).ToArray(),
        };
    }

    private static object ListVolumes()
    {
        var volumes = new WmiAllVolumesEnumerator().Enumerate();

        return new
        {
            volumes = volumes.Select(volume => new
            {
                letter = volume.Letter.ToString(),
                driveType = volume.DriveType,
                label = volume.Label,
                sizeBytes = volume.SizeBytes,
                freeBytes = volume.FreeBytes,
                isUsb = volume.IsUsb,
                diskModel = volume.DiskModel,
                fileSystem = volume.FileSystem,
            }).ToArray(),
        };
    }

    private static string MapStatus(AssignmentAction action) => action switch
    {
        AssignmentAction.Assign => "ready",
        AssignmentAction.SkipAlreadyAssigned => "assigned",
        AssignmentAction.RefuseCompetition => "competition",
        AssignmentAction.RefuseConflict => "conflict",
        AssignmentAction.RefuseInvalidLetter => "error",
        _ => "unmanaged",
    };

    private static object LoadConfig()
    {
        var config = new ConfigStore().Load();
        var devices = TryEnumerate();

        return new
        {
            rules = config.Rules.Select(rule =>
            {
                // 名称统一用当前设备的型号：上下两处（设备表 / 规则表）对同一台盘显示同一个名字。
                var matched = devices.FirstOrDefault(device => rule.Matches(device.Identity));
                return new
                {
                    id = rule.Id,
                    name = matched?.Model ?? rule.Name,
                    vid = rule.Vid,
                    pid = rule.Pid,
                    serial = rule.Serial,
                    target = rule.TargetLetter.ToString(),
                    enabled = rule.Enabled,
                };
            }).ToArray(),
        };
    }

    private static IReadOnlyList<UsbDiskInfo> TryEnumerate()
    {
        try
        {
            return new WmiUsbDiskEnumerator().Enumerate();
        }
        catch
        {
            return Array.Empty<UsbDiskInfo>();
        }
    }

    private static AppConfig? TryLoadConfig()
    {
        try
        {
            return new ConfigStore().Load();
        }
        catch
        {
            return null;
        }
    }

    private static string ReadServiceAccount()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\" + ServiceManager.ServiceName);

        return key?.GetValue("ObjectName") as string ?? "";
    }

    // ---------------- 命令 ----------------

    private static object SaveConfig(JsonObject? parameters)
    {
        var config = new AppConfig();

        if (parameters?["rules"] is JsonArray rules)
        {
            foreach (var node in rules.OfType<JsonObject>())
            {
                config.Rules.Add(new DriveRule
                {
                    Id = node["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Name = node["name"]?.GetValue<string>() ?? "",
                    Vid = node["vid"]?.GetValue<string>() ?? "",
                    Pid = node["pid"]?.GetValue<string>() ?? "",
                    Serial = node["serial"]?.GetValue<string>() ?? "",
                    TargetLetter = ParseLetter(node["target"]?.GetValue<string>()),
                    Enabled = node["enabled"]?.GetValue<bool>() ?? true,
                });
            }
        }

        // 保存时把名称对齐到当前设备型号（插着的设备），保证配置里的名字和设备表一致。
        var devices = TryEnumerate();
        foreach (var rule in config.Rules)
        {
            var matched = devices.FirstOrDefault(device => rule.Matches(device.Identity));
            if (matched is not null)
            {
                rule.Name = matched.Model;
            }
        }

        // 同指纹重复规则没有意义，保存时去重（保留第一条），并提示。
        var deduped = new List<DriveRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in config.Rules)
        {
            var key = $"{rule.Vid}|{rule.Pid}|{SerialRules.Normalize(rule.Serial)}";
            if (seen.Add(key))
            {
                deduped.Add(rule);
            }
        }

        var removedDuplicates = config.Rules.Count - deduped.Count;
        config.Rules = deduped;

        var issues = ConfigValidator.Validate(config);
        var errors = issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToArray();

        if (errors.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error => error.Message)));
        }

        new ConfigStore().Save(config);

        var restart = parameters?["restart"]?.GetValue<bool>() ?? true;
        var restarted = false;

        if (restart && ServiceManager.IsInstalled)
        {
            ServiceManager.Stop();
            ServiceManager.Start();
            restarted = true;
        }

        var warnings = issues.Where(issue => issue.Severity == ValidationSeverity.Warning)
                             .Select(issue => issue.Message)
                             .ToList();

        if (removedDuplicates > 0)
        {
            warnings.Insert(0, $"已自动忽略 {removedDuplicates} 条重复指纹的规则。");
        }

        return new
        {
            saved = true,
            restarted,
            warnings = warnings.ToArray(),
        };
    }

    private static object AssignLetter(JsonObject? parameters)
    {
        var deviceId = parameters?["deviceId"]?.GetValue<string>() ?? "";
        var letterText = parameters?["letter"]?.GetValue<string>() ?? "";
        var force = parameters?["force"]?.GetValue<bool>() ?? false;

        if (!DriveLetters.TryNormalize(letterText, out var letter))
        {
            throw new InvalidOperationException($"盘符不合法：{letterText}");
        }

        var disk = new WmiUsbDiskEnumerator().Enumerate()
                       .FirstOrDefault(item => item.PnpDeviceId == deviceId)
                   ?? throw new InvalidOperationException("设备已不在线，请刷新后重试。");

        var occupant = new WmiVolumeInspector().Inspect(letter);

        if (occupant.Exists && !force)
        {
            throw new InvalidOperationException($"{letter}: 已被 {occupant.Description} 占用。");
        }

        var volumeId = disk.Partitions.FirstOrDefault(partition => partition.VolumeId.Length > 0)?.VolumeId
                       ?? throw new InvalidOperationException("该设备没有可用的卷（可能未分区或未格式化）。");

        var result = new WmiVolumeAssigner().AssignVolume(volumeId, letter);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return new { assigned = true, letter = letter.ToString(), message = result.Message };
    }

    private static object InstallService(JsonObject? parameters)
    {
        var mode = parameters?["mode"]?.GetValue<string>() ?? "existing";
        var account = parameters?["account"]?.GetValue<string>()?.Trim() ?? "";
        var password = parameters?["password"]?.GetValue<string>() ?? "";

        if (account.Length == 0)
        {
            throw new InvalidOperationException("账号不能为空。");
        }

        if (password.Length == 0)
        {
            throw new InvalidOperationException("密码不能为空。");
        }

        ServiceManager.Stop();
        var executable = ServicePayload.PrepareInstallFiles();
        var warnings = new List<string>();

        if (string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase))
        {
            if (password.Length < 12)
            {
                throw new InvalidOperationException("新建账号的密码至少 12 位。");
            }

            if (AccountManager.Exists(account))
            {
                throw new InvalidOperationException("该账号已存在。请改用“使用已有账号”，或换一个账号名。");
            }

            AccountManager.EnsureAccount(account, password);
            AccountManager.EnsureAdministrators(account);
            AccountManager.GrantServiceLogonRight(account);
            AccountManager.DenyInteractiveLogon(account);
            AccountManager.EnsureDataDirectoryAcl(account);

            ServiceManager.Install(executable, AccountManager.DomainAccount(account), password);
        }
        else
        {
            if (!AccountManager.Exists(account))
            {
                throw new InvalidOperationException($"本机不存在账号 {account}。请检查拼写，或改用“新建专用账号”。");
            }

            AccountManager.ValidateLogon(account, password);

            if (!AccountManager.IsAdministrator(account))
            {
                throw new InvalidOperationException("该账号不在 Administrators 组。改盘符需要管理员权限，请先把它加入该组。");
            }

            if (!AccountManager.PasswordNeverExpires(account))
            {
                warnings.Add("该账号密码未设为“永不过期”，密码到期后服务会启动失败。");
            }

            // 已有账号只补授服务登录权，绝不修改它的密码或标志位。
            AccountManager.GrantServiceLogonRight(account);
            AccountManager.EnsureDataDirectoryAcl(account);

            ServiceManager.Install(executable, AccountManager.DomainAccount(account), password);
        }

        ServiceManager.Start();
        return new { installed = true, mode, warnings = warnings.ToArray() };
    }

    private static object UninstallService()
    {
        ServiceManager.Uninstall();
        return new { installed = false };
    }

    private static object StartService()
    {
        ServiceManager.Start();
        return new { state = ServiceManager.GetStatus()?.ToString() ?? "NotInstalled" };
    }

    private static object StopService()
    {
        ServiceManager.Stop();
        return new { state = ServiceManager.GetStatus()?.ToString() ?? "NotInstalled" };
    }

    private static object OpenFolder(JsonObject? parameters)
    {
        var path = parameters?["path"]?.GetValue<string>() ?? "";

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        else if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            throw new InvalidOperationException($"路径不存在：{path}");
        }

        return new { opened = true };
    }

    private static char ParseLetter(string? text)
    {
        if (DriveLetters.TryNormalize(text, out var letter))
        {
            return letter;
        }

        return 'F';
    }
}
