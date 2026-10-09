using System;

namespace SignalGate;

/// <summary>
/// Thrown when a request did not complete within the client's per-request deadline.
/// </summary>
public sealed class SignalGateTimeoutException : SignalGateException
{
    /// <summary>
    /// Creates an exception with a default message.
    /// </summary>
    public SignalGateTimeoutException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SignalGateTimeoutException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and the exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SignalGateTimeoutException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
