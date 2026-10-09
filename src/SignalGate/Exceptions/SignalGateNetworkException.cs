using System;

namespace SignalGate;

/// <summary>
/// Thrown when a request failed before a usable response was received: a DNS, connection, TLS or I/O
/// failure, any other failure in the HTTP handler, or a response that was redirected.
/// </summary>
public sealed class SignalGateNetworkException : SignalGateException
{
    /// <summary>
    /// Creates an exception with a default message.
    /// </summary>
    public SignalGateNetworkException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SignalGateNetworkException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and the exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SignalGateNetworkException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
