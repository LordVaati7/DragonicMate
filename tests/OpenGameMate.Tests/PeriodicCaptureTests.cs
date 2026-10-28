using OpenGameMate.Core;

namespace OpenGameMate.Tests;

public class PeriodicCaptureTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void DisabledByDefaultAndNoImmediateCaptureAfterStart()
    {
        var schedule = new PeriodicCaptureSchedule();
        Assert.False(schedule.TryClaim(Start));
        schedule.Reset(Start, PeriodicCaptureSchedule.DefaultInterval);
        Assert.False(schedule.TryClaim(Start.AddSeconds(119)));
        Assert.True(schedule.TryClaim(Start.AddMinutes(2)));
        Assert.False(schedule.TryClaim(Start.AddMinutes(2)));
    }

    [Fact]
    public void LongDelayDoesNotCreateCatchUpBurst()
    {
        var schedule = new PeriodicCaptureSchedule();
        schedule.Reset(Start, TimeSpan.FromMinutes(2));
        Assert.True(schedule.TryClaim(Start.AddHours(1)));
        Assert.False(schedule.TryClaim(Start.AddHours(1).AddSeconds(1)));
        Assert.Equal(Start.AddMinutes(62), schedule.NextDueAt);
    }

    [Fact]
    public void ManualResetAndPauseResumeRequireFreshFullIntervals()
    {
        var schedule = new PeriodicCaptureSchedule();
        schedule.Reset(Start, TimeSpan.FromMinutes(2));
        schedule.Reset(Start.AddSeconds(119), TimeSpan.FromMinutes(2));
        Assert.False(schedule.TryClaim(Start.AddMinutes(2)));
        schedule.Stop();
        Assert.False(schedule.TryClaim(Start.AddHours(1)));
        schedule.Reset(Start.AddHours(1), TimeSpan.FromMinutes(2));
        Assert.False(schedule.TryClaim(Start.AddMinutes(61)));
        Assert.True(schedule.TryClaim(Start.AddMinutes(62)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void SupportedIntervalsDoNotFireEarly(int minutes)
    {
        var schedule = new PeriodicCaptureSchedule();
        schedule.Reset(Start, TimeSpan.FromMinutes(minutes));
        Assert.False(schedule.TryClaim(Start.AddMinutes(minutes).AddTicks(-1)));
        Assert.True(schedule.TryClaim(Start.AddMinutes(minutes)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(180)]
    public void UnsupportedIntervalsAreRejected(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodicCaptureSchedule().Reset(Start, TimeSpan.FromSeconds(seconds)));

    private static AdapterIdleProbeResult VoiceProbe() => new(true, 1, 0, 0, false, false,
        "ok", WebAdapterStatus.NotReady, new(AdapterPageState.VoiceComposer, 0, [], AdapterFailureStage.None));

    [Fact]
    public void VoiceRequiresExplicitPeriodicPolicyWhileOriginalPolicyRemainsClosed()
    {
        var probe = VoiceProbe();
        Assert.False(probe.IsSafeToPrepare);
        Assert.True(probe.IsSafeToPrepareForPeriodic);
        Assert.False((probe with { ComposerEmpty = false }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { StopButtonCount = 1 }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { DomainCorrect = false }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { SendButtonCount = 2 }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { SendButtonCount = 1, SendButtonInComposerForm = false }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { Status = WebAdapterStatus.AdapterInvalid }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { Status = WebAdapterStatus.QuotaReached }).IsSafeToPrepareForPeriodic);
        Assert.False((probe with { Diagnostics = probe.Diagnostics with { PageState = AdapterPageState.VoiceComposerWithAttachment } }).IsSafeToPrepareForPeriodic);
    }
}
