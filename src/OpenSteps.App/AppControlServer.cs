using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// Makes OpenSteps single-instance per user and lets other processes talk to the running copy over a
/// current-user-only named pipe (see <see cref="AppControl"/>): a second launch brings the first to the front,
/// and the install script asks it to close, which it refuses while recording.
/// </summary>
internal sealed class AppControlServer : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Func<string, Task<string>>? _handler;

    private AppControlServer(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Claims the single-instance slot for this user, or returns null when OpenSteps is already running.</summary>
    public static AppControlServer? TryBecomeTheInstance()
    {
        var sid = GetUserSid();
        var mutex = new Mutex(initiallyOwned: true, AppControl.GetMutexName(sid), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new AppControlServer(mutex, GetPipeName(sid));
    }

    /// <summary>Sends one command to the running copy and returns its reply, or null if it didn't answer in time.</summary>
    public static string? Send(string command, int timeoutMilliseconds = 3000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", GetPipeName(GetUserSid()), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMilliseconds);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 256, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 256, leaveOpen: true);
            writer.WriteLine(command);
            return reader.ReadLine();
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Starts answering commands. <paramref name="handler"/> gets the command and returns the reply line.</summary>
    public void Start(Func<string, Task<string>> handler)
    {
        _handler = handler;
        _ = Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread; it is released when the process exits.
        }

        _mutex.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 256, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 256, leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(timeout.Token);
                var reply = command is null || _handler is null ? AppControl.ReplyUnknown : await _handler(command);
                await writer.WriteLineAsync(reply.AsMemory(), timeout.Token);
                pipe.WaitForPipeDrain();
            }
            catch (OperationCanceledException)
            {
                // Shutting down, or a client connected and then said nothing.
            }
            catch (IOException)
            {
                // The client went away mid-conversation; wait for the next one.
            }
            catch (Exception)
            {
                await Task.Delay(500);
            }
        }
    }

    private static string GetPipeName(string sid)
    {
        return AppControl.GetPipeName(sid, System.Diagnostics.Process.GetCurrentProcess().SessionId);
    }

    private static string GetUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? Environment.UserName;
    }
}
