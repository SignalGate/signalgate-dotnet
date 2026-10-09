using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SignalGate.Internal;

// Owns the log queue and the single background task that delivers queued events, one at a time and in order.
// Every dequeued job ends counted exactly once: as sent, as retry_exhausted or as closed.
internal sealed class LogWorker : IDisposable
{
    // Upper bound on queued bytes.
    internal const long MaxQueuedBytes = 64L * 1024 * 1024;

    private readonly Channel<LogJob> _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly AsyncLocal<bool> _workerContext = new();
    private readonly Transport _transport;
    private readonly Uri _logUri;
    private readonly int _timeoutMs;
    private readonly int _maxRetries;
    private readonly int _retryBaseMs;
    private readonly SignalGateMetrics _metrics;
    private readonly SafeLogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _backoffDelay;

    private long _queuedBytes;

    // Creates the queue and starts the worker task. The worker does not inherit the caller's execution context.
    internal LogWorker(
        Transport transport,
        Uri logUri,
        ClientSettings settings,
        SignalGateMetrics metrics,
        SafeLogger logger,
        Func<TimeSpan, CancellationToken, Task> backoffDelay)
    {
        _transport = transport;
        _logUri = logUri;
        _timeoutMs = settings.LogTimeoutMs;
        _maxRetries = settings.LogMaxRetries;
        _retryBaseMs = settings.LogRetryBaseMs;
        _metrics = metrics;
        _logger = logger;
        _backoffDelay = backoffDelay;
        _channel = Channel.CreateBounded<LogJob>(new BoundedChannelOptions(settings.LogQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        if (ExecutionContext.IsFlowSuppressed())
        {
            Completion = Task.Run(RunAsync);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                Completion = Task.Run(RunAsync);
            }
        }
    }

    // The worker task. It always ends in RanToCompletion, once the queue is completed and drained.
    internal Task Completion { get; }

    // True for code running on the worker, such as a logger or handler called during delivery.
    internal bool IsWorkerContext => _workerContext.Value;

    // The serialized size of the jobs that are queued and not yet picked up by the worker.
    internal long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    // Returns the delay before the retry that follows the given failed attempt (0-based): base × 2^attempt,
    // computed without overflow and clamped.
    internal static TimeSpan BackoffDelay(int retryBaseMs, long attempt)
    {
        long ms;
        if (retryBaseMs <= 0)
        {
            ms = 0;
        }
        else if (attempt >= 32)
        {
            ms = Deadline.MaxMs;
        }
        else
        {
            ms = Math.Min(Deadline.MaxMs, (long)retryBaseMs << (int)attempt);
        }

        return Deadline.ToTimeSpan(ms);
    }

    // Queues job unless that would exceed the queued-bytes bound or the queue is full or completed.
    // Called with the client's lock held, so it must stay short and must not call the logger.
    internal bool TryEnqueue(LogJob job)
    {
        long length = job.Body.Length;
        if (Interlocked.Read(ref _queuedBytes) + length > MaxQueuedBytes)
        {
            return false;
        }

        Interlocked.Add(ref _queuedBytes, length);
        if (_channel.Writer.TryWrite(job))
        {
            return true;
        }

        Interlocked.Add(ref _queuedBytes, -length);
        return false;
    }

    // Stops accepting jobs. The worker delivers what is queued, then ends.
    internal void Complete()
    {
        _channel.Writer.TryComplete();
    }

    // Aborts the attempt or backoff in progress; it and every job still queued are counted as closed.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing cancellation callback must not stop the client from closing.")]
    internal async Task StopAsync()
    {
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    // Must be called only after the worker task has completed.
    public void Dispose()
    {
        _stop.Dispose();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The worker must keep draining the queue whatever happens.")]
    private async Task RunAsync()
    {
        _workerContext.Value = true;
        ChannelReader<LogJob> reader = _channel.Reader;
        while (true)
        {
            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out LogJob? job))
                    {
                        Interlocked.Add(ref _queuedBytes, -job.Body.Length);
                        await DeliverAsync(job).ConfigureAwait(false);
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                ReportUnhandled(ex);
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "An unexpected failure drops one job and must not stop the worker.")]
    private async Task DeliverAsync(LogJob job)
    {
        try
        {
            await DeliverCoreAsync(job).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Failed attempts were counted when they happened; this failure adds no log_http_error_total.
            Drop(job, MetricNames.RetryExhausted);
            ReportUnhandled(ex);
        }
    }

    private async Task DeliverCoreAsync(LogJob job)
    {
        CancellationToken stop = _stop.Token;
        for (long attempt = 0; ; attempt++)
        {
            if (stop.IsCancellationRequested)
            {
                Drop(job, MetricNames.Closed);
                return;
            }

            AttemptResult result = await _transport
                .SendAsync(_logUri, job.Body, job.IdempotencyKey, _timeoutMs, stop)
                .ConfigureAwait(false);
            bool lastAttempt = attempt >= _maxRetries;

            switch (result.Outcome)
            {
                case AttemptOutcome.Canceled:
                    Drop(job, MetricNames.Closed);
                    return;

                case AttemptOutcome.Timeout:
                case AttemptOutcome.NetworkError:
                    _metrics.Increment(MetricNames.LogHttpErrorTotal, MetricNames.StatusLabel, MetricNames.Network);
                    if (lastAttempt)
                    {
                        Drop(job, MetricNames.RetryExhausted);
                        WarnDroppedNetwork(result.Error);
                        return;
                    }

                    break;

                default:
                    int statusCode = result.StatusCode;
                    if (statusCode is >= 200 and <= 299)
                    {
                        MarkSent(job);
                        return;
                    }

                    _metrics.Increment(
                        MetricNames.LogHttpErrorTotal,
                        MetricNames.StatusLabel,
                        statusCode.ToString(CultureInfo.InvariantCulture));
                    if (statusCode < 500)
                    {
                        Drop(job, MetricNames.RetryExhausted);
                        WarnDropped4xx(result);
                        return;
                    }

                    if (lastAttempt)
                    {
                        Drop(job, MetricNames.RetryExhausted);
                        return;
                    }

                    break;
            }

            if (stop.IsCancellationRequested)
            {
                Drop(job, MetricNames.Closed);
                return;
            }

            try
            {
                await _backoffDelay(BackoffDelay(_retryBaseMs, attempt), stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                Drop(job, MetricNames.Closed);
                return;
            }
        }
    }

    private void MarkSent(LogJob job)
    {
        if (job.Accounted)
        {
            return;
        }

        job.Accounted = true;
        _metrics.Increment(MetricNames.LogSentTotal);
    }

    private void Drop(LogJob job, string reason)
    {
        if (job.Accounted)
        {
            return;
        }

        job.Accounted = true;
        _metrics.Increment(MetricNames.LogDroppedTotal, MetricNames.ReasonLabel, reason);
    }

    private void WarnDropped4xx(AttemptResult result)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        SignalGateServerException error = ResponseParser.ToServerException(
            result.StatusCode,
            result.Body,
            result.ResponseRequestId,
            result.RequestId);
        _logger.Warn(LogEvents.LogDropped4xx, new Dictionary<string, object?>(2, StringComparer.Ordinal)
        {
            ["status"] = result.StatusCode,
            ["code"] = error.Code,
        });
    }

    private void WarnDroppedNetwork(Exception? error)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        _logger.Warn(LogEvents.LogDroppedNetwork, new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            ["error"] = error?.Message ?? string.Empty,
        });
    }

    private void ReportUnhandled(Exception error)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        _logger.Error(LogEvents.LogWorkerUnhandled, new Dictionary<string, object?>(1, StringComparer.Ordinal)
        {
            ["error"] = error.GetType().FullName + ": " + error.Message,
        });
    }
}
