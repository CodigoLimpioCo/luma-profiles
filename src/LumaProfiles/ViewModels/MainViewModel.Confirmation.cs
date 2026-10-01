using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>
/// "Keep changes?" flow modelled on Windows display settings: a change made from the UI is undone
/// automatically unless the user confirms it within <see cref="ConfirmSeconds"/> seconds.
/// The snapshot is taken before the FIRST unconfirmed change, so several changes in a row all revert
/// to what the user had before them, not to the previous attempt.
/// </summary>
public sealed partial class MainViewModel
{
    public const int ConfirmSeconds = 30;

    private readonly DispatcherTimer _confirmTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ChangeSnapshot? _pending;
    private int _confirmRemaining;
    private string _confirmDetail = string.Empty;
    private bool _lastChangeApplied;
    private CancellationTokenSource? _reapplyCts;

    private sealed record ChangeSnapshot(
        IReadOnlyList<OriginalMonitorState> States,
        string? PowerPlan,
        List<MonitorColorCorrection> Corrections,
        string? ActiveProfileId);

    public ICommand KeepChangesCommand { get; private set; } = null!;
    public ICommand RevertChangesCommand { get; private set; } = null!;

    public bool IsConfirmationPending => _pending is not null;
    public int ConfirmRemainingSeconds => _confirmRemaining;
    public int ConfirmTotalSeconds => ConfirmSeconds;
    public string ConfirmDetail => _confirmDetail;
    public string ConfirmCountdownText => L("ConfirmCountdown", _confirmRemaining);

    /// <summary>Raised when a "keep changes?" question starts, so a window hidden in the tray can come forward.</summary>
    public event EventHandler? ConfirmationStarted;

    public bool ConfirmChanges
    {
        get => _settings.ConfirmChanges;
        set
        {
            if (_settings.ConfirmChanges == value) return;
            if (!value && !_shell.Confirm(T("ConfirmOffTitle"), T("ConfirmOffMessage")))
            {
                // Declined: put the switch back (after the binding has finished writing its value).
                Raise();
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Raise(nameof(ConfirmChanges)));
                return;
            }

            _settings.ConfirmChanges = value;
            if (!value) CommitPending();
            SaveSettings();
            Raise();
            Raise(nameof(ConfirmChangesDisabled));
        }
    }

    /// <summary>True while changes are applied for good without the 30-second safety net.</summary>
    public bool ConfirmChangesDisabled => !_settings.ConfirmChanges;

    private void InitializeConfirmation()
    {
        KeepChangesCommand = new RelayCommand(Keep);
        RevertChangesCommand = new RelayCommand(() => _ = Enqueue(() => RevertAsync(timedOut: false)));
        _confirmTimer.Tick += (_, _) => ConfirmTick();
    }

    /// <summary>Runs a user-initiated display change and, when it took effect, asks the user to keep it.</summary>
    private async Task RunWithConfirmationAsync(Func<Task> change, string detail, string busyText)
    {
        StatusMessage = busyText;
        if (!ConfirmChanges)
        {
            CommitPending();
            await change();
            return;
        }

        // Reuse the snapshot of an unconfirmed change so "revert" goes back to the original state.
        var snapshot = _pending ?? await TakeSnapshotAsync();
        _lastChangeApplied = false;
        await change();
        if (_lastChangeApplied) BeginConfirmation(snapshot, detail);
    }

    private async Task<ChangeSnapshot> TakeSnapshotAsync()
    {
        var corrections = _settings.MonitorCorrections.ToList();
        var activeProfileId = Profiles.FirstOrDefault(profile => profile.IsActive)?.Id;
        var (states, plan) = await _runner.RunAsync(() => (_monitor.CaptureCurrentStates(), _monitor.GetActivePowerPlan()));
        return new ChangeSnapshot(states, plan, corrections, activeProfileId);
    }

    private void BeginConfirmation(ChangeSnapshot snapshot, string detail)
    {
        _pending = snapshot;
        _confirmDetail = detail;
        _confirmRemaining = ConfirmSeconds;
        _confirmTimer.Stop();
        _confirmTimer.Start();
        RaiseConfirmation();
        ConfirmationStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Accepts an outstanding change without asking (automation supersedes the question).</summary>
    private void CommitPending()
    {
        if (_pending is null) return;
        StopConfirmation();
    }

    private void Keep()
    {
        if (_pending is null) return;
        StopConfirmation();
        StatusMessage = T("ChangesKept");
    }

    internal void ConfirmTick()
    {
        if (_pending is null)
        {
            _confirmTimer.Stop();
            return;
        }

        _confirmRemaining--;
        if (_confirmRemaining <= 0)
        {
            _confirmTimer.Stop();
            _ = Enqueue(() => RevertAsync(timedOut: true));
            return;
        }

        Raise(nameof(ConfirmRemainingSeconds));
        Raise(nameof(ConfirmCountdownText));
    }

    private async Task RevertAsync(bool timedOut)
    {
        var snapshot = _pending;
        StopConfirmation();
        if (snapshot is null) return;

        CancelPendingReapply();
        StatusMessage = T("Reverting");
        var result = await _runner.RunAsync(() => _monitor.RestoreOriginal(snapshot.States, BothDisplays, snapshot.PowerPlan));
        FinishRevert(snapshot, result, timedOut);
    }

    /// <summary>Synchronous variant used when the application is closing.</summary>
    private void RevertNow()
    {
        var snapshot = _pending;
        StopConfirmation();
        if (snapshot is null) return;

        CancelPendingReapply();
        var result = _monitor.RestoreOriginal(snapshot.States, BothDisplays, snapshot.PowerPlan);
        FinishRevert(snapshot, result, timedOut: false);
    }

    private void FinishRevert(ChangeSnapshot snapshot, ApplyResult result, bool timedOut)
    {
        _settings.MonitorCorrections.Clear();
        _settings.MonitorCorrections.AddRange(snapshot.Corrections);
        foreach (var profile in Profiles) profile.IsActive = profile.Id == snapshot.ActiveProfileId;
        SaveProfiles();
        SaveSettings();
        StatusMessage = FormatResult(T(timedOut ? "ChangesRevertedTimeout" : "ChangesReverted"), result);
    }

    private void StopConfirmation()
    {
        _confirmTimer.Stop();
        _pending = null;
        _confirmRemaining = 0;
        RaiseConfirmation();
    }

    private void RaiseConfirmation()
    {
        Raise(nameof(IsConfirmationPending));
        Raise(nameof(ConfirmRemainingSeconds));
        Raise(nameof(ConfirmCountdownText));
        Raise(nameof(ConfirmDetail));
    }

    private void CancelPendingReapply()
    {
        _reapplyTimer.Stop();
        _reapplyRequested = false;
        _reapplyCts?.Cancel();
    }
}
