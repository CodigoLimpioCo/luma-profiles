using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LumaProfiles.Models;

public sealed class DisplayProfile : INotifyPropertyChanged
{
    private int _brightness;
    private int _contrast;
    private int _saturation;
    private int _hue;
    private double _gamma;
    private double _red;
    private double _green;
    private double _blue;
    private string _colorTemperature = "Usuario (RGB)";
    private bool _isActive;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string PreviewStart { get; init; }
    public required string PreviewEnd { get; init; }
    public string PowerPlan { get; init; } = "Balanced";

    public int Brightness { get => _brightness; set => Set(ref _brightness, value); }
    public int Contrast { get => _contrast; set => Set(ref _contrast, value); }
    public int Saturation { get => _saturation; set => Set(ref _saturation, value); }
    public int Hue { get => _hue; set => Set(ref _hue, value); }
    public double Gamma { get => _gamma; set => Set(ref _gamma, value); }
    public double Red { get => _red; set => Set(ref _red, value); }
    public double Green { get => _green; set => Set(ref _green, value); }
    public double Blue { get => _blue; set => Set(ref _blue, value); }
    public string ColorTemperature { get => _colorTemperature; set => Set(ref _colorTemperature, value); }

    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    public DisplayProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Category = Category,
        Description = Description,
        PreviewStart = PreviewStart,
        PreviewEnd = PreviewEnd,
        PowerPlan = PowerPlan,
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Hue = Hue,
        Gamma = Gamma,
        Red = Red,
        Green = Green,
        Blue = Blue,
        ColorTemperature = ColorTemperature
    };

    public void CopyAdjustmentsFrom(DisplayProfile source)
    {
        Brightness = source.Brightness;
        Contrast = source.Contrast;
        Saturation = source.Saturation;
        Hue = source.Hue;
        Gamma = source.Gamma;
        Red = source.Red;
        Green = source.Green;
        Blue = source.Blue;
        ColorTemperature = source.ColorTemperature;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
