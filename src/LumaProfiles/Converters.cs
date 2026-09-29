using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LumaProfiles.Services;

namespace LumaProfiles;

/// <summary>True when the bound value equals the converter parameter (used to check the active sidebar entry).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Turns a profile's adjustment values into the simulated "after" picture.</summary>
public sealed class ProfilePreviewConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (values.Length < 9 || values.Any(value => value == DependencyProperty.UnsetValue)) return null;

            var settings = new PreviewSettings(
                System.Convert.ToInt32(values[0], CultureInfo.InvariantCulture),
                System.Convert.ToInt32(values[1], CultureInfo.InvariantCulture),
                System.Convert.ToInt32(values[2], CultureInfo.InvariantCulture),
                System.Convert.ToInt32(values[3], CultureInfo.InvariantCulture),
                System.Convert.ToDouble(values[4], CultureInfo.InvariantCulture),
                System.Convert.ToDouble(values[5], CultureInfo.InvariantCulture),
                System.Convert.ToDouble(values[6], CultureInfo.InvariantCulture),
                System.Convert.ToDouble(values[7], CultureInfo.InvariantCulture),
                values[8]?.ToString() ?? string.Empty);
            return ProfilePreviewRenderer.Render(settings);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
