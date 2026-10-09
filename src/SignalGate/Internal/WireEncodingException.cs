using System;
using System.Diagnostics.CodeAnalysis;

namespace SignalGate.Internal;

// Raised by WireEncoder for an event that cannot be serialized. Callers translate it into the public error for
// their path.
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Never escapes the SDK; callers translate it.")]
internal sealed class WireEncodingException : Exception
{
    public WireEncodingException()
    {
    }

    public WireEncodingException(string message)
        : base(message)
    {
    }

    public WireEncodingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
