namespace SignalGate.Internal;

// The values shown in place of the API key.
internal static class Redaction
{
    // Shown wherever the API key would otherwise appear.
    internal const string Mask = "***REDACTED***";

    // The Authorization header value shown in diagnostic output.
    internal const string AuthorizationValue = "Bearer " + Mask;
}
