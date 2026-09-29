using System.Collections.ObjectModel;
using System.Windows.Input;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>A toggle button for one connected display in the target selector.</summary>
public sealed class DisplayButton : ObservableObject
{
    private readonly Action<DisplayButton> _onToggled;
    private bool _isSelected;

    public DisplayButton(DisplayInfo display, string tooltip, bool isSelected, Action<DisplayButton> onToggled)
    {
        Display = display;
        Tooltip = tooltip;
        _isSelected = isSelected;
        _onToggled = onToggled;
    }

    public DisplayInfo Display { get; }
    public int Number => Display.Number;
    public string Label => Display.Number.ToString();
    public string Tooltip { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            _onToggled(this);
        }
    }

    /// <summary>Updates the toggle without triggering the selection logic again.</summary>
    internal void SetSelectedSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }
}

/// <summary>Which displays a profile is applied to: all of them, or any subset (e.g. 1 and 3 but not 2).</summary>
public sealed partial class MainViewModel
{
    private const string DisplayTargetPrefix = MonitorService.DisplayTargetPrefix;

    private IReadOnlyList<DisplayInfo> _lastDisplays = [];

    public ObservableCollection<DisplayButton> DisplayButtons { get; } = [];

    public ICommand IdentifyDisplaysCommand { get; private set; } = null!;

    /// <summary>The per-display toggles are only useful when there is more than one display.</summary>
    public bool HasMultipleDisplays => DisplayButtons.Count > 1;

    public bool IsAllDisplaysSelected
    {
        get => DisplayButtons.Count > 0 && DisplayButtons.All(button => button.IsSelected);
        set
        {
            if (value)
            {
                foreach (var button in DisplayButtons) button.SetSelectedSilently(true);
                UpdateTarget();
            }
            else
            {
                // At least one display must stay selected; switching "all" off is a no-op.
                Raise();
            }
        }
    }

    public string SelectedMonitorTarget
    {
        get => _selectedMonitorTarget;
        set
        {
            var numbers = ParseTarget(value);
            foreach (var button in DisplayButtons)
            {
                button.SetSelectedSilently(numbers is null || numbers.Contains(button.Number));
            }

            NormalizeSelection();
            UpdateTarget();
        }
    }

    private void InitializeDisplays()
    {
        IdentifyDisplaysCommand = new RelayCommand(() =>
        {
            StatusMessage = T("IdentifyingDisplays");
            _shell.IdentifyDisplays(_monitor.GetDisplays());
        });
        RefreshDisplays();
    }

    /// <summary>Re-reads the connected displays (start-up, hot-plug, language change) keeping the selection.</summary>
    public void RefreshDisplays(bool force = true)
    {
        IReadOnlyList<DisplayInfo> displays;
        try
        {
            displays = _monitor.GetDisplays();
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not enumerate displays.", exception);
            displays = [];
        }

        if (!force && displays.SequenceEqual(_lastDisplays)) return;
        _lastDisplays = displays;

        var wanted = ParseTarget(_selectedMonitorTarget);
        DisplayButtons.Clear();
        foreach (var display in displays)
        {
            DisplayButtons.Add(new DisplayButton(display, DisplayTooltip(display), wanted is null || wanted.Contains(display.Number), OnDisplayToggled));
        }

        NormalizeSelection();
        UpdateTarget();
        Raise(nameof(HasMultipleDisplays));
    }

    public void OnDisplaysChanged() => RefreshDisplays(force: false);

    private string DisplayTooltip(DisplayInfo display) =>
        L(display.IsPrimary ? "DisplayButtonTipPrimary" : "DisplayButtonTip", display.Number, display.Width, display.Height);

    private void OnDisplayToggled(DisplayButton changed)
    {
        // Turning off the last selected display is not allowed: keep it on.
        if (DisplayButtons.All(button => !button.IsSelected)) changed.SetSelectedSilently(true);
        UpdateTarget();
    }

    /// <summary>When nothing is selected (e.g. a selected display was unplugged) fall back to all displays.</summary>
    private void NormalizeSelection()
    {
        if (DisplayButtons.Count == 0 || DisplayButtons.Any(button => button.IsSelected)) return;
        foreach (var button in DisplayButtons) button.SetSelectedSilently(true);
    }

    private void UpdateTarget()
    {
        var target = BuildTarget();
        Raise(nameof(IsAllDisplaysSelected));
        if (target == _selectedMonitorTarget) return;

        _selectedMonitorTarget = target;
        _settings.SelectedMonitorTarget = target;
        SaveSettings();
        Raise(nameof(SelectedMonitorTarget));
        ScheduleLivePreview();
    }

    private string BuildTarget()
    {
        var selected = DisplayButtons.Where(button => button.IsSelected).Select(button => button.Number).ToList();
        if (DisplayButtons.Count == 0 || selected.Count == DisplayButtons.Count) return BothDisplays;
        return string.Join(", ", selected.Select(number => DisplayTargetPrefix + number));
    }

    /// <summary>Returns the display numbers in a target, or null when it means "all displays" (or is unreadable).</summary>
    internal static HashSet<int>? ParseTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == BothDisplays) return null;

        var numbers = new HashSet<int>();
        foreach (var part in target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith(DisplayTargetPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(part[DisplayTargetPrefix.Length..], out var number))
            {
                numbers.Add(number);
            }
        }

        return numbers.Count == 0 ? null : numbers;
    }
}
