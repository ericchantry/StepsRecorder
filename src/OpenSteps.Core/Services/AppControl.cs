namespace OpenSteps.Core.Services;

/// <summary>What the running OpenSteps is doing, as far as closing it safely is concerned.</summary>
public enum AppActivity
{
    /// <summary>Nothing would be lost by closing now.</summary>
    Idle,

    /// <summary>A recording is in progress (including while paused).</summary>
    Recording,

    /// <summary>A dialog with unsaved work is open, e.g. the step editor.</summary>
    Busy
}

/// <summary>
/// The one-line text protocol spoken over OpenSteps' per-user named pipe. A second copy of the app uses it to bring
/// the first to the front, and the install and uninstall scripts use it to close the app without losing work.
/// </summary>
public static class AppControl
{
    public const string Activate = "activate";
    public const string Status = "status";
    public const string Quit = "quit";

    public const string ReplyOk = "ok";
    public const string ReplyIdle = "idle";
    public const string ReplyRecording = "recording";
    public const string ReplyBusy = "busy";
    public const string ReplyUnknown = "unknown";

    /// <summary>Pipe name for one user in one Windows session, so users on a shared PC never reach each other's app.</summary>
    public static string GetPipeName(string userSid, int windowsSessionId)
    {
        return $"OpenSteps-{windowsSessionId}-{userSid}";
    }

    public static string GetMutexName(string userSid)
    {
        return $@"Local\OpenSteps-{userSid}";
    }

    /// <summary>
    /// The reply to <paramref name="command"/> given what the app is doing, and whether the app should then act on it
    /// (come to the front, or close). A quit is refused while recording or busy: an upgrade can wait, a recording can't be redone.
    /// </summary>
    public static (string Reply, bool Act) Decide(string? command, AppActivity activity)
    {
        var state = activity switch
        {
            AppActivity.Recording => ReplyRecording,
            AppActivity.Busy => ReplyBusy,
            _ => ReplyIdle
        };

        return command?.Trim().ToLowerInvariant() switch
        {
            Activate => (ReplyOk, true),
            Status => (state, false),
            Quit => activity == AppActivity.Idle ? (ReplyOk, true) : (state, false),
            _ => (ReplyUnknown, false)
        };
    }
}
