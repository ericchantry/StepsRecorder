namespace OpenSteps.Core.Services;

/// <summary>
/// Measures recording time with paused periods removed. The video recorder and the input
/// timeline share one clock so event offsets line up with video timestamps.
/// Thread-safe: the video encoder reads it from a background thread.
/// </summary>
public sealed class RecordingClock
{
    private readonly object _sync = new();
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedAt;
    private DateTimeOffset? _stoppedAt;
    private TimeSpan _pausedTotal;

    public RecordingClock(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _startedAt.HasValue && !_stoppedAt.HasValue;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _pausedAt.HasValue;
            }
        }
    }

    public TimeSpan Elapsed => ElapsedAt(_now());

    public void Start()
    {
        lock (_sync)
        {
            _startedAt = _now();
            _pausedAt = null;
            _stoppedAt = null;
            _pausedTotal = TimeSpan.Zero;
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_startedAt.HasValue && !_stoppedAt.HasValue && !_pausedAt.HasValue)
            {
                _pausedAt = _now();
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_pausedAt is { } pausedAt)
            {
                _pausedTotal += _now() - pausedAt;
                _pausedAt = null;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_startedAt.HasValue || _stoppedAt.HasValue)
            {
                return;
            }

            _stoppedAt = _pausedAt ?? _now();
        }
    }

    /// <summary>Recording time at wall-clock instant <paramref name="instant"/>, clamped to the running range.</summary>
    public TimeSpan ElapsedAt(DateTimeOffset instant)
    {
        lock (_sync)
        {
            if (_startedAt is not { } startedAt)
            {
                return TimeSpan.Zero;
            }

            var end = instant;
            if (_pausedAt is { } pausedAt && end > pausedAt)
            {
                end = pausedAt;
            }

            if (_stoppedAt is { } stoppedAt && end > stoppedAt)
            {
                end = stoppedAt;
            }

            var elapsed = end - startedAt - _pausedTotal;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }
}
