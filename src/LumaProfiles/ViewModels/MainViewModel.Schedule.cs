using System.Collections.ObjectModel;
using System.Globalization;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

public sealed record ScheduleEntryItem(ScheduleEntry Entry, string ProfileName, string Label);

public sealed record TransitionOption(int Seconds, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>Automatic schedule: fixed times and sunrise/sunset slots, each with a profile, faded in smoothly.</summary>
public sealed partial class MainViewModel
{
    private const string FixedKind = "Fixed";
    private const int MaxSunOffsetMinutes = 720;
    private static readonly int[] TransitionChoices = [0, 5, 15, 30, 60, 300];

    private string _newScheduleTime = "12:00";
    private string _newScheduleKind = FixedKind;
    private string _newScheduleOffset = "0";
    private string? _newScheduleProfileId;
    private CancellationTokenSource? _transitionCts;

    public ObservableCollection<LocalizedOption> ScheduleKindOptions { get; } = [];

    public ObservableCollection<TransitionOption> TransitionOptions { get; } = [];

    /// <summary>Waits between fade steps; replaced in tests so they do not sleep.</summary>
    internal Func<TimeSpan, CancellationToken, Task> TransitionDelay { get; set; } = Task.Delay;

    public bool ScheduleEnabled
    {
        get => _settings.Schedule.Enabled;
        set
        {
            if (_settings.Schedule.Enabled == value) return;
            _settings.Schedule.Enabled = value;
            SaveSettings();
            Raise();
            RestartSchedule();
        }
    }

    public int ScheduleTransitionSeconds
    {
        get => NearestTransition(_settings.Schedule.TransitionSeconds);
        set
        {
            if (_settings.Schedule.TransitionSeconds == value) return;
            _settings.Schedule.TransitionSeconds = value;
            SaveSettings();
            Raise();
        }
    }

    public string ScheduleLatitude
    {
        get => FormatCoordinate(_settings.Schedule.Latitude);
        set => SetCoordinate(value, 90, coordinate => _settings.Schedule.Latitude = coordinate, nameof(ScheduleLatitude));
    }

    public string ScheduleLongitude
    {
        get => FormatCoordinate(_settings.Schedule.Longitude);
        set => SetCoordinate(value, 180, coordinate => _settings.Schedule.Longitude = coordinate, nameof(ScheduleLongitude));
    }

    public string NewScheduleTime { get => _newScheduleTime; set => Set(ref _newScheduleTime, value); }

    public string NewScheduleOffset { get => _newScheduleOffset; set => Set(ref _newScheduleOffset, value); }

    public string? NewScheduleProfileId { get => _newScheduleProfileId; set => Set(ref _newScheduleProfileId, value); }

    public string NewScheduleKind
    {
        get => _newScheduleKind;
        set
        {
            if (!Set(ref _newScheduleKind, value)) return;
            Raise(nameof(NewScheduleIsFixed));
            Raise(nameof(NewScheduleIsSun));
        }
    }

    public bool NewScheduleIsFixed => _newScheduleKind == FixedKind;

    public bool NewScheduleIsSun => !NewScheduleIsFixed;

    private static int NearestTransition(int seconds) =>
        TransitionChoices.OrderBy(choice => Math.Abs(choice - seconds)).First();

    private static string FormatCoordinate(double? coordinate) =>
        coordinate?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    private void SetCoordinate(string text, double limit, Action<double?> assign, string propertyName)
    {
        double? coordinate = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || Math.Abs(parsed) > limit)
            {
                StatusMessage = L("ScheduleInvalidLocation", limit);
                Raise(propertyName);
                return;
            }

            coordinate = parsed;
        }

        assign(coordinate);
        SaveSettings();
        Raise(propertyName);
        RefreshScheduleEntries();
        RestartSchedule();
    }

    private void RefreshScheduleOptions()
    {
        ScheduleKindOptions.Clear();
        ScheduleKindOptions.Add(new LocalizedOption(FixedKind, T("ScheduleFixedTime")));
        ScheduleKindOptions.Add(new LocalizedOption(nameof(SunEvent.Sunrise), T("ScheduleSunrise")));
        ScheduleKindOptions.Add(new LocalizedOption(nameof(SunEvent.Sunset), T("ScheduleSunset")));
        Raise(nameof(NewScheduleKind));

        TransitionOptions.Clear();
        foreach (var seconds in TransitionChoices) TransitionOptions.Add(new TransitionOption(seconds, TransitionLabel(seconds)));
        Raise(nameof(ScheduleTransitionSeconds));
    }

    private string TransitionLabel(int seconds) => seconds switch
    {
        0 => T("TransitionOff"),
        >= 60 => L("TransitionMinutes", seconds / 60),
        _ => L("TransitionSeconds", seconds),
    };

    private void RestartSchedule()
    {
        _lastScheduleEntry = null;
        EvaluateSchedule();
    }

    /// <summary>Applies the entry in effect once per boundary crossing so manual changes are not overridden.</summary>
    internal void EvaluateSchedule()
    {
        if (!_settings.Schedule.Enabled || _ruleEngine.ActiveRule is not null) return;

        var entry = ScheduleResolver.Resolve(_settings.Schedule, DateTime.Now);
        if (entry is null || ReferenceEquals(entry, _lastScheduleEntry)) return;
        _lastScheduleEntry = entry;

        if (FindProfile(entry.ProfileId) is not { } profile) return;
        var scheduleStatus = L("ScheduleApplied", profile.DisplayName);
        _ = EnqueueAutomation(() => ApplyScheduledAsync(profile, scheduleStatus));
    }

    private void AddScheduleEntry()
    {
        if (string.IsNullOrWhiteSpace(NewScheduleProfileId)) return;
        if (BuildScheduleEntry() is not { } entry) return;

        var entries = _settings.Schedule.EnsureEntries();
        entries.RemoveAll(existing => existing.Sun == entry.Sun && (entry.Sun != SunEvent.None || existing.Time == entry.Time));
        entries.Add(entry);
        SaveSettings();
        RefreshScheduleEntries();
        RestartSchedule();
        if (entry.Sun != SunEvent.None && !HasScheduleLocation) StatusMessage = T("ScheduleNeedsLocationHint");
    }

    private bool HasScheduleLocation => _settings.Schedule.Latitude is not null && _settings.Schedule.Longitude is not null;

    /// <summary>Reads the "add slot" form; null (with a status message) when the time or offset is not valid.</summary>
    private ScheduleEntry? BuildScheduleEntry()
    {
        var profileId = NewScheduleProfileId!;
        if (NewScheduleIsFixed)
        {
            if (!ScheduleResolver.TryParseTime(NewScheduleTime, out var time))
            {
                StatusMessage = T("ScheduleInvalidTime");
                return null;
            }

            return new ScheduleEntry { Time = time.ToString("HH:mm", CultureInfo.InvariantCulture), ProfileId = profileId };
        }

        if (!int.TryParse(NewScheduleOffset.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var offset)
            || Math.Abs(offset) > MaxSunOffsetMinutes)
        {
            StatusMessage = L("ScheduleInvalidOffset", MaxSunOffsetMinutes);
            return null;
        }

        var sun = Enum.Parse<SunEvent>(NewScheduleKind);
        return new ScheduleEntry { Sun = sun, OffsetMinutes = offset, ProfileId = profileId };
    }

    private void RemoveScheduleEntry(ScheduleEntryItem item)
    {
        _settings.Schedule.EnsureEntries().Remove(item.Entry);
        SaveSettings();
        RefreshScheduleEntries();
        RestartSchedule();
    }

    private void RefreshScheduleEntries()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);

        ScheduleEntries.Clear();
        var timed = _settings.Schedule.EnsureEntries()
            .Select(entry => (Entry: entry, Time: ScheduleResolver.EntryTime(_settings.Schedule, entry, date, offset)))
            .Where(item => item.Time is not null || item.Entry.Sun != SunEvent.None)
            .OrderBy(item => item.Time is null)
            .ThenBy(item => item.Time);
        foreach (var (entry, time) in timed)
        {
            var name = FindProfile(entry.ProfileId)?.DisplayName ?? entry.ProfileId;
            ScheduleEntries.Add(new ScheduleEntryItem(entry, name, ScheduleEntryLabel(entry, time)));
        }
    }

    private string ScheduleEntryLabel(ScheduleEntry entry, TimeOnly? today)
    {
        if (entry.Sun == SunEvent.None) return entry.Time;

        var name = T(entry.Sun == SunEvent.Sunrise ? "ScheduleSunrise" : "ScheduleSunset");
        var shift = entry.OffsetMinutes == 0 ? string.Empty : $" {entry.OffsetMinutes.ToString("+#;-#", CultureInfo.InvariantCulture)} min";
        var when = today is { } time ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : T("ScheduleNeedsLocation");
        return $"{name}{shift} ({when})";
    }

    // ---- smooth transition -----------------------------------------------------------------

    private void CancelTransition() => _transitionCts?.Cancel();

    /// <summary>Fades from the active profile to <paramref name="profile"/> before applying it, unless a newer action interrupts.</summary>
    private async Task ApplyScheduledAsync(DisplayProfile profile, string status)
    {
        var from = Profiles.FirstOrDefault(item => item.IsActive);
        var seconds = _settings.Schedule.TransitionSeconds;
        if (seconds > 0 && from is not null && !from.HasSameAdjustmentsAs(profile)
            && !await TransitionAsync(from.Clone(), profile, seconds))
        {
            return;
        }

        await ApplyAsync(profile, status);
    }

    /// <returns>False when a newer action cancelled the fade, in which case the final profile must not be applied.</returns>
    private async Task<bool> TransitionAsync(DisplayProfile from, DisplayProfile to, int seconds)
    {
        using var cts = new CancellationTokenSource();
        _transitionCts = cts;
        try
        {
            // Every step is a DDC/CI write, so long fades use fewer, larger steps.
            var steps = Math.Clamp(seconds, 2, 30);
            var interval = TimeSpan.FromSeconds((double)seconds / steps);
            var target = SelectedMonitorTarget;
            StatusMessage = L("Transitioning", to.DisplayName);

            for (var step = 1; step < steps; step++)
            {
                var frame = ProfileBlend.Between(from, to, (double)step / steps);
                await _runner.RunAsync(() => _monitor.Preview(frame, target));
                await TransitionDelay(interval, cts.Token);
            }

            return !cts.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_transitionCts, cts)) _transitionCts = null;
        }
    }
}
