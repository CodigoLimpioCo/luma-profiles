using System.Windows.Input;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>"A new version is available": a daily check of the GitHub releases (the Store updates its own build).</summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(20);

    /// <summary>Without a checker (tests) nothing is ever requested from the network.</summary>
    private IUpdateChecker? _updateChecker;
    private UpdateInfo? _availableUpdate;
    private bool _isCheckingUpdates;

    public ICommand CheckUpdatesNowCommand { get; private set; } = null!;
    public ICommand OpenUpdateCommand { get; private set; } = null!;
    public ICommand SkipUpdateCommand { get; private set; } = null!;

    /// <summary>The Microsoft Store build is updated by the Store; the check is only offered elsewhere.</summary>
    public bool CanCheckForUpdates => _updateChecker is not null && !_settingsStore.UsesPackagedStartup;

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value) return;
            _settings.CheckForUpdates = value;
            SaveSettings();
            Raise();
            if (value) _ = CheckForUpdatesAsync(manual: false);
        }
    }

    public bool IsUpdateAvailable => _availableUpdate is not null;

    public string UpdateBadgeText => _availableUpdate is { } update ? L("UpdateBadge", update.Version.ToString(3)) : string.Empty;

    public string UpdateStatusText { get; private set; } = string.Empty;

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set => Set(ref _isCheckingUpdates, value);
    }

    private void InitializeUpdates(IUpdateChecker? checker)
    {
        _updateChecker = checker;
        CheckUpdatesNowCommand = new RelayCommand(() => _ = CheckForUpdatesAsync(manual: true), () => !IsCheckingUpdates);
        OpenUpdateCommand = new RelayCommand(() => _shell.OpenUrl(_availableUpdate?.Url ?? GitHubUpdateChecker.ReleasesPage));
        SkipUpdateCommand = new RelayCommand(SkipUpdate);
    }

    /// <summary>Start-up check: at most once every ~day, and never in the Store build.</summary>
    private void CheckForUpdatesOnStartup()
    {
        if (!CanCheckForUpdates || !_settings.CheckForUpdates) return;
        if (_settings.LastUpdateCheck is { } last && DateTime.Now - last < UpdateCheckInterval) return;
        _ = CheckForUpdatesAsync(manual: false);
    }

    internal async Task CheckForUpdatesAsync(bool manual)
    {
        if (!CanCheckForUpdates || IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        CommandManager.InvalidateRequerySuggested();
        SetUpdateStatus(T("UpdateChecking"));
        try
        {
            var latest = await _updateChecker!.GetLatestAsync();
            _settings.LastUpdateCheck = DateTime.Now;
            SaveSettings();

            if (latest is null)
            {
                SetUpdateStatus(T("UpdateCheckFailed"));
                return;
            }

            var running = GitHubUpdateChecker.TryParseVersion(AppVersion, out var current) ? current : new Version(0, 0, 0);
            var skipped = !manual && latest.Version.ToString(3) == _settings.SkippedUpdateVersion;
            _availableUpdate = latest.Version > running && !skipped ? latest : null;
            SetUpdateStatus(latest.Version > running ? L("UpdateAvailable", latest.Version.ToString(3)) : L("UpdateUpToDate", AppVersion));
            if (_availableUpdate is not null) StatusMessage = L("UpdateAvailable", latest.Version.ToString(3));
            RaiseUpdate();
        }
        finally
        {
            IsCheckingUpdates = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void SkipUpdate()
    {
        if (_availableUpdate is null) return;
        _settings.SkippedUpdateVersion = _availableUpdate.Version.ToString(3);
        SaveSettings();
        _availableUpdate = null;
        RaiseUpdate();
    }

    private void SetUpdateStatus(string text)
    {
        UpdateStatusText = text;
        Raise(nameof(UpdateStatusText));
    }

    private void RaiseUpdate()
    {
        Raise(nameof(IsUpdateAvailable));
        Raise(nameof(UpdateBadgeText));
    }
}
