using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Internal;

namespace SignalGate;

/// <summary>
/// The SignalGate client. It sends checks and log events to <c>https://api.signalgate.ai</c> and keeps counters
/// in <see cref="Metrics"/>.
/// </summary>
/// <remarks>
/// <para>
/// Create one instance per process and share it: every member is safe for concurrent use. Dispose it on
/// shutdown with <see cref="CloseAsync"/>, <see cref="DisposeAsync"/> or <see cref="Dispose"/>, so that queued
/// log events are delivered; events still queued when the process exits are lost.
/// </para>
/// <para>
/// Call <see cref="CheckAsync"/> before the action and <see cref="Log"/> after it. Each call needs its own
/// freshly captured payload; a reused payload is rejected (<c>422 REQUEST_REJECTED</c>). If the browser capture
/// produced no payload, skip the call (current policy).
/// </para>
/// </remarks>
public sealed class SignalGateClient : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// The SDK name sent in the <c>User-Agent</c> header.
    /// </summary>
    public const string SdkName = UserAgent.SdkName;

    private const string BaseAddress = "https://api.signalgate.ai";

    private readonly object _lock = new();

    // Holds the API key.
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly ClientSettings _settings;

    private readonly SafeLogger _logger;
    private readonly Transport _transport;
    private readonly Uri _checkUri;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed when the client closes.")]
    private readonly LogWorker _logWorker;

    private volatile bool _closed;
    private Task? _closeTask;

    /// <summary>
    /// Creates a client with the given API key and default options.
    /// </summary>
    /// <param name="apiKey">The API key, sent as <c>Authorization: Bearer &lt;key&gt;</c>.</param>
    /// <exception cref="SignalGateConfigException"><paramref name="apiKey"/> is null, empty, whitespace or contains
    /// characters that are not allowed in an HTTP header.</exception>
    public SignalGateClient(string apiKey)
        : this(new SignalGateClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>
    /// Creates a client with the given options. Every value is copied, so changing <paramref name="options"/>
    /// afterwards has no effect on the client.
    /// </summary>
    /// <param name="options">The client options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="SignalGateConfigException">An option is invalid.</exception>
    public SignalGateClient(SignalGateClientOptions options)
        : this(options, new Uri(BaseAddress), DefaultBackoffDelay)
    {
    }

    // Constructor for unit tests; the public constructors always use the SignalGate API address and real delays.
    // backoffDelay waits between log delivery attempts; null means Task.Delay.
    internal SignalGateClient(
        SignalGateClientOptions options,
        Uri baseUri,
        Func<TimeSpan, CancellationToken, Task>? backoffDelay = null)
    {
        _settings = ClientSettings.From(options);
        ArgumentNullException.ThrowIfNull(baseUri);

        string root = baseUri.AbsoluteUri.TrimEnd('/');
        _checkUri = new Uri(root + "/v0/check");
        var logUri = new Uri(root + "/v0/log");

        _logger = new SafeLogger(_settings.Logger);
        HttpClient = Transport.CreateHttpClient(_settings.HttpHandler, out SocketsHttpHandler? ownedHandler);
        OwnedHandler = ownedHandler;
        _transport = new Transport(HttpClient, _settings.ApiKey, _logger);

        // Started last, once every option is valid.
        _logWorker = new LogWorker(
            _transport,
            logUri,
            _settings,
            Metrics,
            _logger,
            backoffDelay ?? DefaultBackoffDelay);
    }

    /// <summary>
    /// The SDK version sent in the <c>User-Agent</c> header.
    /// </summary>
    public static string SdkVersion { get; } = UserAgent.SdkVersion;

    /// <summary>
    /// The client's counters.
    /// </summary>
    public SignalGateMetrics Metrics { get; } = new();

    // The HttpClient used for every request; disposed when the client closes.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed when the client closes.")]
    internal HttpClient HttpClient { get; }

    // The handler created by the client when no HttpHandler option was given; null otherwise.
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed together with HttpClient.")]
    internal SocketsHttpHandler? OwnedHandler { get; }

    // The background task that delivers log events.
    internal Task Worker => _logWorker.Completion;

    // The serialized size of the log events that are queued and not yet picked up for delivery.
    internal long QueuedLogBytes => _logWorker.QueuedBytes;

    /// <summary>
    /// Sends one check for <paramref name="evt"/> and returns the verdict. Call it before the gated action, and
    /// gate on <see cref="CheckResult.Action"/> equal to <see cref="CheckActions.Block"/>; never gate on
    /// <see cref="CheckResult.Score"/>.
    /// </summary>
    /// <remarks>
    /// The request is sent once and never retried. With <see cref="SignalGateClientOptions.FailOpen"/> set (the
    /// default), a timeout, a network error, a 5xx status or an unreadable success response returns an
    /// <c>allow</c> verdict with <see cref="CheckResult.FailedOpen"/> set instead of throwing. A 4xx or 3xx
    /// status always throws.
    /// </remarks>
    /// <param name="evt">The event to check.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="SignalGateConfigException">The client is closed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="evt"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="evt"/> cannot be sent: a custom value is unsupported, or
    /// the event is too large to send.</exception>
    /// <exception cref="SignalGateServerException">SignalGate answered with a 4xx or 3xx status; or, with fail-open
    /// disabled, with a 5xx status or an unreadable success response.</exception>
    /// <exception cref="SignalGateTimeoutException">Fail-open is disabled and the request timed out.</exception>
    /// <exception cref="SignalGateNetworkException">Fail-open is disabled and the request failed before a usable
    /// response was received.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<CheckResult> CheckAsync(SignalGateEvent evt, CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            throw new SignalGateConfigException("client is closed");
        }

        ArgumentNullException.ThrowIfNull(evt);

        byte[] body;
        try
        {
            body = WireEncoder.Encode(evt);
        }
        catch (WireEncodingException ex)
        {
            throw new ArgumentException(ex.Message, nameof(evt), ex);
        }

        Metrics.Increment(MetricNames.CheckTotal);
        return CheckCoreAsync(body, cancellationToken);
    }

    /// <summary>
    /// Queues <paramref name="evt"/> for delivery in the background and returns at once. Call it after the
    /// action succeeded. It never throws and never blocks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The event is serialized on the calling thread and delivered by a single background task, in order. A
    /// failed delivery is retried up to <see cref="SignalGateClientOptions.LogMaxRetries"/> times after a 5xx
    /// status, a timeout or a network error. The first retry waits
    /// <see cref="SignalGateClientOptions.LogRetryBaseMs"/>, and the wait doubles for each later retry (200, 400
    /// and 800 ms by default). A 4xx or 3xx status is not retried.
    /// </para>
    /// <para>
    /// The event is dropped, and counted in <c>log_dropped_total</c>, when the queue is full
    /// (<c>reason="queue_full"</c>, with an error <c>signalgate.log.queue_full</c>) or when delivery failed for
    /// good: a 4xx or 3xx status, or every attempt failed (<c>reason="retry_exhausted"</c>). A null event, one
    /// with a <see cref="SignalGateEvent.Custom"/> value that is unsupported, or one too large to send, is dropped
    /// with an error <c>signalgate.log.invalid_event</c> and is not counted. After the client starts closing,
    /// events are ignored.
    /// </para>
    /// </remarks>
    /// <param name="evt">The event to deliver.</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Log never throws; an unexpected failure is reported as an invalid event.")]
    public void Log(SignalGateEvent? evt)
    {
        try
        {
            if (_closed)
            {
                return;
            }

            if (evt is null)
            {
                ReportInvalidEvent("event is null");
                return;
            }

            byte[] body;
            try
            {
                body = WireEncoder.Encode(evt);
            }
            catch (WireEncodingException ex)
            {
                ReportInvalidEvent(ex.Message);
                return;
            }

            var job = new LogJob(body, Uuid.NewV4());
            bool closing;
            bool enqueued = false;
            lock (_lock)
            {
                closing = _closed;
                if (!closing)
                {
                    enqueued = _logWorker.TryEnqueue(job);
                    if (enqueued)
                    {
                        // Counted under the lock, so a close that starts after this call has already seen it.
                        Metrics.Increment(MetricNames.LogEnqueuedTotal);
                    }
                }
            }

            // The logger and every other counter are updated outside the lock.
            if (closing || enqueued)
            {
                return;
            }

            Metrics.Increment(MetricNames.LogDroppedTotal, MetricNames.ReasonLabel, MetricNames.QueueFull);
            _logger.Error(LogEvents.LogQueueFull);
        }
        catch (Exception ex)
        {
            try
            {
                ReportInvalidEvent(ex.Message);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Closes the client: stops accepting events, waits for queued log events to be delivered, then releases
    /// its resources. The first call starts closing; later and concurrent calls return the same task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once closing starts, <see cref="CheckAsync"/> throws <see cref="SignalGateConfigException"/> and
    /// <see cref="Log"/> ignores new events. When the deadline passes, the delivery in progress is cancelled,
    /// and it and every event still queued are counted as <c>log_dropped_total{reason="closed"}</c>. When the
    /// returned task completes, every accepted event has been counted as sent or dropped, and no log delivery
    /// starts afterwards. A <see cref="CheckAsync"/> call that started before closing may still complete, or
    /// fail open, after that.
    /// </para>
    /// <para>
    /// The wait for a delivery in progress ends at the deadline even when a custom
    /// <see cref="SignalGateClientOptions.HttpHandler"/> ignores cancellation while it waits asynchronously, but
    /// the client cannot stop such a handler: its call keeps running in the background until it returns. A logger
    /// or handler that blocks its thread, rather than waiting asynchronously, can make closing wait past the
    /// deadline until it returns. Called from a logger or handler while it runs on the background delivery
    /// task, this method only stops accepting events and returns a completed task; close the client again from
    /// outside to wait for delivery.
    /// </para>
    /// </remarks>
    /// <param name="deadline">How long to wait for queued events: <see cref="TimeSpan.Zero"/> for no wait,
    /// <see cref="Timeout.InfiniteTimeSpan"/> for no limit. The default is five times
    /// <see cref="SignalGateClientOptions.LogTimeoutMs"/>.</param>
    /// <param name="cancellationToken">A token that ends the wait early, as if the deadline had arrived.</param>
    /// <returns>A task that completes when the client is closed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadline"/> is negative and is not
    /// <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    public Task CloseAsync(TimeSpan? deadline = null, CancellationToken cancellationToken = default)
    {
        if (deadline is TimeSpan value && value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                value,
                "The deadline must be zero or more, or Timeout.InfiniteTimeSpan.");
        }

        if (_logWorker.IsWorkerContext)
        {
            // Waiting here would wait for this very call to return; only stop accepting work.
            lock (_lock)
            {
                MarkClosed();
            }

            return Task.CompletedTask;
        }

        TaskCompletionSource completion;
        lock (_lock)
        {
            if (_closeTask is not null)
            {
                return _closeTask;
            }

            MarkClosed();
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = completion.Task;
        }

        _ = CloseCoreAsync(completion, ResolveDeadline(deadline), cancellationToken);
        return completion.Task;
    }

    /// <summary>
    /// Closes the client with the default deadline, delivering queued log events first; the same as
    /// <see cref="CloseAsync"/>.
    /// </summary>
    /// <returns>A task that completes when the client is closed.</returns>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new ValueTask(CloseAsync());
    }

    /// <summary>
    /// Closes the client with the default deadline, delivering queued log events first, and blocks until it is
    /// closed; the same as <see cref="CloseAsync"/>.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CloseAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Returns a description of the client. The API key is never included.
    /// </summary>
    /// <returns>The description, with the key shown as <c>***REDACTED***</c>.</returns>
    public override string ToString()
    {
        return "SignalGateClient { ApiKey = " + Redaction.Mask + " }";
    }

    private static Task DefaultBackoffDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }

    private static bool IsKnownAction(string action)
    {
        return string.Equals(action, CheckActions.Allow, StringComparison.Ordinal)
            || string.Equals(action, CheckActions.Block, StringComparison.Ordinal)
            || string.Equals(action, CheckActions.DryRunBlock, StringComparison.Ordinal)
            || string.Equals(action, CheckActions.AdminAlert, StringComparison.Ordinal);
    }

    private async Task<CheckResult> CheckCoreAsync(byte[] body, CancellationToken cancellationToken)
    {
        string idempotencyKey = Uuid.NewV4();
        AttemptResult attempt = await _transport
            .SendAsync(_checkUri, body, idempotencyKey, _settings.CheckTimeoutMs, cancellationToken)
            .ConfigureAwait(false);

        switch (attempt.Outcome)
        {
            case AttemptOutcome.Canceled:
                Exception canceled = attempt.Error!;
                throw new OperationCanceledException(canceled.Message, canceled, cancellationToken);
            case AttemptOutcome.Timeout:
                return FailOpenOrThrow(MetricNames.TimeoutError, attempt.Error!, attempt.Error!.Message);
            case AttemptOutcome.NetworkError:
                return FailOpenOrThrow(MetricNames.NetworkError, attempt.Error!, attempt.Error!.Message);
            default:
                break;
        }

        int statusCode = attempt.StatusCode;
        if (statusCode >= 500)
        {
            SignalGateServerException degraded = ResponseParser.ToServerException(
                statusCode,
                attempt.Body,
                attempt.ResponseRequestId,
                attempt.RequestId);
            return FailOpenOrThrow(MetricNames.ServerError, degraded, degraded.Message);
        }

        if (statusCode is < 200 or > 299)
        {
            Metrics.Increment(MetricNames.CheckErrorTotal, MetricNames.TypeLabel, MetricNames.ServerError);
            throw ResponseParser.ToServerException(statusCode, attempt.Body, attempt.ResponseRequestId, attempt.RequestId);
        }

        if (!ResponseParser.TryParseCheckResult(attempt.Body, out CheckResult result))
        {
            string requestId = string.IsNullOrEmpty(attempt.ResponseRequestId) ? attempt.RequestId : attempt.ResponseRequestId;
            var malformed = new SignalGateServerException(
                statusCode,
                ResponseParser.MalformedCode,
                ResponseParser.MalformedMessage,
                requestId);
            return FailOpenOrThrow(MetricNames.MalformedResponse, malformed, ResponseParser.MalformedMessage);
        }

        if (!IsKnownAction(result.Action) && _logger.IsEnabled)
        {
            _logger.Warn(LogEvents.CheckUnknownAction, new Dictionary<string, object?>(1, StringComparer.Ordinal)
            {
                ["action"] = result.Action,
            });
        }

        Metrics.Increment(MetricNames.CheckSuccessTotal);
        return result;
    }

    // Counts the error, then either returns the fail-open verdict (with a warning) or throws error.
    private CheckResult FailOpenOrThrow(string errorType, Exception error, string errorText)
    {
        Metrics.Increment(MetricNames.CheckErrorTotal, MetricNames.TypeLabel, errorType);
        if (!_settings.FailOpen)
        {
            throw error;
        }

        if (_logger.IsEnabled)
        {
            _logger.Warn(LogEvents.CheckFailedOpen, new Dictionary<string, object?>(2, StringComparer.Ordinal)
            {
                ["error_type"] = errorType,
                ["error"] = errorText,
            });
        }

        Metrics.Increment(MetricNames.CheckFailedOpenTotal);
        return new CheckResult(CheckActions.Allow, 0.0, string.Empty, string.Empty, string.Empty, 0, failedOpen: true);
    }

    // Must be called with _lock held.
    private void MarkClosed()
    {
        _closed = true;
        _logWorker.Complete();
    }

    // Returns how long CloseAsync waits for queued events: five times LogTimeoutMs when deadline is null,
    // otherwise deadline, clamped to the timer range unless it is Timeout.InfiniteTimeSpan.
    internal TimeSpan ResolveDeadline(TimeSpan? deadline)
    {
        if (deadline is not TimeSpan value)
        {
            return Deadline.ToTimeSpan(5L * _settings.LogTimeoutMs);
        }

        if (value == Timeout.InfiniteTimeSpan)
        {
            return value;
        }

        TimeSpan max = Deadline.ToTimeSpan(Deadline.MaxMs);
        return value > max ? max : value;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Closing always completes; failures while releasing resources are ignored.")]
    private async Task CloseCoreAsync(TaskCompletionSource completion, TimeSpan deadline, CancellationToken cancellationToken)
    {
        try
        {
            Task worker = _logWorker.Completion;
            if (!worker.IsCompleted)
            {
                try
                {
                    await worker.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (!worker.IsCompleted)
            {
                await _logWorker.StopAsync().ConfigureAwait(false);
            }

            try
            {
                await worker.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            try
            {
                HttpClient.Dispose();
            }
            catch (Exception)
            {
            }

            _logWorker.Dispose();
        }
        catch (Exception)
        {
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void ReportInvalidEvent(string reason)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        _logger.Error(LogEvents.LogInvalidEvent, new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            ["reason"] = reason,
        });
    }
}
