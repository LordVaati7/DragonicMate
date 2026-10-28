using System.Windows;
using OpenGameMate.Capture;
using OpenGameMate.Core;
using OpenGameMate.Diagnostics;

namespace OpenGameMate.App;

public partial class MainWindow
{
    private bool _kenshiConsentConfirmed;

    private void ApplyKenshiCaptureLabels()
    {
        Title += " - Kenshi window";
        PrivacySummaryText.Text = T(
            "仅捕获已选择的 Kenshi 窗口。定时截图可选且默认关闭；不会捕获整个桌面。",
            "Captures only the selected Kenshi window. Periodic captures are optional and off by default. No desktop fallback.");
        SelectKenshiButton.Content = T("选择 Kenshi 窗口", "Select Kenshi window");
        KenshiTargetText.Text = _capture.HasSelectedWindow
            ? T("已选择 Kenshi；请保持窗口打开且不要最小化。", "Kenshi selected. Keep the game window open and not minimized.")
            : T("未选择窗口。请以窗口或无边框模式打开 Kenshi，然后选择。", "No window selected. Open Kenshi in windowed or borderless mode, then select it.");
    }

    private void SelectKenshiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stateMachine.State is GameMateState.Running or GameMateState.Sending or GameMateState.Paused ||
            _auxiliaryOperation)
        {
            return;
        }
        _kenshiConsentConfirmed = false;
        try
        {
            _capture.SelectWindow();
            KenshiTargetText.Text = T("已选择 Kenshi；不会捕获 GameMate 或整个桌面。",
                "Kenshi selected. GameMate and the whole desktop are not capture targets.");
            AddActivity(T("已选择 Kenshi 窗口。", "Kenshi window selected."));
            _ = SafeLogAsync("capture.target-selected", DiagnosticLevel.Information, success: true);
        }
        catch (ScreenCaptureException exception)
        {
            KenshiTargetText.Text = $"{exception.Code}: " + T(
                "请打开一个 Kenshi 窗口，恢复窗口后重试。", "Open exactly one Kenshi window, restore it, then select again.");
            _ = SafeLogAsync("capture.target-selection-failed", DiagnosticLevel.Warning,
                errorCode: exception.Code.ToString(), success: false);
        }
        UpdateUi();
    }

    private bool ConfirmKenshiCapture()
    {
        if (!_capture.HasSelectedWindow)
        {
            MessageBox.Show(this, "Select the Kenshi window before starting.", "Dragonic Mate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (_kenshiConsentConfirmed) return true;
        _kenshiConsentConfirmed = MessageBox.Show(this,
            _manualOnly
                ? "Send now or the hotkey captures only the selected Kenshi window and sends it to the current ChatGPT chat. Private content inside the game can be included. Periodic capture is OFF. Keep Kenshi open and not minimized. Start?"
                : $"PERIODIC CAPTURE ENABLED: every {_periodicInterval.TotalMinutes:0} minutes, followed by 10 seconds of stable page and ChatGPT output-audio silence, capture the selected Kenshi window and send it to the current chat. Private content inside the game can be included. Microphone audio is not monitored; this cannot guarantee it detects when you are speaking. Manual capture remains available. Pause or Stop suspends captures. Keep Kenshi open and not minimized. Start?",
            "Dragonic Mate - Kenshi window", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
        return _kenshiConsentConfirmed;
    }
}
