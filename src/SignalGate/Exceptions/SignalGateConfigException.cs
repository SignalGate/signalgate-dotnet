using System;

namespace SignalGate;

/// <summary>
/// Thrown when the client options are invalid, and when <c>CheckAsync</c> is called on a closed client.
/// </summary>
public sealed class SignalGateConfigException : SignalGateException
{
    /// <summary>
    /// Creates an exception with a default message.
    /// </summary>
    public SignalGateConfigException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SignalGateConfigException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and the exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SignalGateConfigException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
