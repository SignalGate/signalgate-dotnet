using System.Globalization;
using System.Net.Http;
using SignalGate.Internal;

namespace SignalGate;

/// <summary>
/// Settings for a <c>SignalGateClient</c>. The client copies every value when it is constructed,
/// so changing an options instance afterwards has no effect on an existing client.
/// </summary>
public sealed class SignalGateClientOptions
{
    /// <summary>
    /// The API key, sent as <c>Authorization: Bearer &lt;key&gt;</c>. It must not be empty or whitespace,
    /// and every character must be a horizontal tab or printable ASCII (0x20 to 0x7E). The key is sent
    /// exactly as given; it is never trimmed.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// The deadline, in milliseconds, for one <c>CheckAsync</c> request, covering connect, send,
    /// response headers and body. Must be at least 1. The default is 3000.
    /// </summary>
    public int CheckTimeoutMs { get; set; } = 3000;

    /// <summary>
    /// The deadline, in milliseconds, for each delivery attempt of a logged event. Must be at least 1.
    /// The default is 1000. The default close deadline is five times this value.
    /// </summary>
    public int LogTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// The maximum number of logged events waiting for delivery. Must be at least 1. The default is 10000.
    /// When the queue is full, the newest event is dropped and counted. The client also bounds the total size
    /// of queued events to protect process memory; an event over that bound is dropped the same way.
    /// </summary>
    public int LogQueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// The number of retries after a failed delivery attempt of a logged event. Must be zero or more.
    /// The default is 3, which means up to 4 attempts.
    /// </summary>
    public int LogMaxRetries { get; set; } = 3;

    /// <summary>
    /// The base delay, in milliseconds, between delivery attempts of a logged event. The delay doubles
    /// after each attempt (200, 400, 800 ms with the default). Must be zero or more. The default is 200.
    /// </summary>
    public int LogRetryBaseMs { get; set; } = 200;

    /// <summary>
    /// When <see langword="true"/> (the default), a check that times out, fails on the network, receives
    /// a 5xx status or receives an unreadable success response returns an <c>allow</c> verdict with
    /// <see cref="CheckResult.FailedOpen"/> set instead of throwing.
    /// </summary>
    public bool FailOpen { get; set; } = true;

    /// <summary>
    /// An optional logger for diagnostic events. <see langword="null"/> (the default) disables logging.
    /// Exceptions thrown by the logger are caught and ignored.
    /// </summary>
    public ISignalGateLogger? Logger { get; set; }

    /// <summary>
    /// An optional HTTP message handler used for every request. When <see langword="null"/> (the default),
    /// the client creates and owns its own handler. A handler supplied here is never disposed by the client;
    /// it should not follow redirects and should not retry requests. It should observe the cancellation token
    /// it receives: the client stops waiting for a request at its deadline even when the handler ignores the
    /// token while it waits asynchronously, but it cannot stop such a handler, whose call keeps running in the
    /// background until it returns. A handler that blocks its thread before it returns its task delays the call
    /// until it does.
    /// </summary>
    public HttpMessageHandler? HttpHandler { get; set; }

    /// <summary>
    /// Returns a description of these options with the API key masked.
    /// </summary>
    /// <returns>A string that lists every option; the key is always shown as <c>***REDACTED***</c>.</returns>
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"SignalGateClientOptions {{ ApiKey = {Redaction.Mask}, CheckTimeoutMs = {CheckTimeoutMs}, LogTimeoutMs = {LogTimeoutMs}, LogQueueCapacity = {LogQueueCapacity}, LogMaxRetries = {LogMaxRetries}, LogRetryBaseMs = {LogRetryBaseMs}, FailOpen = {FailOpen}, Logger = {Logger?.GetType().Name ?? "null"}, HttpHandler = {HttpHandler?.GetType().Name ?? "null"} }}");
    }
}
