using System;
using System.Collections.Generic;
using System.Globalization;

namespace SignalGate;

/// <summary>
/// Thrown when SignalGate answered with a non-2xx status, or with a 2xx response that could not be read
/// while fail-open is disabled. The <see cref="Exception.Message"/> has the form
/// <c>[status] code: message</c>.
/// </summary>
/// <remarks>
/// Public error codes include 400 <c>BAD_REQUEST</c>, 400 <c>INVALID_PAYLOAD</c>, 401 <c>UNAUTHORIZED</c> and
/// 422 <c>REQUEST_REJECTED</c>. A 5xx status means the service is degraded; a check fails open by default.
/// </remarks>
public sealed class SignalGateServerException : SignalGateException
{
    /// <summary>
    /// Creates an exception with a default message. <see cref="StatusCode"/> is 0 and the string
    /// properties are empty.
    /// </summary>
    public SignalGateServerException()
    {
    }

    /// <summary>
    /// Creates an exception with the given message. <see cref="StatusCode"/> is 0 and the string
    /// properties are empty.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SignalGateServerException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the given message and the exception that caused it.
    /// <see cref="StatusCode"/> is 0 and the string properties are empty.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SignalGateServerException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception from an error response. The message is <c>[statusCode] code: serverMessage</c>.
    /// A null string argument is stored as an empty string.
    /// </summary>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="code">The error code from the response, or an empty string.</param>
    /// <param name="serverMessage">The error message from the response, or an empty string.</param>
    /// <param name="requestId">The request id of the failed request.</param>
    /// <param name="details">Optional error details from the response.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public SignalGateServerException(
        int statusCode,
        string code,
        string serverMessage,
        string requestId,
        IReadOnlyDictionary<string, string>? details = null,
        Exception? innerException = null)
        : base(FormatMessage(statusCode, code, serverMessage), innerException)
    {
        StatusCode = statusCode;
        Code = code ?? string.Empty;
        ServerMessage = serverMessage ?? string.Empty;
        RequestId = requestId ?? string.Empty;
        Details = details;
    }

    /// <summary>
    /// The HTTP status code of the response, or 0 when the exception was not created from a response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The error code from the response, for example <c>UNAUTHORIZED</c>, or an empty string.
    /// </summary>
    public string Code { get; } = string.Empty;

    /// <summary>
    /// The error message from the response, or an empty string.
    /// </summary>
    public string ServerMessage { get; } = string.Empty;

    /// <summary>
    /// The request id of the failed request: the id reported in the response when present, otherwise the
    /// <c>X-Request-Id</c> response header, otherwise the id the client sent.
    /// </summary>
    public string RequestId { get; } = string.Empty;

    /// <summary>
    /// Error details from the response, or <see langword="null"/> when the response had none. String values
    /// are kept as they are; any other value is stored as its JSON text.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Details { get; }

    private static string FormatMessage(int statusCode, string? code, string? serverMessage)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{statusCode}] {code ?? string.Empty}: {serverMessage ?? string.Empty}");
    }
}
