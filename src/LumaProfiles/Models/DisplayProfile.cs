using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LumaProfiles.Models;

public sealed class DisplayProfile : INotifyPropertyChanged
{
    private int _brightness;
    private int _contrast;
    private int _saturation;
    private double _gamma;
    private double _red;
    private double _green;
    private double _blue;
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
    public double Gamma { get => _gamma; set => Set(ref _gamma, value); }
    public double Red { get => _red; set => Set(ref _red, value); }
    public double Green { get => _green; set => Set(ref _green, value); }
    public double Blue { get => _blue; set => Set(ref _blue, value); }

    [JsonIgnore]
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
        Gamma = Gamma,
        Red = Red,
        Green = Green,
        Blue = Blue
    };

    public void CopyAdjustmentsFrom(DisplayProfile source)
    {
        Brightness = source.Brightness;
        Contrast = source.Contrast;
        Saturation = source.Saturation;
        Gamma = source.Gamma;
        Red = source.Red;
        Green = source.Green;
        Blue = source.Blue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
