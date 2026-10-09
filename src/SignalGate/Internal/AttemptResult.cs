using System;

namespace SignalGate.Internal;

// The result of one HTTP attempt made by Transport.
internal sealed class AttemptResult
{
    private AttemptResult(
        AttemptOutcome outcome,
        string requestId,
        int statusCode,
        byte[] body,
        string? responseRequestId,
        Exception? error)
    {
        Outcome = outcome;
        RequestId = requestId;
        StatusCode = statusCode;
        Body = body;
        ResponseRequestId = responseRequestId;
        Error = error;
    }

    internal AttemptOutcome Outcome { get; }

    // The X-Request-Id value sent with the attempt.
    internal string RequestId { get; }

    // The response status code, or 0 when no response was received.
    internal int StatusCode { get; }

    // The response body, or an empty array when no response was received.
    internal byte[] Body { get; }

    // The first non-empty X-Request-Id response header, or null.
    internal string? ResponseRequestId { get; }

    // For Timeout a SignalGateTimeoutException, for NetworkError a SignalGateNetworkException, for Canceled the
    // cancellation exception; null for Response.
    internal Exception? Error { get; }

    internal static AttemptResult FromResponse(string requestId, int statusCode, byte[] body, string? responseRequestId)
    {
        return new AttemptResult(AttemptOutcome.Response, requestId, statusCode, body, responseRequestId, error: null);
    }

    internal static AttemptResult FromFailure(AttemptOutcome outcome, string requestId, Exception error)
    {
        return new AttemptResult(outcome, requestId, statusCode: 0, [], responseRequestId: null, error);
    }
}
