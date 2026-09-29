namespace LumaProfiles.ViewModels;

/// <summary>Bindable typeface option; selecting it notifies the view model.</summary>
public sealed class FontChoice : ObservableObject
{
    private readonly Action<string> _onSelected;
    private bool _isSelected;

    public FontChoice(string name, bool isSelected, Action<string> onSelected)
    {
        Name = name;
        _isSelected = isSelected;
        _onSelected = onSelected;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            if (value) _onSelected(Name);
        }
    }

    internal void SetSelectedSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }
}
