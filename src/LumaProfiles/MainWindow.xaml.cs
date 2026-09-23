using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LumaProfiles.Models;
using LumaProfiles.Services;

namespace LumaProfiles;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ProfileStore _profileStore = new();
    private readonly MonitorService _monitorService = new();
    private DisplayProfile _selectedProfile;
    private string _selectedCategory = "Todos";
    private string _selectedMonitorTarget = "Ambas pantallas";
    private string _statusMessage = "Listo. Selecciona un perfil para aplicarlo o ajustarlo.";

    public ObservableCollection<DisplayProfile> Profiles { get; }
    public ICollectionView VisibleProfiles { get; }
    public IReadOnlyList<string> MonitorTargets { get; } = ["Ambas pantallas", "Pantalla 1", "Pantalla 2"];

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
        VisibleProfiles = CollectionViewSource.GetDefaultView(Profiles);
        VisibleProfiles.Filter = item => item is DisplayProfile profile &&
            (_selectedCategory == "Todos" || profile.Category == _selectedCategory);

        InitializeComponent();
        DataContext = this;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
        {
            _selectedCategory = category;
            VisibleProfiles.Refresh();
            StatusMessage = category == "Todos" ? "Mostrando todos los perfiles." : $"Categoría: {category}.";
        }
    }

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
