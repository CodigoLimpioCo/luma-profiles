namespace LumaProfiles.Services;

/// <summary>
/// Lets only one copy of the app run per Windows session. A second launch calls <see cref="SignalExisting"/> so the
/// first one brings its window to the front, then exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private Thread? _listener;
    private bool _owned;
    private volatile bool _stopping;

    public SingleInstanceGuard(string name = "LumaProfiles.SingleInstance")
    {
        _mutex = new Mutex(initiallyOwned: false, $@"Local\{name}");
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
    }

    /// <summary>True for the first copy; false when another one already holds the guard.</summary>
    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous copy crashed while holding it; ownership passes to this one.
            _owned = true;
        }

        return _owned;
    }

    public void SignalExisting() => _activation.Set();

    /// <summary>Calls <paramref name="onActivated"/> (on a worker thread) each time another launch asks for this one.</summary>
    public void ListenForActivation(Action onActivated)
    {
        _listener = new Thread(() =>
        {
            while (true)
            {
                _activation.WaitOne();
                if (_stopping) return;
                onActivated();
            }
        })
        { IsBackground = true, Name = "Luma single-instance listener" };
        _listener.Start();
    }

    public void Dispose()
    {
        _stopping = true;
        _activation.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));
        if (_owned) _mutex.ReleaseMutex();
        _owned = false;
        _mutex.Dispose();
        _activation.Dispose();
    }
}
