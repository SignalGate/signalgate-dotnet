using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGate.Tests.Fakes;

// Stands in for the wait between log delivery attempts. It records every requested delay and returns at once,
// or fails with the given exception.
public sealed class RecordingBackoff
{
    private readonly object _sync = new();
    private readonly List<TimeSpan> _delays = [];
    private readonly Func<Exception>? _failure;

    public RecordingBackoff(Func<Exception>? failure = null)
    {
        _failure = failure;
    }

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_sync)
            {
                return _delays.ToArray();
            }
        }
    }

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _delays.Add(delay);
        }

        if (_failure is not null)
        {
            return Task.FromException(_failure());
        }

        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }
}
