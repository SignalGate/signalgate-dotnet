using System;

namespace SignalGate;

/// <summary>
/// The base type of every exception defined by the SignalGate client.
/// </summary>
public class SignalGateException : Exception
{
    /// <summary>
    /// Creates an exception with a default message.
    /// </summary>
    public SignalGateException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SignalGateException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and the exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SignalGateException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
