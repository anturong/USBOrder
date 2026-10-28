using UsbDriveManager.Core;
using UsbDriveManager.Core.Models;
using UsbDriveManager.Core.Wmi;

namespace UsbConfig.Core.Tests;

public class AssignmentEngineTests
{
    private sealed class FakeInspector : IVolumeInspector
    {
        public VolumeOccupant Occupant { get; set; } = VolumeOccupant.Free('F');

        public VolumeOccupant Inspect(char letter) => Occupant;
    }

    private sealed class FakeAssigner : ILetterAssigner
    {
        public char? Target { get; private set; }

        public char? Current { get; private set; }

        public string? VolumeId { get; private set; }

        public LetterAssignResult Result { get; set; } = LetterAssignResult.Ok("ok");

        public LetterAssignResult Assign(char targetLetter, char? currentLetter)
        {
            Target = targetLetter;
            Current = currentLetter;
            return Result;
        }

        public LetterAssignResult AssignVolume(string volumeId, char targetLetter)
        {
            Target = targetLetter;
            VolumeId = volumeId;
            return Result;
        }
    }

    private static UsbDiskInfo Disk(char? letter = 'G', string vid = "0951", string pid = "1666", string serial = "SN1")
        => new()
        {
            DeviceId = @"\\.\PHYSICALDRIVE2",
            PnpDeviceId = @"USBSTOR\DISK&VEN_KINGSTON&PROD_DATATRAVELER\SN1&0",
            Model = "Kingston DataTraveler",
            Identity = new DeviceIdentity { Vid = vid, Pid = pid, Serial = serial },
            Partitions = letter is null
                ? Array.Empty<UsbPartition>()
                : new[]
                {
                    new UsbPartition
                    {
                        Letter = letter,
                        VolumeId = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\",
                        Label = "U",
                        SizeBytes = 16UL * 1024 * 1024 * 1024,
                    },
                },
        };

    private static DriveRule Rule(char letter = 'F', string serial = "SN1")
        => new() { Name = "产线 U 盘", Vid = "0951", Pid = "1666", Serial = serial, TargetLetter = letter };

    private static AppConfig Config(params DriveRule[] rules) => new() { Rules = rules.ToList() };

    [Fact]
    public void FreeLetter_Assigns()
    {
        var inspector = new FakeInspector { Occupant = VolumeOccupant.Free('F') };
        var engine = new AssignmentEngine(inspector);

        var decision = engine.Decide(Disk('G'), Config(Rule()));

        Assert.Equal(AssignmentAction.Assign, decision.Action);
        Assert.Equal('F', decision.TargetLetter);
        Assert.True(decision.ShouldAssign);
        Assert.False(decision.NotifyUser);
    }

    [Fact]
    public void AlreadyOnTargetLetter_Skips()
    {
        var engine = new AssignmentEngine(new FakeInspector());

        var decision = engine.Decide(Disk('F'), Config(Rule()));

        Assert.Equal(AssignmentAction.SkipAlreadyAssigned, decision.Action);
        Assert.False(decision.ShouldAssign);
    }

    [Fact]
    public void NoMatchingRule_SkipsSilently()
    {
        var engine = new AssignmentEngine(new FakeInspector());

        var decision = engine.Decide(Disk(serial: "OTHER"), Config(Rule()));

        Assert.Equal(AssignmentAction.SkipNoRule, decision.Action);
        Assert.False(decision.NotifyUser);
    }

    [Fact]
    public void OccupiedByRuleDevice_RefusesAsCompetition_AndNotifies()
    {
        var other = Disk('F', serial: "SN2");
        var inspector = new FakeInspector
        {
            Occupant = new VolumeOccupant
            {
                Letter = 'F',
                Exists = true,
                IsUsbDisk = true,
                DriveType = 2,
                Identity = other.Identity,
                Description = "F: U 盘 · Kingston",
            },
        };

        var config = Config(Rule(serial: "SN1"), Rule(serial: "SN2"));
        var engine = new AssignmentEngine(inspector);

        var decision = engine.Decide(Disk('G', serial: "SN1"), config);

        Assert.Equal(AssignmentAction.RefuseCompetition, decision.Action);
        Assert.True(decision.NotifyUser);
        Assert.Contains("先到先得", decision.Message);
    }

    [Fact]
    public void OccupiedByUnrelatedVolume_RefusesAsConflict_AndNotifies()
    {
        var inspector = new FakeInspector
        {
            Occupant = new VolumeOccupant
            {
                Letter = 'F',
                Exists = true,
                IsUsbDisk = false,
                DriveType = 3,
                Description = "F: 本地磁盘 · ST2000DM008",
            },
        };

        var engine = new AssignmentEngine(inspector);

        var decision = engine.Decide(Disk('G'), Config(Rule()));

        Assert.Equal(AssignmentAction.RefuseConflict, decision.Action);
        Assert.True(decision.NotifyUser);
        Assert.Contains("拒绝分配", decision.Message);
    }

    [Fact]
    public void InvalidTargetLetter_Refuses()
    {
        var engine = new AssignmentEngine(new FakeInspector());

        var decision = engine.Decide(Disk('G'), Config(Rule('C')));

        Assert.Equal(AssignmentAction.RefuseInvalidLetter, decision.Action);
        Assert.False(decision.ShouldAssign);
    }

    [Fact]
    public void Apply_MovesVolumeByVolumeId()
    {
        var inspector = new FakeInspector();
        var assigner = new FakeAssigner();
        var engine = new AssignmentEngine(inspector);
        var disk = Disk('G');

        var decision = engine.Decide(disk, Config(Rule()));
        var result = engine.Apply(decision, assigner);

        Assert.True(result.Success);
        Assert.Equal('F', assigner.Target);
        Assert.Equal(disk.Partitions[0].VolumeId, assigner.VolumeId);
    }

    [Fact]
    public void Apply_OnRefusal_DoesNothing()
    {
        var assigner = new FakeAssigner();
        var engine = new AssignmentEngine(new FakeInspector());
        var decision = engine.Decide(Disk('G', serial: "NOPE"), Config(Rule()));

        var result = engine.Apply(decision, assigner);

        Assert.False(result.Success);
        Assert.Null(assigner.Target);
    }
}
