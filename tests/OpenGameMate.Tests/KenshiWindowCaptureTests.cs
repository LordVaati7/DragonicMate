using OpenGameMate.Capture;

namespace OpenGameMate.Tests;

public class KenshiWindowCaptureTests
{
    [Theory]
    [InlineData("kenshi", true)]
    [InlineData("Kenshi_x64", true)]
    [InlineData("KENSHI_X64", true)]
    [InlineData("kenshi_x64-copy", false)]
    [InlineData("notkenshi", false)]
    [InlineData("OpenGameMate.App", false)]
    [InlineData("msedge", false)]
    [InlineData("", false)]
    public void OnlyExactKenshiProcessNamesAreEligible(string name, bool expected)
    {
        Assert.Equal(expected, KenshiWindowCapture.IsKenshiProcess(name));
    }

    [Fact]
    public async Task MissingWindowNeverFallsBackToDisplayOrReturnsAnOldScreenshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenGameMate.Tests", Guid.NewGuid().ToString("N"));
        var capture = new KenshiWindowCapture(directory);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(capture.TemporaryScreenshotPath, "old-synthetic-frame");
        try
        {
            Assert.False(capture.HasSelectedWindow);
            var error = await Assert.ThrowsAsync<ScreenCaptureException>(() => capture.CaptureAsync());
            Assert.Equal(CaptureFailureCode.TargetNotSelected, error.Code);
            Assert.Equal("old-synthetic-frame", await File.ReadAllTextAsync(capture.TemporaryScreenshotPath));
        }
        finally
        {
            capture.DeleteTemporaryScreenshot();
        }
    }

    [Fact]
    public async Task CancelledRequestCannotStartCaptureOrCreateAnImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenGameMate.Tests", Guid.NewGuid().ToString("N"));
        var capture = new KenshiWindowCapture(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture.CaptureAsync(cancellation.Token));
        Assert.False(File.Exists(capture.TemporaryScreenshotPath));
        Assert.False(capture.HasSelectedWindow);
    }
}
