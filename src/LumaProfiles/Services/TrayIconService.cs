using System.Windows;
using LumaProfiles.ViewModels;

namespace LumaProfiles.Services;

/// <summary>System tray icon with quick access to favorites and the neutral profile.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly Action _showWindow;
    private readonly Action _exit;

    public TrayIconService(MainViewModel viewModel, Action showWindow, Action exit)
    {
        _viewModel = viewModel;
        _showWindow = showWindow;
        _exit = exit;

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Luma Profiles",
            Icon = LoadIcon(),
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip(),
            Visible = true
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left) _showWindow();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(_viewModel["TrayShow"], null, (_, _) => _showWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var favorites = _viewModel.Favorites.Take(12).ToList();
        if (favorites.Count == 0)
        {
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(_viewModel["TrayNoFavorites"]) { Enabled = false });
        }
        foreach (var profile in favorites)
        {
            var id = profile.Id;
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(profile.DisplayName, null, (_, _) => _viewModel.ApplyProfileById(id))
            {
                Checked = profile.IsActive
            });
        }

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_viewModel["Neutralize"], null, (_, _) => _viewModel.ApplyNeutral());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_viewModel["TrayExit"], null, (_, _) => _exit());
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LumaProfiles.ico"))?.Stream;
            if (stream is not null) return new System.Drawing.Icon(stream);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Tray icon resource could not be loaded.", exception);
        }

        return System.Drawing.SystemIcons.Application;
    }
}
