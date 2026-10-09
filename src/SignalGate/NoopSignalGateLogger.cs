using System.Collections.Generic;

namespace SignalGate;

/// <summary>
/// A logger that discards every event. Using it is equivalent to leaving
/// <see cref="SignalGateClientOptions.Logger"/> unset.
/// </summary>
public sealed class NoopSignalGateLogger : ISignalGateLogger
{
    private NoopSignalGateLogger()
    {
    }

    /// <summary>
    /// The shared instance.
    /// </summary>
    public static NoopSignalGateLogger Instance { get; } = new();

    /// <inheritdoc/>
    public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }

    /// <inheritdoc/>
    public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }

    /// <inheritdoc/>
    public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }

    /// <inheritdoc/>
    public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }
}
