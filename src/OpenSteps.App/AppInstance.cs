using System.Threading;

namespace OpenSteps.App;

/// <summary>
/// Keeps OpenSteps to one running copy per Windows session and lets other processes talk to it by name:
/// a second launch asks the first to show itself, and the installer asks it to save and close before an upgrade.
/// While a recording is in progress a named marker exists so the installer can see it and wait.
/// The names are in the session-local namespace, so nothing here needs administrator rights.
/// </summary>
public sealed class AppInstance : IDisposable
{
    public const string InstanceName = @"Local\OpenSteps.Instance";
    public const string ActivateEventName = @"Local\OpenSteps.Activate";
    public const string ShutdownEventName = @"Local\OpenSteps.Shutdown";
    public const string RecordingMarkerName = @"Local\OpenSteps.Recording";

    private readonly Mutex _instance;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _shutdown;
    private readonly ManualResetEvent _stop = new(false);
    private Mutex? _recording;
    private Thread? _listener;

    private AppInstance(Mutex instance)
    {
        _instance = instance;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName);
    }

    /// <summary>
    /// Claims the single instance. Returns null when OpenSteps is already running; in that case the running
    /// copy has been asked to show its window.
    /// </summary>
    public static AppInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, InstanceName, out var createdNew);
        if (createdNew)
        {
            return new AppInstance(mutex);
        }

        mutex.Dispose();
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var activate))
            {
                using (activate)
                {
                    activate.Set();
                }
            }
        }
        catch
        {
            // The other copy may be shutting down; there is nothing useful to do about it.
        }

        return null;
    }

    /// <summary>Starts listening. The callbacks run on a background thread.</summary>
    public void Listen(Action onActivate, Action onShutdown)
    {
        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _stop, _activate, _shutdown };
            while (true)
            {
                var signalled = WaitHandle.WaitAny(handles);
                if (signalled == 0)
                {
                    return;
                }

                try
                {
                    (signalled == 1 ? onActivate : onShutdown)();
                }
                catch
                {
                    // A failed callback must not stop the listener.
                }
            }
        })
        {
            IsBackground = true,
            Name = "OpenSteps instance listener"
        };
        _listener.Start();
    }

    /// <summary>Publishes (or withdraws) the "recording in progress" marker that the installer checks.</summary>
    public void SetRecording(bool recording)
    {
        if (recording)
        {
            _recording ??= new Mutex(initiallyOwned: false, RecordingMarkerName);
        }
        else
        {
            _recording?.Dispose();
            _recording = null;
        }
    }

    public void Dispose()
    {
        _stop.Set();
        SetRecording(false);
        _activate.Dispose();
        _shutdown.Dispose();
        _instance.Dispose();
    }
}
