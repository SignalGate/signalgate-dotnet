namespace SignalGate.Internal;

// How one HTTP attempt ended.
internal enum AttemptOutcome
{
    // A response was received; its status code may be any value.
    Response,

    // The caller's token or the client's stop token was cancelled.
    Canceled,

    // The per-attempt deadline elapsed.
    Timeout,

    // The request failed before a usable response was received, or the response was redirected.
    NetworkError,
}
