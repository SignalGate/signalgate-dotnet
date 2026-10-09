using System;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogConservationTests
{
    // The close may or may not reach this deadline; every event is accounted for either way.
    private static readonly TimeSpan CloseDeadline = TimeSpan.FromSeconds(3);

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Every_accepted_event_is_accounted_for_after_close()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: options =>
        {
            options.LogQueueCapacity = 200;
            options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        Task close;
        try
        {
            for (int i = 0; i < 100; i++)
            {
                client.Log(StandardEvent.Create());
            }

            close = client.CloseAsync(CloseDeadline);
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.WaitForCloseAsync(close, CloseDeadline);

        Assert.Equal(100, LogCounters.Enqueued(client));
        Assert.Equal(0, LogCounters.Dropped(client, "queue_full"));
        LogCounters.AssertConserved(client);
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Every_call_is_either_accepted_or_dropped_as_queue_full()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: options =>
        {
            options.LogQueueCapacity = 10;
            options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        Task close;
        try
        {
            for (int i = 0; i < 100; i++)
            {
                client.Log(StandardEvent.Create());
            }

            close = client.CloseAsync(CloseDeadline);
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.WaitForCloseAsync(close, CloseDeadline);

        Assert.Equal(100, LogCounters.Enqueued(client) + LogCounters.Dropped(client, "queue_full"));
        Assert.InRange(LogCounters.Enqueued(client), 10, 11);
        LogCounters.AssertConserved(client);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Immediate_close_counts_the_attempt_in_flight_and_the_queued_events_as_closed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs);
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience);
            for (int i = 0; i < 5; i++)
            {
                client.Log(StandardEvent.Create());
            }

            await client.CloseWithinLimitAsync(TimeSpan.Zero);

            LogCounters.AssertConserved(client);
            TestClients.AssertMetrics(
                client,
                ("log_enqueued_total", 6),
                ("""log_dropped_total{reason="closed"}""", 6));
            Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
            Assert.Equal(1, handler.CallCount);

            await Task.Delay(300);

            Assert.Equal(1, handler.CallCount);
            TestClients.AssertMetrics(
                client,
                ("log_enqueued_total", 6),
                ("""log_dropped_total{reason="closed"}""", 6));
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Close_during_a_retry_wait_counts_the_event_as_closed()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.LogRetryBaseMs = 60_000);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await LogCounters.WaitUntilAsync(() => LogCounters.HttpErrors(client, "503") == 1);
        await client.CloseWithinLimitAsync(TimeSpan.FromMilliseconds(50));

        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", 1),
            ("""log_http_error_total{status="503"}""", 1),
            ("""log_dropped_total{reason="closed"}""", 1));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }
}
