using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;
using UsbDriveManager.Core.Wmi;

namespace UsbConfig.Core.Tests;

public class DeviceIdentityTests
{
    [Fact]
    public void StorageInstanceId_ParsesVendorProductAndSerial()
    {
        const string pnp = @"USBSTOR\DISK&VEN_SANDISK&PROD_CRUZER_BLADE&REV_1.00\4C530001234567890123&0";

        var (vendor, product, serial) = PnpIdParser.FromStorageInstanceId(pnp);

        Assert.Equal("SANDISK", vendor);
        Assert.Equal("CRUZER_BLADE", product);
        Assert.Equal("4C530001234567890123", serial);
    }

    [Theory]
    [InlineData("ABC&0", "ABC")]
    [InlineData("ABC", "ABC")]
    [InlineData("  ABC  ", "ABC")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeSerial_StripsTrailingAmpZero(string? raw, string expected)
        => Assert.Equal(expected, SerialRules.Normalize(raw));

    [Theory]
    [InlineData("ABC123", true)]
    [InlineData("000000000000", false)]
    [InlineData("0", false)]
    [InlineData("[", false)]
    [InlineData("6&29B12556", false)] // Windows 生成的总线相对实例号，不是硬件序列号
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SerialUsable_RejectsEmptyAllZeroAndJunk(string? raw, bool expected)
        => Assert.Equal(expected, SerialRules.IsUsable(raw));

    [Fact]
    public void Resolver_WalksToParentUsbNodeForVidPid()
    {
        const string pnp = @"USBSTOR\DISK&VEN_KINGSTON&PROD_DATATRAVELER&REV_PMAP\000000000000&0";
        string? Parent(string id) => id.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)
            ? @"USB\VID_0951&PID_1666\000000000000"
            : null;

        var identity = DeviceIdentityResolver.Resolve(pnp, null, Parent);

        Assert.Equal("0951", identity.Vid);
        Assert.Equal("1666", identity.Pid);
        Assert.Equal("", identity.Serial); // 全零序列号被清空，退化为 VID/PID 匹配
        Assert.False(identity.HasUsableSerial);
    }

    [Fact]
    public void Resolver_WithoutParent_LeavesVidPidEmpty()
    {
        const string pnp = @"USBSTOR\DISK&VEN_GENERIC&PROD_FLASH&REV_1.00&0";

        var identity = DeviceIdentityResolver.Resolve(pnp, null, _ => null);

        Assert.False(identity.HasVidPid);
        Assert.Equal("GENERIC", identity.Vendor);
    }
}

public class DriveLettersTests
{
    [Theory]
    [InlineData('A', false)]
    [InlineData('B', false)]
    [InlineData('C', false)]
    [InlineData('D', true)]
    [InlineData('Z', true)]
    [InlineData('f', true)]
    public void IsAssignable_OnlyAllowsDThroughZ(char letter, bool expected)
        => Assert.Equal(expected, DriveLetters.IsAssignable(letter));

    [Theory]
    [InlineData("F", 'F')]
    [InlineData("f:", 'F')]
    [InlineData(" Z ", 'Z')]
    public void TryNormalize_AcceptsLetterWithOptionalColon(string text, char expected)
    {
        Assert.True(DriveLetters.TryNormalize(text, out var letter));
        Assert.Equal(expected, letter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("1")]
    [InlineData(":")]
    public void TryNormalize_RejectsGarbage(string text)
        => Assert.False(DriveLetters.TryNormalize(text, out _));
}
