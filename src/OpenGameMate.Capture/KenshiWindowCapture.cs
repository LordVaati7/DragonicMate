using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace OpenGameMate.Capture;

// Selection retains a capture item for one window lifetime. Handles and process
// metadata are not persisted or logged. No game memory, titles, or credentials are read.
public sealed class KenshiWindowCapture : IPrimaryDisplayCapture
{
    private static readonly string[] ProcessNames = ["kenshi_x64", "kenshi"];
    private readonly PrimaryDisplayCapture _engine;
    private GraphicsCaptureItem? _item;
    private nint _window;
    private int _processId;
    private long _processStartTicks;
    private volatile bool _closed;

    public KenshiWindowCapture(string temporaryDirectory)
    {
        _engine = new PrimaryDisplayCapture(temporaryDirectory, GetSelectedItem, ValidateSelectedWindow);
    }

    public string TemporaryScreenshotPath => _engine.TemporaryScreenshotPath;
    public bool HasSelectedWindow => _item is not null && !_closed;

    public static bool IsKenshiProcess(string name) =>
        ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public void SelectWindow()
    {
        // A failed reselection must not leave a previous target armed.
        _item = null;
        _closed = true;
        var candidates = new List<(nint Handle, int ProcessId, long StartTicks)>();
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle != nint.Zero && IsWindowVisible(handle))
                        {
                            candidates.Add((handle, process.Id, process.StartTime.ToUniversalTime().Ticks));
                        }
                    }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                    {
                        // Inaccessible/exiting processes cannot become capture targets.
                    }
                }
            }
        }
        if (candidates.Count != 1)
        {
            throw new ScreenCaptureException(candidates.Count == 0
                ? CaptureFailureCode.TargetUnavailable : CaptureFailureCode.TargetAmbiguous,
                "Open exactly one Kenshi window, then select it again.");
        }
        var candidate = candidates[0];
        if (IsIconic(candidate.Handle))
        {
            throw new ScreenCaptureException(CaptureFailureCode.TargetMinimized,
                "Restore Kenshi before selecting its window.");
        }
        try
        {
            var item = PrimaryDisplayCapture.CreateItem(candidate.Handle, forWindow: true);
            _window = candidate.Handle;
            _processId = candidate.ProcessId;
            _processStartTicks = candidate.StartTicks;
            _closed = false;
            _item = item;
            item.Closed += (sender, _) =>
            {
                if (ReferenceEquals(sender, _item)) _closed = true;
            };
            ValidateSelectedWindow();
        }
        catch (ScreenCaptureException)
        {
            _item = null;
            _closed = true;
            throw;
        }
        catch (Exception exception) when (exception is COMException or Win32Exception or InvalidOperationException)
        {
            _item = null;
            _closed = true;
            throw new ScreenCaptureException(CaptureFailureCode.TargetUnavailable,
                "Windows could not select the Kenshi window.", exception);
        }
    }

    public Task<ScreenCaptureResult> CaptureAsync(CancellationToken cancellationToken = default) =>
        _engine.CaptureAsync(cancellationToken);

    public bool DeleteTemporaryScreenshot() => _engine.DeleteTemporaryScreenshot();

    private GraphicsCaptureItem GetSelectedItem()
    {
        ValidateSelectedWindow();
        return _item!;
    }

    private void ValidateSelectedWindow()
    {
        if (_item is null)
        {
            throw new ScreenCaptureException(CaptureFailureCode.TargetNotSelected,
                "Select the Kenshi window before capturing.");
        }
        if (_closed || !IsWindow(_window) || !IsWindowVisible(_window))
        {
            throw new ScreenCaptureException(CaptureFailureCode.TargetUnavailable,
                "The selected Kenshi window is no longer available.");
        }
        GetWindowThreadProcessId(_window, out var processId);
        try
        {
            using var process = Process.GetProcessById(_processId);
            if (processId != (uint)_processId || !IsKenshiProcess(process.ProcessName) ||
                process.StartTime.ToUniversalTime().Ticks != _processStartTicks ||
                process.MainWindowHandle != _window)
            {
                throw new ScreenCaptureException(CaptureFailureCode.TargetUnavailable,
                    "The selected window identity changed; select Kenshi again.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or Win32Exception or InvalidOperationException)
        {
            throw new ScreenCaptureException(CaptureFailureCode.TargetUnavailable,
                "The selected Kenshi process is no longer available.", exception);
        }
        if (IsIconic(_window))
        {
            throw new ScreenCaptureException(CaptureFailureCode.TargetMinimized,
                "Restore Kenshi; minimized windows are not captured.");
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
