namespace LumaProfiles.ViewModels;

/// <summary>One selectable accent: the brand color plus readable text/hover variants for both themes.</summary>
public sealed record AccentOption(string Key, string Accent, string TextOnDark, string TextOnLight, string Hover);

public static class AccentPalette
{
    public static IReadOnlyList<AccentOption> All { get; } =
    [
        new("Cyan", "#55D6F5", "#8EDFF2", "#087B95", "#83E5FA"),
        new("Indigo", "#7B6CF6", "#B3AAFF", "#4F40D0", "#9A8DFF"),
        new("Blue", "#3B82F6", "#93BBFF", "#1D5FD1", "#6FA0FA"),
        new("Emerald", "#34D399", "#86EFC4", "#047857", "#6EE7B7"),
        new("Rose", "#FB7185", "#FDA4B0", "#BE123C", "#FDA4AF"),
        new("Amber", "#F59E0B", "#FCD34D", "#B45309", "#FBBF24")
    ];

    public static AccentOption Default => All[0];

    public static AccentOption? Find(string? key) =>
        All.FirstOrDefault(option => option.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static AccentOption Get(string? key) => Find(key) ?? Default;
}

/// <summary>Bindable swatch for the accent picker; selecting it notifies the view model.</summary>
public sealed class AccentChoice : ObservableObject
{
    private readonly Action<string> _onSelected;
    private string _name;
    private bool _isSelected;

    public AccentChoice(AccentOption option, string name, bool isSelected, Action<string> onSelected)
    {
        Option = option;
        _name = name;
        _isSelected = isSelected;
        _onSelected = onSelected;
    }

    public AccentOption Option { get; }
    public string Key => Option.Key;
    public string Color => Option.Accent;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            if (value) _onSelected(Key);
        }
    }

    /// <summary>Updates the highlight without echoing the change back to the view model.</summary>
    internal void SetSelectedSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }
}
