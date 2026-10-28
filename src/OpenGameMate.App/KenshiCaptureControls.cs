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
            "仅捕获已选择的 Kenshi 窗口，不捕获整个桌面。此版本只按需发送。",
            "Captures only the selected Kenshi window, even with GameMate in front. This version sends only on request; it never falls back to the desktop.");
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
            AddActivity(T("已选择 Kenshi 窗口；只按需捕获。", "Kenshi window selected; capture is on request only."));
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
            T("仅在点击立即发送或按快捷键时，捕获已选择的 Kenshi 窗口并发送到当前 ChatGPT 对话。游戏内显示的私人内容也可能被捕获。请保持游戏窗口打开且不要最小化；本版本不会自动截图。是否开始？",
                "Send now or the hotkey will capture only the selected Kenshi window and send it to the current ChatGPT chat. Private content displayed inside the game can be included. Keep the game open and not minimized. Automatic capture is disabled. Start?"),
            "Dragonic Mate - Kenshi window", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
        return _kenshiConsentConfirmed;
    }
}
