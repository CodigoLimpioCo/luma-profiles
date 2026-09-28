using System.Diagnostics;
using Microsoft.Win32;

namespace LumaProfiles.Services;

/// <summary>OS interactions the view model needs but should not perform directly (keeps it testable).</summary>
public interface IShellService
{
    void OpenUrl(string url);
    string? PickSaveFile(string title, string suggestedFileName);
    string? PickOpenFile(string title);
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

    public string? PickOpenFile(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = Filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
