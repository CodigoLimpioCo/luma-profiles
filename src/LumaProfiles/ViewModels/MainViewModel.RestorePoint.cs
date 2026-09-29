using System.Windows.Input;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>
/// The restore point: how the displays were before this app changed anything. It is kept in settings.json and in a
/// separate restore-point.json, and the user can replace it with the current state when the picture looks right.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly RestorePointStore _restorePoints;

    public ICommand SaveRestorePointCommand { get; private set; } = null!;

    public bool HasRestorePoint => _settings.OriginalMonitorStates.Count > 0;

    public string RestorePointText => HasRestorePoint
        ? L("RestorePointInfo", (_settings.OriginalCapturedAt ?? DateTime.Now).ToString("g", System.Globalization.CultureInfo.CurrentCulture), _settings.OriginalMonitorStates.Count)
        : T("RestorePointNone");

    private void InitializeRestorePoint() =>
        SaveRestorePointCommand = new RelayCommand(
            () => _ = Enqueue(SaveRestorePointAsync),
            () => !IsBusy);

    /// <summary>If settings.json was lost or reset, take the restore point back from its own file.</summary>
    private void AdoptRestorePointCopy()
    {
        if (_settings.OriginalMonitorStates.Count > 0)
        {
            PersistRestorePointCopy();
            return;
        }

        if (_restorePoints.Load() is not { } saved) return;
        _settings.OriginalMonitorStates.AddRange(saved.Monitors);
        _settings.OriginalPowerPlan ??= saved.PowerPlan;
        _settings.OriginalCapturedAt ??= saved.CapturedAt;
        SaveSettings();
        AppLog.Info("The restore point was recovered from restore-point.json.");
    }

    private void PersistRestorePointCopy()
    {
        if (_settings.OriginalMonitorStates.Count == 0) return;
        _restorePoints.Save(new RestorePoint
        {
            CapturedAt = _settings.OriginalCapturedAt ?? DateTime.Now,
            PowerPlan = _settings.OriginalPowerPlan,
            Monitors = [.. _settings.OriginalMonitorStates]
        });
        Raise(nameof(HasRestorePoint));
        Raise(nameof(RestorePointText));
    }

    private async Task SaveRestorePointAsync()
    {
        if (!_shell.Confirm(T("RestorePointConfirmTitle"), T("RestorePointConfirmMessage"))) return;

        CommitPending();
        CancelPendingReapply();
        var (states, plan) = await _runner.RunAsync(() => (_monitor.CaptureCurrentStates(), _monitor.GetActivePowerPlan()));
        if (states.Count == 0)
        {
            StatusMessage = T("RestorePointFailed");
            return;
        }

        _settings.OriginalMonitorStates.Clear();
        _settings.OriginalMonitorStates.AddRange(states);
        _settings.OriginalPowerPlan = plan ?? _settings.OriginalPowerPlan;
        _settings.OriginalCapturedAt = DateTime.Now;
        _monitor.UseOriginalStates(_settings.OriginalMonitorStates);
        SaveSettings();
        PersistRestorePointCopy();
        StatusMessage = L("RestorePointSaved", states.Count);
    }
}
