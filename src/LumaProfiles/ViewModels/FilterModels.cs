namespace LumaProfiles.ViewModels;

/// <summary>A toggleable chip in the advanced filter panel (a category or a color temperature).</summary>
public sealed class FilterChip : ObservableObject
{
    private readonly Action<FilterChip> _onChanged;
    private string _label;
    private int _count;
    private bool _isSelected;

    public FilterChip(string value, string label, int count, Action<FilterChip> onChanged)
    {
        Value = value;
        _label = label;
        _count = count;
        _onChanged = onChanged;
    }

    /// <summary>Stable key matched against the profile (e.g. "Gamer" or "Neutro 6500 K").</summary>
    public string Value { get; }

    public string Label
    {
        get => _label;
        set
        {
            if (Set(ref _label, value)) Raise(nameof(Caption));
        }
    }

    /// <summary>How many profiles of the whole library belong to this chip.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, value)) Raise(nameof(Caption));
        }
    }

    public string Caption => $"{Label}  {Count}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            _onChanged(this);
        }
    }

    internal void SetSelectedSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }
}

/// <summary>A min/max pair on a 0–100 scale; the two ends can never cross.</summary>
public sealed class RangeFilter : ObservableObject
{
    public const double Lowest = 0;
    public const double Highest = 100;

    private readonly Action _onChanged;
    private double _min = Lowest;
    private double _max = Highest;

    public RangeFilter(Action onChanged) => _onChanged = onChanged;

    public double Min
    {
        get => _min;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), Lowest, _max);
            if (Math.Abs(clamped - _min) < 0.01)
            {
                Raise();
                return;
            }

            _min = clamped;
            Raise();
            Raise(nameof(IsActive));
            Raise(nameof(Label));
            _onChanged();
        }
    }

    public double Max
    {
        get => _max;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), _min, Highest);
            if (Math.Abs(clamped - _max) < 0.01)
            {
                Raise();
                return;
            }

            _max = clamped;
            Raise();
            Raise(nameof(IsActive));
            Raise(nameof(Label));
            _onChanged();
        }
    }

    public bool IsActive => _min > Lowest || _max < Highest;

    public string Label => $"{_min:0}–{_max:0}";

    public bool Contains(double value) => value >= _min && value <= _max;

    public void Reset()
    {
        if (!IsActive) return;
        _min = Lowest;
        _max = Highest;
        Raise(nameof(Min));
        Raise(nameof(Max));
        Raise(nameof(IsActive));
        Raise(nameof(Label));
    }
}
