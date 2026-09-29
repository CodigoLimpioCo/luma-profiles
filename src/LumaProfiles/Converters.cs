using System.Globalization;
using System.Windows.Data;

namespace LumaProfiles;

/// <summary>True when the bound value equals the converter parameter (used to check the active sidebar entry).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
