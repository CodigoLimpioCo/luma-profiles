using Microsoft.Win32;

namespace LumaProfiles.Services;

/// <summary>Reports whether the PC runs on battery and raises an event when the power source changes.</summary>
public sealed class PowerSourceWatcher : IDisposable
{
    private bool? _lastOnBattery;

    public PowerSourceWatcher() => SystemEvents.PowerModeChanged += OnPowerModeChanged;

    /// <summary>True on battery, false on AC power, null when the PC has no battery (a desktop).</summary>
    public event Action<bool>? PowerSourceChanged;

    public static bool? IsOnBattery()
    {
        try
        {
            var status = System.Windows.Forms.SystemInformation.PowerStatus;
            if (status.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) return null;
            return status.PowerLineStatus switch
            {
                System.Windows.Forms.PowerLineStatus.Offline => true,
                System.Windows.Forms.PowerLineStatus.Online => false,
                _ => null,
            };
        }
        catch (Exception exception)
        {
            AppLog.Warn("Could not read the power status.", exception);
            return null;
        }
    }

    /// <summary>Raises the current state once (start-up).</summary>
    public void Start()
    {
        _lastOnBattery = IsOnBattery();
        if (_lastOnBattery is { } onBattery) PowerSourceChanged?.Invoke(onBattery);
    }

    public void Dispose() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;
        var now = IsOnBattery();
        if (now is not { } onBattery || now == _lastOnBattery) return;
        _lastOnBattery = now;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => PowerSourceChanged?.Invoke(onBattery));
    }
}
