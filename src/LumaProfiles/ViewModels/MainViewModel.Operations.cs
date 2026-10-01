using System.Windows.Input;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>Runs blocking work (DDC/CI writes can take seconds) off the UI thread.</summary>
public interface IWorkRunner
{
    Task<T> RunAsync<T>(Func<T> work);
}

public sealed class ThreadPoolWorkRunner : IWorkRunner
{
    public Task<T> RunAsync<T>(Func<T> work) => Task.Run(work);
}

/// <summary>
/// Display changes are queued and executed one at a time in the background while the UI stays responsive.
/// <see cref="IsBusy"/> drives the progress indicator and disables the action buttons.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Queue<Func<Task>> _operations = new();
    private IWorkRunner _runner = new ThreadPoolWorkRunner();
    private bool _draining;
    private int _visibleOperations;
    private bool _previewInFlight;
    private bool _previewRequestedAgain;

    /// <summary>True while a change the user can see (apply, neutralize, restore, revert) is queued or running.</summary>
    public bool IsBusy => _visibleOperations > 0;

    /// <summary>Queues an operation; awaiting the returned task waits for it to finish.</summary>
    private Task Enqueue(Func<Task> operation, bool visible = true)
    {
        var completion = new TaskCompletionSource();
        if (visible)
        {
            CancelTransition();
            _visibleOperations++;
            RaiseBusy();
        }

        _operations.Enqueue(async () =>
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                AppLog.Error("A queued display operation failed.", exception);
                StatusMessage = L("OperationFailed", exception.Message);
            }
            finally
            {
                if (visible)
                {
                    _visibleOperations--;
                    RaiseBusy();
                }

                completion.SetResult();
            }
        });

        if (!_draining) _ = DrainAsync();
        return completion.Task;
    }

    /// <summary>For changes made by tray, hotkeys, schedule and app rules: they accept any pending confirmation.</summary>
    private Task EnqueueAutomation(Func<Task> operation) => Enqueue(async () =>
    {
        CommitPending();
        await operation();
    });

    private async Task DrainAsync()
    {
        _draining = true;
        try
        {
            while (_operations.TryDequeue(out var next)) await next();
        }
        finally
        {
            _draining = false;
        }
    }

    private void RaiseBusy()
    {
        Raise(nameof(IsBusy));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---- display operations ----------------------------------------------------------------

    private async Task ApplyAsync(DisplayProfile profile, string? statusOverride = null)
    {
        CancelPendingReapply();
        StatusMessage = L("Applying", profile.DisplayName);
        var knownIds = _settings.OriginalMonitorStates.Select(item => item.MonitorId).ToList();
        var needsPlan = string.IsNullOrWhiteSpace(_settings.OriginalPowerPlan);
        var target = SelectedMonitorTarget;

        var (captured, plan, result) = await _runner.RunAsync(() => (
            _monitor.CaptureOriginalStates(knownIds),
            needsPlan ? _monitor.GetActivePowerPlan() : null,
            _monitor.Apply(profile, target)));

        StoreOriginalState(captured, plan);
        foreach (var item in Profiles) item.IsActive = false;
        profile.IsActive = result.DisplayCount > 0;
        SaveProfiles();
        SaveMonitorCorrections(profile, result, applyImageControls: true);
        _lastChangeApplied = result.DisplayCount > 0;
        StatusMessage = FormatResult(statusOverride ?? L("Applied", profile.DisplayName), result);
        if (profile.IsHdr) StatusMessage += T("HdrReminder");
    }

    /// <summary>
    /// Returns the displays to their natural Windows/monitor state: the settings captured before this app
    /// changed anything. Falls back to a neutral RGB profile when no original state was captured.
    /// </summary>
    private async Task NeutralizeAsync()
    {
        CancelPendingReapply();
        StatusMessage = T("Neutralizing");
        var originals = _settings.OriginalMonitorStates.ToList();
        var target = SelectedMonitorTarget;

        var (result, usedFallback) = await _runner.RunAsync(() =>
        {
            var restored = _monitor.RestoreOriginal(originals, target, originalPowerPlan: null);
            return restored.DisplayCount > 0 ? (restored, false) : (_monitor.RestoreNeutral(target), true);
        });

        if (usedFallback)
        {
            SaveMonitorCorrections(MonitorService.NeutralProfile(), result, applyImageControls: false);
        }
        else
        {
            RemoveCorrectionsFor(result);
        }

        foreach (var profile in Profiles) profile.IsActive = false;
        SaveProfiles();
        SaveSettings();
        _lastChangeApplied = result.DisplayCount > 0;
        StatusMessage = FormatResult(T("Neutralized"), result);
    }

    private async Task RestoreOriginalAsync()
    {
        CancelPendingReapply();
        StatusMessage = T("RestoringOriginalState");
        var originals = _settings.OriginalMonitorStates.ToList();
        var target = SelectedMonitorTarget;
        var plan = _settings.OriginalPowerPlan;

        var result = await _runner.RunAsync(() => _monitor.RestoreOriginal(originals, target, plan));

        RemoveCorrectionsFor(result);
        if (result.DisplayCount > 0)
        {
            foreach (var profile in Profiles) profile.IsActive = false;
            SaveProfiles();
        }

        SaveSettings();
        _lastChangeApplied = result.DisplayCount > 0;
        StatusMessage = FormatResult(T("OriginalStateRestored"), result);
    }

    private async Task RestoreAfterRuleAsync()
    {
        var previous = _profileBeforeRule;
        _profileBeforeRule = null;
        if (previous is not null && FindProfile(previous) is { } profile)
        {
            await ApplyAsync(profile, L("RuleRestored", profile.DisplayName));
        }
        else
        {
            await NeutralizeAsync();
        }
    }

    /// <summary>Records the first-ever state of each display so it can be restored later.</summary>
    private async Task CaptureOriginalAsync()
    {
        var knownIds = _settings.OriginalMonitorStates.Select(item => item.MonitorId).ToList();
        var needsPlan = string.IsNullOrWhiteSpace(_settings.OriginalPowerPlan);
        var (captured, plan) = await _runner.RunAsync(() => (
            _monitor.CaptureOriginalStates(knownIds),
            needsPlan ? _monitor.GetActivePowerPlan() : null));
        StoreOriginalState(captured, plan);
    }

    private void StoreOriginalState(IReadOnlyList<OriginalMonitorState> captured, string? powerPlan)
    {
        var changed = false;
        foreach (var state in captured)
        {
            if (_settings.OriginalMonitorStates.Any(item => item.MonitorId == state.MonitorId)) continue;
            _settings.OriginalMonitorStates.Add(state);
            _settings.OriginalCapturedAt ??= DateTime.Now;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.OriginalPowerPlan) && !string.IsNullOrWhiteSpace(powerPlan))
        {
            _settings.OriginalPowerPlan = powerPlan;
            changed = true;
        }

        if (changed)
        {
            _monitor.UseOriginalStates(_settings.OriginalMonitorStates);
            SaveSettings();
            PersistRestorePointCopy();
        }
    }

    private void LivePreviewTick()
    {
        _livePreviewTimer.Stop();
        if (_previewInFlight)
        {
            _previewRequestedAgain = true;
            return;
        }

        _previewInFlight = true;
        _ = Enqueue(PreviewAsync, visible: false);
    }

    private async Task PreviewAsync()
    {
        try
        {
            var profile = SelectedProfile;
            var knownIds = _settings.OriginalMonitorStates.Select(item => item.MonitorId).ToList();
            var needsPlan = string.IsNullOrWhiteSpace(_settings.OriginalPowerPlan);
            var target = SelectedMonitorTarget;

            var (captured, plan, result) = await _runner.RunAsync(() => (
                _monitor.CaptureOriginalStates(knownIds),
                needsPlan ? _monitor.GetActivePowerPlan() : null,
                _monitor.Preview(profile, target)));

            StoreOriginalState(captured, plan);
            StatusMessage = FormatResult(T("LivePreviewApplied"), result);
        }
        finally
        {
            _previewInFlight = false;
            if (_previewRequestedAgain)
            {
                _previewRequestedAgain = false;
                ScheduleLivePreview();
            }
        }
    }
}
