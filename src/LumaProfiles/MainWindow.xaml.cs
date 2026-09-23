using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ProfileStore _profileStore = new();
    private readonly MonitorService _monitorService = new();
    private DisplayProfile _selectedProfile;
    private string _selectedCategory = "Todos";
    private string _searchText = string.Empty;
    private string _selectedMonitorTarget = "Ambas pantallas";
    private string _statusMessage = "Listo. Selecciona un perfil para aplicarlo o ajustarlo.";

    public ObservableCollection<DisplayProfile> Profiles { get; }
    public ObservableCollection<DisplayProfile> VisibleProfiles { get; } = [];
    public IReadOnlyList<string> MonitorTargets { get; } = ["Ambas pantallas", "Pantalla 1", "Pantalla 2"];
    public IReadOnlyList<string> ColorTemperatureOptions { get; } =
        ["Usuario (RGB)", "Cálido 5000 K", "Neutro 6500 K", "Frío 7500 K"];

    public DisplayProfile SelectedProfile
    {
        get => _selectedProfile;
        set => Set(ref _selectedProfile, value);
    }

    public string SelectedMonitorTarget
    {
        get => _selectedMonitorTarget;
        set => Set(ref _selectedMonitorTarget, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public MainWindow()
    {
        Profiles = new ObservableCollection<DisplayProfile>(_profileStore.Load());
        _selectedProfile = Profiles.First();
        RefreshVisibleProfiles();

        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => ProfilesScrollViewer.ScrollToTop();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category })
        {
            _selectedCategory = category;
            RefreshVisibleProfiles();
            ProfilesScrollViewer?.ScrollToTop();
            StatusMessage = category == "Todos" ? $"Mostrando los {Profiles.Count} perfiles." : $"Categoría: {category}.";
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = (sender as TextBox)?.Text?.Trim() ?? string.Empty;
        RefreshVisibleProfiles();
        ProfilesScrollViewer?.ScrollToTop();
        StatusMessage = string.IsNullOrWhiteSpace(_searchText)
            ? $"Biblioteca completa: {Profiles.Count} perfiles."
            : $"Resultados para “{_searchText}”.";
    }

    private void RefreshVisibleProfiles()
    {
        VisibleProfiles.Clear();
        foreach (var profile in Profiles.Where(MatchesCurrentFilter))
        {
            VisibleProfiles.Add(profile);
        }
    }

    private bool MatchesCurrentFilter(DisplayProfile profile)
    {
        if (_selectedCategory != "Todos" && profile.Category != _selectedCategory) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        return profile.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) ||
               profile.Category.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) ||
               profile.Description.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenCodigoLimpio_Click(object sender, RoutedEventArgs e) => OpenUrl("https://codigolimpio.com.co/");

    private void OpenCodigoLimpioGithub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/CodigoLimpioCo");

    private void OpenHdrSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DisplayProfile profile })
        {
            SelectedProfile = profile;
            StatusMessage = $"Editando {profile.Name}. Ajusta los controles y pulsa Guardar y aplicar.";
        }
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DisplayProfile profile })
        {
            SelectedProfile = profile;
            Apply(profile);
        }
    }

    private void SaveAndApply_Click(object sender, RoutedEventArgs e)
    {
        _profileStore.Save(Profiles);
        Apply(SelectedProfile);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var restored = _profileStore.GetDefault(SelectedProfile.Id);
        SelectedProfile.CopyAdjustmentsFrom(restored);
        _profileStore.Save(Profiles);
        StatusMessage = $"{SelectedProfile.Name} volvió a sus valores originales.";
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        StatusMessage = "Neutralizando la señal de color…";
        var result = _monitorService.RestoreNeutral(SelectedMonitorTarget);
        StatusMessage = FormatResult("Color neutralizado", result);
    }

    private void Apply(DisplayProfile profile)
    {
        StatusMessage = $"Aplicando {profile.Name}…";
        var result = _monitorService.Apply(profile, SelectedMonitorTarget);
        foreach (var item in Profiles) item.IsActive = false;
        profile.IsActive = result.DisplayCount > 0;
        _profileStore.Save(Profiles);
        StatusMessage = FormatResult($"{profile.Name} aplicado", result);
        if (profile.IsHdr)
        {
            StatusMessage += " Activa HDR en Windows; al hacerlo, el monitor administra y bloquea varios controles SDR.";
        }
    }

    private static string FormatResult(string successText, ApplyResult result)
    {
        if (result.Failures.Count == 0)
        {
            return $"{successText} en {result.DisplayCount} pantalla(s).";
        }

        var summary = string.Join(" ", result.Failures.Distinct().Take(2));
        return $"{successText} en {result.DisplayCount} pantalla(s), con avisos: {summary}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
