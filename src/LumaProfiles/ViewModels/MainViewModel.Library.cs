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
    /// <summary>Applies the next/previous profile, preferring favorites when there are any.</summary>
    public void CycleProfile(int direction)
    {
        var pool = Favorites.ToList();
        if (pool.Count == 0) pool = Profiles.ToList();
        var current = Profiles.FirstOrDefault(item => item.IsActive)?.Id ?? SelectedProfile.Id;
        if (Cycle(pool, current, direction) is not { } next) return;
        SelectedProfile = next;
        _ = EnqueueAutomation(() => ApplyAsync(next));
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
        _ = EnqueueAutomation(() => ApplyAsync(profile));
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
            _profileStore.Export(path, Profiles);
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
            SaveProfiles();
            if (_favoritesOnly) OnFiltersChanged();
            StatusMessage = L("ProfilesImported", updated);
        }
        catch (Exception exception)
        {
            AppLog.Error("Importing profiles failed.", exception);
            StatusMessage = L("ProfilesTransferFailed", exception.Message);
        }
    }

    private void SaveProfiles()
    {
        try
        {
            _profileStore.Save(Profiles);
        }
        catch (Exception exception)
        {
            AppLog.Error("Saving profiles failed.", exception);
            StatusMessage = T("SettingsSaveFailed");
        }
    }
}
