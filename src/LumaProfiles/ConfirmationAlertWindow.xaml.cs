using System.Windows;
using LumaProfiles.ViewModels;

namespace LumaProfiles;

/// <summary>The "keep changes?" question on its own, for when the main window is hidden, minimized or covered.</summary>
public partial class ConfirmationAlertWindow : Window
{
    private const double ScreenMargin = 12;

    /// <param name="styles">Theme, control styles and accent copied from the main window so the card looks the same.</param>
    public ConfirmationAlertWindow(MainViewModel viewModel, ResourceDictionary styles)
    {
        Resources = styles;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => PlaceInCorner();
        SizeChanged += (_, _) => PlaceInCorner();
    }

    /// <summary>Bottom-right of the work area, above the taskbar, like a system notification.</summary>
    private void PlaceInCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Bottom - ActualHeight - ScreenMargin;
    }
}
