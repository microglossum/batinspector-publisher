namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Keeps a minimum interval between the starts of consecutive requests, so a batch stays under iNaturalist's
/// request limit instead of running into a 429. Concurrent callers queue up, so the spacing holds for all of them.
/// </summary>
internal sealed class RequestPacer
{
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long? _lastStart;

    public RequestPacer(TimeSpan minInterval, TimeProvider time, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _minInterval = minInterval;
        _time = time;
        _delay = delay;
    }

    /// <summary>Returns when the next request may start.</summary>
    public async Task WaitAsync(CancellationToken ct)
    {
        if (_minInterval <= TimeSpan.Zero)
        {
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_lastStart is { } last)
            {
                var remaining = _minInterval - _time.GetElapsedTime(last);
                if (remaining > TimeSpan.Zero)
                {
                    await _delay(remaining, ct);
                }
            }

            _lastStart = _time.GetTimestamp();
        }
        finally
        {
            _gate.Release();
        }
    }
}
