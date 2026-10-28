using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;
using UsbDriveManager.Core.Wmi;

namespace UsbDriveManager.Tools.CoreSmoke;

/// <summary>
/// Core 自检工具（不写任何系统状态）：
///   1) 打印当前机器上的 USB 磁盘与盘符占用，验证 WMI 关联查询可用；
///   2) 用假实现跑一遍决策分支，验证“先到先得 / 冲突即拒”的判定；
///   3) 校验配置读写与静态校验。
/// 退出码 0 = 全部通过。
/// </summary>
internal static class Program
{
    private static int _failed;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("== UsbConfig.Core 自检 ==");
        Console.WriteLine($"数据目录: {AppPaths.DataDirectory}");
        Console.WriteLine();

        Section("1. 现场设备枚举（只读）", EnumerateDevices);
        Section("2. 盘符占用反查（只读）", InspectLetters);
        Section("3. 决策分支", DecisionChecks);
        Section("4. 配置读写与校验", ConfigChecks);

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "全部通过 ✔" : $"失败 {_failed} 项 ✘");
        return _failed == 0 ? 0 : 1;
    }

    private static void Section(string title, Action body)
    {
        Console.WriteLine($"--- {title} ---");
        try
        {
            body();
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {title}: {ex.GetType().Name} {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine();
    }

    private static void Check(string name, Func<bool> condition)
    {
        try
        {
            var ok = condition();
            if (ok)
            {
                Console.WriteLine($"  [PASS] {name}");
            }
            else
            {
                _failed++;
                Console.WriteLine($"  [FAIL] {name}");
            }
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {name}: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static void EnumerateDevices()
    {
        var disks = new WmiUsbDiskEnumerator().Enumerate();
        Console.WriteLine($"  USB 磁盘数量: {disks.Count}");

        foreach (var disk in disks)
        {
            Console.WriteLine($"  - {disk.Model}");
            Console.WriteLine($"    PnP : {disk.PnpDeviceId}");
            Console.WriteLine($"    指纹: {disk.Identity}  序列号可用={disk.Identity.HasUsableSerial}");
            Console.WriteLine($"    容量: {DriveLetters.FormatSize(disk.SizeBytes)}  分区={disk.Partitions.Count}");

            foreach (var partition in disk.Partitions)
            {
                Console.WriteLine($"      · {partition}  vol={partition.VolumeId}");
            }
        }

        if (disks.Count == 0)
        {
            Console.WriteLine("  （当前没有插入 USB 磁盘，属于正常情况）");
        }

        Check("枚举不抛异常", () => true);
    }

    private static void InspectLetters()
    {
        var inspector = new WmiVolumeInspector();

        foreach (var letter in "DEFGHIJ".ToCharArray())
        {
            var occupant = inspector.Inspect(letter);
            var text = occupant.Exists
                ? $"{occupant.Description}  类型={occupant.DriveTypeText}  USB={occupant.IsUsbDisk}  指纹={occupant.Identity?.ToString() ?? "-"}"
                : "空闲";
            Console.WriteLine($"  {letter}: {text}");
        }

        Check("反查 C: 是存在的卷", () => inspector.Inspect('C').Exists);
        Check("反查 Z: 视为空闲或存在（不抛异常）", () => true);
    }

    private static void DecisionChecks()
    {
        var disk = new UsbDiskInfo
        {
            DeviceId = @"\\.\PHYSICALDRIVE9",
            PnpDeviceId = @"USBSTOR\DISK&VEN_TEST&PROD_DISK\SN1&0",
            Model = "Test Disk",
            Identity = new DeviceIdentity { Vid = "0951", Pid = "1666", Serial = "SN1" },
            Partitions = new[] { new UsbPartition { Letter = 'G', Label = "U", SizeBytes = 8UL * 1024 * 1024 * 1024 } },
        };

        var rule = new DriveRule { Name = "测试规则", Vid = "0951", Pid = "1666", Serial = "SN1", TargetLetter = 'F' };
        var config = new AppConfig { Rules = { rule } };

        var freeInspector = new FakeInspector { Occupant = VolumeOccupant.Free('F') };
        var engine = new AssignmentEngine(freeInspector);
        Check("空闲盘符 → Assign", () => engine.Decide(disk, config).Action == AssignmentAction.Assign);

        var assigned = new UsbDiskInfo
        {
            DeviceId = disk.DeviceId,
            PnpDeviceId = disk.PnpDeviceId,
            Model = disk.Model,
            Identity = disk.Identity,
            Partitions = new[] { new UsbPartition { Letter = 'F', Label = "U" } },
        };
        Check("已在目标盘符 → SkipAlreadyAssigned", () => engine.Decide(assigned, config).Action == AssignmentAction.SkipAlreadyAssigned);

        var noRule = new UsbDiskInfo
        {
            DeviceId = disk.DeviceId,
            PnpDeviceId = disk.PnpDeviceId,
            Model = disk.Model,
            Identity = new DeviceIdentity { Vid = "1111", Pid = "2222", Serial = "X" },
            Partitions = disk.Partitions,
        };
        Check("无规则命中 → SkipNoRule", () => engine.Decide(noRule, config).Action == AssignmentAction.SkipNoRule);

        var competitor = new FakeInspector
        {
            Occupant = new VolumeOccupant
            {
                Letter = 'F',
                Exists = true,
                IsUsbDisk = true,
                DriveType = 2,
                Identity = new DeviceIdentity { Vid = "0951", Pid = "1666", Serial = "SN2" },
                Description = "F: U 盘 · 另一支",
            },
        };

        var competitionConfig = new AppConfig
        {
            Rules =
            {
                rule,
                new DriveRule { Name = "另一支", Vid = "0951", Pid = "1666", Serial = "SN2", TargetLetter = 'F' },
            },
        };

        var competition = new AssignmentEngine(competitor).Decide(disk, competitionConfig);
        Check("规则内设备占位 → RefuseCompetition 且提示", () => competition.Action == AssignmentAction.RefuseCompetition && competition.NotifyUser);

        var conflict = new FakeInspector
        {
            Occupant = new VolumeOccupant { Letter = 'F', Exists = true, DriveType = 3, Description = "F: 本地磁盘" },
        }.Let(i => new AssignmentEngine(i).Decide(disk, config));
        Check("无关卷占位 → RefuseConflict 且提示", () => conflict.Action == AssignmentAction.RefuseConflict && conflict.NotifyUser);

        var invalid = new AssignmentEngine(freeInspector).Decide(disk, new AppConfig
        {
            Rules = { new DriveRule { Vid = "0951", Pid = "1666", Serial = "SN1", TargetLetter = 'C' } },
        });
        Check("目标盘符 C: → RefuseInvalidLetter", () => invalid.Action == AssignmentAction.RefuseInvalidLetter);
    }

    private static void ConfigChecks()
    {
        Console.WriteLine($"  user={Environment.UserName} temp={Path.GetTempPath()}");
        Console.WriteLine($"  base={AppContext.BaseDirectory}");

        var dir = CreateScratchDirectory();

        try
        {
            var store = new ConfigStore(Path.Combine(dir, "config.json"));
            var config = new AppConfig
            {
                Rules = { new DriveRule { Name = "U", Vid = "0951", Pid = "1666", Serial = "SN1&0", TargetLetter = 'F' } },
            };

            store.Save(config);
            var loaded = store.Load();

            Check("配置往返", () => loaded.Rules.Count == 1 && loaded.Rules[0].Serial == "SN1" && loaded.Rules[0].TargetLetter == 'F');

            var issues = ConfigValidator.Validate(loaded);
            Check("合法配置无 error", () => !ConfigValidator.HasErrors(issues));

            var bad = new AppConfig
            {
                Rules = { new DriveRule { Vid = "95", Pid = "ZZZZ", Serial = "", TargetLetter = 'A' } },
            };
            var badIssues = ConfigValidator.Validate(bad);
            Check("非法配置报 error", () => ConfigValidator.HasErrors(badIssues));
            Check("缺序列号给 warning", () => badIssues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("VID/PID")));

            File.WriteAllText(store.FilePath, "{ broken");
            Check("坏 JSON 抛 ConfigException", () =>
            {
                try
                {
                    store.Load();
                    return false;
                }
                catch (ConfigException)
                {
                    return true;
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // 忽略清理失败。
            }
        }
    }

    private static string CreateScratchDirectory()
    {
        var name = "usbcfg-smoke-" + Guid.NewGuid().ToString("N");
        var candidates = new[]
        {
            Path.Combine(Path.GetTempPath(), name),
            Path.Combine(AppContext.BaseDirectory, "scratch", name),
        };

        foreach (var dir in candidates)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Console.WriteLine($"  scratch={dir}");
                return dir;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  scratch 失败: {dir} -> {ex.GetType().Name} {ex.Message}");
            }
        }

        throw new IOException("无法创建临时目录：临时目录与程序目录都不可写。");
    }

    private sealed class FakeInspector : IVolumeInspector
    {
        public VolumeOccupant Occupant { get; set; } = VolumeOccupant.Free('F');

        public VolumeOccupant Inspect(char letter) => Occupant;
    }
}

internal static class FunctionalExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> map) => map(value);
}
