using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenGameMate.Core;

namespace OpenGameMate.App;

public partial class MainWindow
{
    private readonly PeriodicCaptureSchedule _periodicSchedule = new();
    private readonly bool _forceManual;
    private CancellationTokenSource? _periodicPendingCancellation;
    private TimeSpan _periodicInterval = PeriodicCaptureSchedule.DefaultInterval;

    private void PeriodicOptions_Changed(object sender, RoutedEventArgs e) => ChangePeriodicOptions();
    private void PeriodicInterval_Changed(object sender, SelectionChangedEventArgs e) => ChangePeriodicOptions();

    private void ChangePeriodicOptions()
    {
        if (!_loaded || _stateMachine.State is GameMateState.Running or GameMateState.Sending or GameMateState.Paused)
            return;
        _manualOnly = _forceManual || PeriodicCaptureCheckBox.IsChecked != true;
        if (PeriodicIntervalComboBox.SelectedItem is ComboBoxItem { Tag: string minutes } &&
            int.TryParse(minutes, out var value) && value is 1 or 2 or 5 or 10)
            _periodicInterval = TimeSpan.FromMinutes(value);
        _kenshiConsentConfirmed = false;
        ApplyLocalization();
        UpdateUi();
    }

    private void UpdatePeriodicControls(GameMateState state)
    {
        var editable = !_auxiliaryOperation &&
            state is not (GameMateState.Running or GameMateState.Sending or GameMateState.Paused);
        PeriodicCaptureCheckBox.IsEnabled = editable && !_forceManual;
        PeriodicIntervalComboBox.IsEnabled = editable && !_forceManual && !_manualOnly;
        IdleDelayComboBox.IsEnabled = false;
        IdleDelayLabelText.Text = "Quiet wait (fixed)";
        SelectIdleDelayItem(10);
        RunInstructionText.Text = _manualOnly
            ? "Captures on request only. Enable optional periodic captures before Start if needed."
            : $"Every {_periodicInterval.TotalMinutes:0} minutes, then wait for 10 seconds of stable page and ChatGPT output-audio silence. Manual capture remains available.";
        if (state == GameMateState.Ready)
            RunStatusText.Text = "Ready. Periodic captures are off unless explicitly enabled above before Start.";
        if (state == GameMateState.Running)
            RunStatusText.Text = _manualOnly
                ? "Ready: Send now captures only Kenshi. Periodic capture is off."
                : $"Periodic capture enabled: {_periodicInterval.TotalMinutes:0} min + safe idle wait. Send now resets the timer. Pause or Stop suspends it.";
    }

    private void ResetPeriodicTimer()
    {
        _periodicPendingCancellation?.Cancel();
        if (!_manualOnly)
            _periodicSchedule.Reset(DateTimeOffset.UtcNow, _periodicInterval);
    }

    private async Task RunPeriodicCaptureLoopAsync(CancellationToken cancellationToken)
    {
        _periodicSchedule.Reset(DateTimeOffset.UtcNow, _periodicInterval);
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            if (_stateMachine.State != GameMateState.Running ||
                !_periodicSchedule.TryClaim(DateTimeOffset.UtcNow)) continue;
            using var pendingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _periodicPendingCancellation = pendingCancellation;
            try
            {
                await ProcessAutomaticOccurrenceAsync(DateTimeOffset.UtcNow, pendingCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && pendingCancellation.IsCancellationRequested)
            {
                // Manual capture or Pause cancelled only this pending occurrence.
            }
            finally
            {
                _periodicPendingCancellation = null;
                if (_stateMachine.State == GameMateState.Running && !_manualOnly)
                    _periodicSchedule.Reset(DateTimeOffset.UtcNow, _periodicInterval);
            }
        }
    }
}
