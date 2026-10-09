using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SignalGate.Internal;

// Wraps the tenant's logger so that no exception thrown by it can escape into the SDK. Callers must not invoke
// it while holding an SDK lock.
internal sealed class SafeLogger
{
    private readonly ISignalGateLogger? _inner;

    internal SafeLogger(ISignalGateLogger? inner)
    {
        _inner = inner;
        IsEnabled = inner is not null && !ReferenceEquals(inner, NoopSignalGateLogger.Instance);
    }

    // False when nobody listens, so callers can skip building field dictionaries.
    internal bool IsEnabled { get; }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing logger must never affect the caller.")]
    internal void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            _inner!.Debug(message, fields);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing logger must never affect the caller.")]
    internal void Info(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            _inner!.Info(message, fields);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing logger must never affect the caller.")]
    internal void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            _inner!.Warn(message, fields);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing logger must never affect the caller.")]
    internal void Error(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            _inner!.Error(message, fields);
        }
        catch (Exception)
        {
        }
    }
}
