using System.Diagnostics;
using Microsoft.Win32;

namespace LumaProfiles.Services;

/// <summary>OS interactions the view model needs but should not perform directly (keeps it testable).</summary>
public interface IShellService
{
    void OpenUrl(string url);
    string? PickSaveFile(string title, string suggestedFileName);
    string? PickOpenFile(string title);
    void IdentifyDisplays(IReadOnlyList<DisplayInfo> displays);

    /// <summary>Asks a yes/no question before something the user cannot easily undo.</summary>
    bool Confirm(string title, string message);
}

public sealed class ShellService : IShellService
{
    private const string Filter = "Luma Profiles (*.json)|*.json";

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not open '{url}'.", exception);
        }
    }

    public string? PickSaveFile(string title, string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = suggestedFileName, Filter = Filter, DefaultExt = ".json" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void IdentifyDisplays(IReadOnlyList<DisplayInfo> displays) => DisplayIdentifier.Show(displays);

    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

    public string? PickOpenFile(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = Filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
