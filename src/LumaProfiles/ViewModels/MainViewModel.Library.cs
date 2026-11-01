using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;
using LumaProfiles.Services;


namespace LumaProfiles.ViewModels;

/// <summary>Profile library operations: favorites, import/export, saved corrections and live preview.</summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, DisplayProfile> _savedAdjustments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Applies the next/previous profile, preferring favorites when there are any.</summary>
    public void CycleProfile(int direction)
    {
        var pool = Favorites.ToList();
        if (pool.Count == 0) pool = Profiles.ToList();
        var current = Profiles.FirstOrDefault(item => item.IsActive)?.Id ?? SelectedProfile.Id;
        if (Cycle(pool, current, direction) is not { } next) return;
        SelectedProfile = next;
        _ = EnqueueUserTriggered(next);
    }

    internal static DisplayProfile? Cycle(IReadOnlyList<DisplayProfile> pool, string? currentId, int direction)
    {
        if (pool.Count == 0) return null;
        var index = pool.ToList().FindIndex(item => item.Id == currentId);
        var next = index < 0
            ? (direction >= 0 ? 0 : pool.Count - 1)
            : (((index + direction) % pool.Count) + pool.Count) % pool.Count;
        return pool[next];
    }

    public void ApplyProfileById(string profileId)
    {
        if (FindProfile(profileId) is not { } profile) return;
        SelectedProfile = profile;
        var target = _settings.ProfileHotkeys
            .FirstOrDefault(hotkey => hotkey.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase))?.Target;
        _ = EnqueueUserTriggered(profile, target);
    }

    /// <summary>
    /// A profile the user asked for through a hotkey or the tray menu. Like a click in the window it asks
    /// "keep changes?" (unless turned off), because a bad profile applied blindly can leave the screen unreadable.
    /// The schedule and per-app rules cannot ask: nobody is there to answer.
    /// </summary>
    private Task EnqueueUserTriggered(DisplayProfile profile, string? target = null)
    {
        var detail = L("ConfirmDetailApply", profile.DisplayName);
        return Enqueue(() => RunWithConfirmationAsync(() => ApplyAsync(profile, target: target), detail, L("Applying", profile.DisplayName)));
    }

    private DisplayProfile? FindProfile(string id) =>
        Profiles.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void ToggleFavorite(DisplayProfile profile)
    {
        profile.IsFavorite = !profile.IsFavorite;
        SaveProfiles();
        StatusMessage = L(profile.IsFavorite ? "FavoriteAdded" : "FavoriteRemoved", profile.DisplayName);
        if (_favoritesOnly) OnFiltersChanged();
    }

    private void RestoreDefaults()
    {
        var restored = _profileStore.GetDefault(SelectedProfile.Id);
        SelectedProfile.CopyAdjustmentsFrom(restored);
        CommitAdjustments(SelectedProfile);
        SaveProfiles();
        StatusMessage = L("Restored", SelectedProfile.DisplayName);
    }

    private void RemoveCorrectionsFor(ApplyResult result)
    {
        foreach (var monitor in result.AppliedMonitors)
        {
            _settings.MonitorCorrections.RemoveAll(item =>
                item.MonitorId.Equals(monitor.MonitorId, StringComparison.OrdinalIgnoreCase) ||
                item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void SaveMonitorCorrections(DisplayProfile profile, ApplyResult result, bool applyImageControls)
    {
        foreach (var monitor in result.AppliedMonitors)
        {
            _settings.MonitorCorrections.RemoveAll(item =>
                item.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            _settings.MonitorCorrections.Add(MonitorService.CreateCorrection(profile, monitor, applyImageControls));
        }
        SaveSettings();
    }

    private void ScheduleLivePreview()
    {
        if (!_isLivePreviewEnabled) return;
        _livePreviewTimer.Stop();
        _livePreviewTimer.Start();
    }

    private void SchedulePersistentCorrectionReapply(bool immediate = false)
    {
        if (_settings.MonitorCorrections.Count == 0) return;
        _reapplyTimer.Stop();
        if (immediate)
        {
            _ = Enqueue(ReapplyPersistentCorrectionsAsync, visible: false);
        }
        else
        {
            _reapplyTimer.Start();
        }
    }

    private async Task ReapplyPersistentCorrectionsAsync()
    {
        if (_isReapplyingCorrections)
        {
            _reapplyRequested = true;
            return;
        }

        _isReapplyingCorrections = true;
        var corrections = _settings.MonitorCorrections.ToArray();
        var cts = new CancellationTokenSource();
        _reapplyCts = cts;
        try
        {
            var result = await Task.Run(() => _monitor.Reapply(corrections, cts.Token), cts.Token);
            if (result.DisplayCount > 0)
            {
                StatusMessage = FormatResult(T("PersistentCorrectionRestored"), result);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer user action superseded this re-application.
        }
        catch (Exception exception)
        {
            AppLog.Error("Re-applying persistent corrections failed.", exception);
            StatusMessage = L("PersistentCorrectionFailed", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_reapplyCts, cts)) _reapplyCts = null;
            cts.Dispose();
            _isReapplyingCorrections = false;
            if (_reapplyRequested)
            {
                _reapplyRequested = false;
                SchedulePersistentCorrectionReapply();
            }
        }
    }

    private void ExportProfiles()
    {
        try
        {
            var path = _shell.PickSaveFile(T("ExportProfiles"), "luma-profiles.json");
            if (path is null) return;
            _profileStore.Export(path, Profiles, BuildAutomationBackup());
            StatusMessage = L("ProfilesExported", System.IO.Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            AppLog.Error("Exporting profiles failed.", exception);
            StatusMessage = L("ProfilesTransferFailed", exception.Message);
        }
    }

    private void ImportProfiles()
    {
        try
        {
            var path = _shell.PickOpenFile(T("ImportProfiles"));
            if (path is null) return;
            var updated = _profileStore.Import(path, Profiles);
            foreach (var profile in Profiles) CommitAdjustments(profile);
            SaveProfiles();
            if (_profileStore.ReadAutomation(path) is { } automation) ApplyAutomationBackup(automation);
            RefreshProfileCatalog();
            StatusMessage = L("ProfilesImported", updated);
        }
        catch (Exception exception)
        {
            AppLog.Error("Importing profiles failed.", exception);
            StatusMessage = L("ProfilesTransferFailed", exception.Message);
        }
    }

    /// <summary>Marks the profile's current slider values as the ones to keep on disk.</summary>
    private void CommitAdjustments(DisplayProfile profile) => _savedAdjustments[profile.Id] = profile.Clone();

    /// <summary>
    /// The profiles as they should be written: values tried with "Apply" alone stay on screen for this
    /// session but the file keeps the last saved ones, so a one-off tweak (dimmer at night) is not remembered.
    /// </summary>
    private IEnumerable<DisplayProfile> ProfilesToPersist()
    {
        foreach (var profile in Profiles)
        {
            if (!_savedAdjustments.TryGetValue(profile.Id, out var saved))
            {
                CommitAdjustments(profile);
                yield return profile;
            }
            else if (profile.HasSameAdjustmentsAs(saved))
            {
                yield return profile;
            }
            else
            {
                var persisted = profile.Clone();
                persisted.CopyAdjustmentsFrom(saved);
                yield return persisted;
            }
        }
    }

    private void SaveProfiles()
    {
        try
        {
            _profileStore.Save(ProfilesToPersist().ToList());
        }
        catch (Exception exception)
        {
            AppLog.Error("Saving profiles failed.", exception);
            StatusMessage = T("SettingsSaveFailed");
        }
    }

    private AutomationBackup BuildAutomationBackup() => new()
    {
        Schedule = _settings.Schedule,
        AppRules = _settings.AppRules,
        ProfileHotkeys = _settings.ProfileHotkeys,
        GlobalHotkeysEnabled = _settings.GlobalHotkeysEnabled,
        BatteryProfileEnabled = _settings.BatteryProfileEnabled,
        BatteryProfileId = _settings.BatteryProfileId,
        GameModeEnabled = _settings.GameModeEnabled,
        GameModeProfileId = _settings.GameModeProfileId,
    };

    /// <summary>Replaces the schedule, rules and shortcuts with an imported set, dropping anything that points at an unknown profile.</summary>
    private void ApplyAutomationBackup(AutomationBackup backup)
    {
        bool Known(string id) => FindProfile(id) is not null;

        if (backup.Schedule is { } schedule)
        {
            schedule.Entries = schedule.EnsureEntries().Where(entry => Known(entry.ProfileId) && IsValidScheduleEntry(entry)).ToList();
            schedule.TransitionSeconds = NearestTransition(schedule.TransitionSeconds);
            if (schedule.Latitude is not null and (< -90 or > 90)) schedule.Latitude = null;
            if (schedule.Longitude is not null and (< -180 or > 180)) schedule.Longitude = null;
            _settings.Schedule = schedule;
        }

        _settings.AppRules = backup.AppRules.Where(rule => Known(rule.ProfileId) && AppRuleEngine.Normalize(rule.ProcessName).Length > 0).ToList();
        _settings.ProfileHotkeys = backup.ProfileHotkeys
            .Where(hotkey => Known(hotkey.ProfileId) && HotkeyService.TryParseKey(hotkey.Key, out _))
            .GroupBy(hotkey => hotkey.Key)
            .Select(group => group.Last())
            .ToList();
        GlobalHotkeysEnabled = backup.GlobalHotkeysEnabled;
        // Files from before 0.10 carry no battery or game mode: keep what is configured here.
        if (backup.BatteryProfileId is { } batteryId && Known(batteryId))
        {
            BatteryProfileId = batteryId;
            BatteryProfileEnabled = backup.BatteryProfileEnabled;
        }

        if (backup.GameModeProfileId is { } gameId && Known(gameId))
        {
            GameModeProfileId = gameId;
            GameModeEnabled = backup.GameModeEnabled;
        }

        SaveSettings();
        _ruleEngine.Reset();
        RefreshAutomationLists();
        foreach (var property in new[] { nameof(ScheduleEnabled), nameof(ScheduleLatitude), nameof(ScheduleLongitude), nameof(ScheduleTransitionSeconds) })
        {
            Raise(property);
        }

        RestartSchedule();
    }

    private static bool IsValidScheduleEntry(ScheduleEntry entry) => entry.Sun == SunEvent.None
        ? ScheduleResolver.TryParseTime(entry.Time, out _)
        : Math.Abs(entry.OffsetMinutes) <= MaxSunOffsetMinutes;
}
