using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SignalGate;

/// <summary>
/// Receives diagnostic events from the client. Messages are event names such as
/// <c>signalgate.http.request</c>; fields use snake_case keys. The API key is never included.
/// </summary>
/// <remarks>
/// Every call is made outside the client's locks, and any exception thrown by an implementation is caught
/// and ignored. Calls may arrive from a background thread. An implementation should return quickly and
/// should not block.
/// </remarks>
public interface ISignalGateLogger
{
    /// <summary>
    /// Records a debug-level event.
    /// </summary>
    /// <param name="message">The event name.</param>
    /// <param name="fields">Optional structured fields.</param>
    public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null);

    /// <summary>
    /// Records an info-level event.
    /// </summary>
    /// <param name="message">The event name.</param>
    /// <param name="fields">Optional structured fields.</param>
    public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null);

    /// <summary>
    /// Records a warning-level event.
    /// </summary>
    /// <param name="message">The event name.</param>
    /// <param name="fields">Optional structured fields.</param>
    public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null);

    /// <summary>
    /// Records an error-level event.
    /// </summary>
    /// <param name="message">The event name.</param>
    /// <param name="fields">Optional structured fields.</param>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The method name matches the other log levels and is part of the public interface.")]
    public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null);
}
