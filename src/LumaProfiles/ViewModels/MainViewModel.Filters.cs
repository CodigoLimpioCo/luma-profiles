using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles.ViewModels;

/// <summary>Advanced search: free text plus category, state, temperature and range filters with sorting.</summary>
public sealed partial class MainViewModel
{
    private const string AllCategoriesKey = "Todos";
    private const string FavoritesKey = "Favoritos";

    private static readonly string[] Temperatures = ["Usuario (RGB)", "Cálido 5000 K", "Neutro 6500 K", "Frío 7500 K"];
    private static readonly string[] TemperatureTextKeys = ["UserRgb", "Warm5000", "Neutral6500", "Cool7500"];

    public static IReadOnlyList<string> SortModes { get; } =
        ["Default", "NameAsc", "NameDesc", "BrightnessDesc", "BrightnessAsc", "ContrastDesc", "SaturationDesc", "Category"];

    private readonly HashSet<string> _filterCategories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _filterTemperatures = new(StringComparer.Ordinal);
    private bool _favoritesOnly;
    private bool _hdrOnly;
    private bool _highPerformanceOnly;
    private bool _modifiedOnly;
    private bool _isFilterPanelOpen;
    private string _sortMode = "Default";
    private string _searchText = string.Empty;
    private bool _filtersReady;

    public ObservableCollection<FilterChip> CategoryChips { get; } = [];
    public ObservableCollection<FilterChip> TemperatureChips { get; } = [];
    public ObservableCollection<LocalizedOption> SortOptions { get; } = [];

    public RangeFilter BrightnessRange { get; private set; } = null!;
    public RangeFilter ContrastRange { get; private set; } = null!;
    public RangeFilter SaturationRange { get; private set; } = null!;

    public ICommand ClearFiltersCommand { get; private set; } = null!;
    public ICommand ToggleFilterPanelCommand { get; private set; } = null!;

    private void InitializeFilters()
    {
        BrightnessRange = new RangeFilter(OnFiltersChanged);
        ContrastRange = new RangeFilter(OnFiltersChanged);
        SaturationRange = new RangeFilter(OnFiltersChanged);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        ToggleFilterPanelCommand = new RelayCommand(() => IsFilterPanelOpen = !IsFilterPanelOpen);
        BuildFilterChips();
        RefreshSortOptions();
        _filtersReady = true;
    }

    // ---- text ------------------------------------------------------------------------------

    public string SearchText
    {
        get => _searchText;
        set
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (!Set(ref _searchText, trimmed)) return;
            if (trimmed.Length > 0) IsSettingsOpen = false;
            OnFiltersChanged();
            StatusMessage = string.IsNullOrWhiteSpace(_searchText)
                ? L("CompleteLibrary", Profiles.Count)
                : L("SearchResults", _searchText);
        }
    }

    // ---- state filters ---------------------------------------------------------------------

    public bool IsFilterPanelOpen { get => _isFilterPanelOpen; set => Set(ref _isFilterPanelOpen, value); }

    public bool FavoritesOnly { get => _favoritesOnly; set => SetFilter(ref _favoritesOnly, value); }
    public bool HdrOnly { get => _hdrOnly; set => SetFilter(ref _hdrOnly, value); }
    public bool HighPerformanceOnly { get => _highPerformanceOnly; set => SetFilter(ref _highPerformanceOnly, value); }
    public bool ModifiedOnly { get => _modifiedOnly; set => SetFilter(ref _modifiedOnly, value); }

    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (!SortModes.Contains(value) || !Set(ref _sortMode, value)) return;
            OnFiltersChanged();
        }
    }

    private void SetFilter(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return;
        OnFiltersChanged();
    }

    // ---- categories (shared with the sidebar) ----------------------------------------------

    /// <summary>The sidebar entry to highlight: a single category, Favoritos, Todos, or none for a custom mix.</summary>
    public string SidebarCategory
    {
        get
        {
            if (_filterCategories.Count == 0) return _favoritesOnly ? FavoritesKey : AllCategoriesKey;
            return _filterCategories.Count == 1 && !_favoritesOnly ? _filterCategories.First() : string.Empty;
        }
    }

    /// <summary>Sidebar click: show one category (or Todos / Favoritos), replacing any category mix.</summary>
    private void SelectCategory(string category)
    {
        IsSettingsOpen = false;
        _filterCategories.Clear();
        foreach (var chip in CategoryChips) chip.SetSelectedSilently(false);

        if (category == FavoritesKey)
        {
            _favoritesOnly = true;
        }
        else
        {
            _favoritesOnly = false;
            if (category != AllCategoriesKey)
            {
                _filterCategories.Add(category);
                CategoryChips.FirstOrDefault(chip => chip.Value == category)?.SetSelectedSilently(true);
            }
        }

        Raise(nameof(FavoritesOnly));
        OnFiltersChanged();
        StatusMessage = category == AllCategoriesKey
            ? L("ShowingProfiles", Profiles.Count)
            : L("CategoryStatus", category == FavoritesKey ? T("Favorites") : LocalizationService.Category(category, _selectedLanguage.Code));
    }

    private void OnCategoryChipChanged(FilterChip chip)
    {
        if (chip.IsSelected) _filterCategories.Add(chip.Value); else _filterCategories.Remove(chip.Value);
        OnFiltersChanged();
    }

    private void OnTemperatureChipChanged(FilterChip chip)
    {
        if (chip.IsSelected) _filterTemperatures.Add(chip.Value); else _filterTemperatures.Remove(chip.Value);
        OnFiltersChanged();
    }

    private void BuildFilterChips()
    {
        CategoryChips.Clear();
        foreach (var category in Profiles.Select(profile => profile.Category).Distinct())
        {
            var chip = new FilterChip(
                category,
                LocalizationService.Category(category, _selectedLanguage.Code),
                Profiles.Count(profile => profile.Category == category),
                OnCategoryChipChanged);
            chip.SetSelectedSilently(_filterCategories.Contains(category));
            CategoryChips.Add(chip);
        }

        TemperatureChips.Clear();
        for (var index = 0; index < Temperatures.Length; index++)
        {
            var value = Temperatures[index];
            var chip = new FilterChip(value, T(TemperatureTextKeys[index]), Profiles.Count(profile => profile.ColorTemperature == value), OnTemperatureChipChanged);
            chip.SetSelectedSilently(_filterTemperatures.Contains(value));
            TemperatureChips.Add(chip);
        }
    }

    private void RefreshSortOptions()
    {
        SortOptions.Clear();
        foreach (var mode in SortModes) SortOptions.Add(new LocalizedOption(mode, T("Sort" + mode)));
        Raise(nameof(SortMode));
    }

    /// <summary>Re-translates chips and sort names after a language change.</summary>
    private void RefreshFilterText()
    {
        foreach (var chip in CategoryChips) chip.Label = LocalizationService.Category(chip.Value, _selectedLanguage.Code);
        for (var index = 0; index < TemperatureChips.Count; index++) TemperatureChips[index].Label = T(TemperatureTextKeys[index]);
        RefreshSortOptions();
        Raise(nameof(ProfileCountSubtitle));
    }

    // ---- summary ---------------------------------------------------------------------------

    public int ActiveFilterCount =>
        (_filterCategories.Count > 0 ? 1 : 0) + (_favoritesOnly ? 1 : 0) + (_hdrOnly ? 1 : 0) +
        (_highPerformanceOnly ? 1 : 0) + (_modifiedOnly ? 1 : 0) + (_filterTemperatures.Count > 0 ? 1 : 0) +
        (BrightnessRange.IsActive ? 1 : 0) + (ContrastRange.IsActive ? 1 : 0) + (SaturationRange.IsActive ? 1 : 0);

    public bool HasActiveFilters => ActiveFilterCount > 0;
    public bool IsFiltering => HasActiveFilters || _searchText.Length > 0;
    public bool HasNoResults => VisibleProfiles.Count == 0;

    public string ProfileCountSubtitle => IsFiltering
        ? L("FilterResults", VisibleProfiles.Count, Profiles.Count)
        : L("ModesSubtitle", Profiles.Count);

    private void ClearFilters()
    {
        _filtersReady = false;
        _filterCategories.Clear();
        _filterTemperatures.Clear();
        _favoritesOnly = _hdrOnly = _highPerformanceOnly = _modifiedOnly = false;
        _sortMode = "Default";
        _searchText = string.Empty;
        BrightnessRange.Reset();
        ContrastRange.Reset();
        SaturationRange.Reset();
        foreach (var chip in CategoryChips.Concat(TemperatureChips)) chip.SetSelectedSilently(false);
        _filtersReady = true;

        foreach (var name in new[] { nameof(FavoritesOnly), nameof(HdrOnly), nameof(HighPerformanceOnly), nameof(ModifiedOnly), nameof(SortMode), nameof(SearchText) })
        {
            Raise(name);
        }

        OnFiltersChanged();
        StatusMessage = L("CompleteLibrary", Profiles.Count);
    }

    // ---- evaluation ------------------------------------------------------------------------

    internal void OnFiltersChanged()
    {
        if (!_filtersReady) return;
        RefreshVisibleProfiles();
        Raise(nameof(SidebarCategory));
        Raise(nameof(ActiveFilterCount));
        Raise(nameof(HasActiveFilters));
        Raise(nameof(IsFiltering));
        Raise(nameof(HasNoResults));
        Raise(nameof(ProfileCountSubtitle));
        ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshVisibleProfiles()
    {
        var matches = Profiles.Where(MatchesFilters);
        matches = SortMode switch
        {
            "NameAsc" => matches.OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "NameDesc" => matches.OrderByDescending(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "BrightnessDesc" => matches.OrderByDescending(profile => profile.Brightness),
            "BrightnessAsc" => matches.OrderBy(profile => profile.Brightness),
            "ContrastDesc" => matches.OrderByDescending(profile => profile.Contrast),
            "SaturationDesc" => matches.OrderByDescending(profile => profile.Saturation),
            "Category" => matches.OrderBy(profile => profile.DisplayCategory, StringComparer.CurrentCultureIgnoreCase),
            _ => matches
        };

        VisibleProfiles.Clear();
        foreach (var profile in matches) VisibleProfiles.Add(profile);
    }

    internal bool MatchesFilters(DisplayProfile profile)
    {
        if (_favoritesOnly && !profile.IsFavorite) return false;
        if (_hdrOnly && !profile.IsHdr) return false;
        if (_highPerformanceOnly && profile.PowerPlan != "HighPerformance") return false;
        if (_modifiedOnly && !IsModified(profile)) return false;
        if (_filterCategories.Count > 0 && !_filterCategories.Contains(profile.Category)) return false;
        if (_filterTemperatures.Count > 0 && !_filterTemperatures.Contains(profile.ColorTemperature)) return false;
        if (!BrightnessRange.Contains(profile.Brightness) || !ContrastRange.Contains(profile.Contrast) ||
            !SaturationRange.Contains(profile.Saturation))
        {
            return false;
        }

        return MatchesText(profile);
    }

    /// <summary>Every word must appear in the name, category or description; case and accents are ignored.</summary>
    private bool MatchesText(DisplayProfile profile)
    {
        if (_searchText.Length == 0) return true;

        var haystack = Normalize($"{profile.DisplayName} {profile.DisplayCategory} {profile.DisplayDescription} {profile.Category}");
        return Normalize(_searchText)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.Ordinal));
    }

    private bool IsModified(DisplayProfile profile) =>
        _profileStore.Defaults.FirstOrDefault(item => item.Id == profile.Id) is { } original &&
        !profile.HasSameAdjustmentsAs(original);

    internal static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
