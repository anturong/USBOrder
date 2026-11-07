using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;

namespace UsbConfig.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly ConfigStore _store;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "usbcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var config = new AppConfig
        {
            Rules =
            {
                new DriveRule
                {
                    Name = "产线 U 盘",
                    Vid = "0951",
                    Pid = "1666",
                    Serial = "4C530001234567890123&0",
                    TargetLetter = 'F',
                },
            },
        };

        _store.Save(config);
        var loaded = _store.Load();

        var rule = Assert.Single(loaded.Rules);
        Assert.Equal("0951", rule.Vid);
        Assert.Equal("1666", rule.Pid);
        Assert.Equal("4C530001234567890123", rule.Serial); // &0 被规范化掉
        Assert.Equal('F', rule.TargetLetter);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void Save_WritesOnlyPersistedProperties()
    {
        _store.Save(new AppConfig { Rules = { new DriveRule { Vid = "0951", Pid = "1666", Serial = "SN1" } } });

        var json = File.ReadAllText(_store.FilePath);

        Assert.DoesNotContain("enabledRules", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"rules\"", json);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfig()
    {
        var config = _store.Load();

        Assert.NotNull(config);
        Assert.Empty(config.Rules);
    }

    [Fact]
    public void Load_InvalidJson_ThrowsConfigException()
    {
        File.WriteAllText(_store.FilePath, "{ not json ");

        var ex = Assert.Throws<ConfigException>(() => _store.Load());
        Assert.Contains("JSON", ex.Message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响结果。
        }
    }
}

public class ConfigValidatorTests
{
    private static DriveRule Rule(string vid = "0951", string pid = "1666", string serial = "SN1", char letter = 'F')
        => new() { Name = "U", Vid = vid, Pid = pid, Serial = serial, TargetLetter = letter };

    [Fact]
    public void ValidRule_HasNoErrors()
    {
        var issues = ConfigValidator.Validate(new AppConfig { Rules = { Rule() } });

        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData("95", "1666")]
    [InlineData("0951", "XYZ")]
    public void BadVidOrPid_IsError(string vid, string pid)
    {
        var issues = ConfigValidator.Validate(new AppConfig { Rules = { Rule(vid: vid, pid: pid) } });

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('B')]
    [InlineData('C')]
    public void ReservedLetter_IsError(char letter)
    {
        var issues = ConfigValidator.Validate(new AppConfig { Rules = { Rule(letter: letter) } });

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("盘符"));
    }

    [Fact]
    public void MissingSerial_IsWarningWithPrecisionHint()
    {
        var issues = ConfigValidator.Validate(new AppConfig { Rules = { Rule(serial: "") } });

        var warning = Assert.Single(issues, i => i.Severity == ValidationSeverity.Warning);
        Assert.Contains("VID/PID", warning.Message);
    }

    [Fact]
    public void SameTargetLetter_IsCompetitionWarning()
    {
        var issues = ConfigValidator.Validate(new AppConfig
        {
            Rules = { Rule(serial: "SN1"), Rule(serial: "SN2") },
        });

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("先到先得"));
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void DuplicateFingerprint_IsError()
    {
        var issues = ConfigValidator.Validate(new AppConfig
        {
            Rules = { Rule(letter: 'F'), Rule(letter: 'G') },
        });

        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("同一设备指纹"));
    }
}

/// <summary>换 USB 口导致 PnP 父设备树漂移时的规则匹配行为。</summary>
public class PortChangeMatchingTests
{
    private static AppConfig Config(params DriveRule[] rules) => new() { Rules = rules.ToList() };

    private static DeviceIdentity Device(string vid, string pid, string serial)
        => new() { Vid = vid, Pid = pid, Serial = serial };

    [Fact]
    public void ExactFingerprint_StillWins()
    {
        var rule = new DriveRule { Vid = "2109", Pid = "0715", Serial = "000000136623", TargetLetter = 'D' };
        var other = new DriveRule { Vid = "152D", Pid = "0583", Serial = "A1B2C3D4E5", TargetLetter = 'M' };

        Assert.Same(rule, Config(rule, other).FindMatch(Device("2109", "0715", "000000136623")));
    }

    [Fact]
    public void VidPidDrift_FallsBackToStableSerial()
    {
        // 同一块 UAS 硬盘盒换到另一个 USB 控制器后，父设备树上取到的 VID/PID 变了，
        // 但 SCSI INQUIRY 上报的序列号与端口无关 —— 仍应命中 D: 规则。
        var rule = new DriveRule { Vid = "2109", Pid = "0715", Serial = "000000136623", TargetLetter = 'D' };

        Assert.Same(rule, Config(rule).FindMatch(Device("8086", "464E", "000000136623")));
    }

    [Fact]
    public void PlaceholderSerial_DoesNotFallback()
    {
        // 桥接芯片固件默认的占位串不是唯一身份，不能靠它跨端口认设备。
        var rule = new DriveRule { Vid = "152D", Pid = "0583", Serial = "0123456789ABCDEF", TargetLetter = 'M' };

        Assert.Null(Config(rule).FindMatch(Device("2109", "0715", "0123456789ABCDEF")));
        Assert.Null(Config(rule).FindMatch(Device("2109", "0715", "0000000000000000")));
    }

    [Fact]
    public void AmbiguousStableSerial_DoesNotFallback()
    {
        var a = new DriveRule { Vid = "1111", Pid = "1111", Serial = "SN-STABLE-1", TargetLetter = 'D' };
        var b = new DriveRule { Vid = "2222", Pid = "2222", Serial = "SN-STABLE-1", TargetLetter = 'M' };

        Assert.Null(Config(a, b).FindMatch(Device("3333", "3333", "SN-STABLE-1")));
    }

    [Theory]
    [InlineData("000000136623", true)]
    [InlineData("4E078AD056C9", true)]
    [InlineData("000000000000", false)]
    [InlineData("0123456789ABCDEF", false)]
    [InlineData("6&29B12556", false)]
    [InlineData("ABC", false)]
    [InlineData(null, false)]
    public void StableSerial_RejectsPlaceholders(string? raw, bool expected)
        => Assert.Equal(expected, SerialRules.IsStable(raw));
}
