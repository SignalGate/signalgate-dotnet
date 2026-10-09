using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

// A handler that ignores cancellation cannot hold a delivery attempt or a close past its deadline.
[Collection(TimingGroup.Name)]
public sealed class LogUnresponsiveHandlerTests
{
    private const string Closed = """log_dropped_total{reason="closed"}""";

    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(2);

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Zero_deadline_close_returns_promptly_and_counts_the_attempt_in_flight_as_closed()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TrackedContent(ResponseBodies.LogAcknowledged);
        using var handler = new FakeHttpHandler(FakeStep.IgnoringCancellation(
            release.Task,
            FakeStep.Observe(_ => released.TrySetResult(), FakeStep.RespondWithContent(200, () => late))));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs);
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            client.Log(StandardEvent.Create());
            client.Log(StandardEvent.Create());

            var elapsed = Stopwatch.StartNew();
            await client.CloseWithinLimitAsync(TimeSpan.Zero);
            elapsed.Stop();

            Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Prompt);
            Assert.False(released.Task.IsCompleted);
            Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
            Assert.Equal(1, handler.CallCount);
            TestClients.AssertMetrics(client, ("log_enqueued_total", 3), (Closed, 3));
        }
        finally
        {
            release.TrySetResult();
        }

        await released.Task.WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
        await LogCounters.WaitUntilAsync(() => late.IsDisposed);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 3), (Closed, 3));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Each_attempt_ends_at_the_log_timeout()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.IgnoringCancellation(release.Task, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(handler, backoff, configure: options =>
        {
            options.LogTimeoutMs = 50;
            options.LogMaxRetries = 1;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            var elapsed = Stopwatch.StartNew();
            client.Log(StandardEvent.Create());
            await LogCounters.WaitUntilAsync(() => LogCounters.Dropped(client, "retry_exhausted") == 1);
            elapsed.Stop();

            Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Prompt);
            Assert.Equal(2, handler.CallCount);
            Assert.Single(backoff.Delays);
            TestClients.AssertMetrics(
                client,
                ("log_enqueued_total", 1),
                ("""log_http_error_total{status="network"}""", 2),
                ("""log_dropped_total{reason="retry_exhausted"}""", 1));
        }
        finally
        {
            release.TrySetResult();
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
        Assert.Equal(0, LogCounters.Sent(client));
    }
}
