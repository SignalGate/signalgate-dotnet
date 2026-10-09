using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogCloseTests
{
    private const int EventCount = 25;

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Close_returns_the_same_task_while_events_are_pending()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: Hold);
        await using ClosingScope closing = client.ClosesAtEnd();

        Task first;
        try
        {
            client.Log(StandardEvent.Create());
            first = client.CloseAsync(LogCounters.Flush);

            Assert.Same(first, client.CloseAsync());
            Assert.Same(first, client.CloseAsync(TimeSpan.Zero));
            Assert.False(first.IsCompleted);
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.WaitForCloseAsync(first, LogCounters.Flush);
        Assert.Same(first, client.CloseAsync());
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Eight_concurrent_closers_all_complete()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: Hold);
        await using ClosingScope closing = client.ClosesAtEnd();

        Task[] closes;
        try
        {
            for (int i = 0; i < EventCount; i++)
            {
                client.Log(StandardEvent.Create());
            }

            Task<Task>[] callers = Enumerable.Range(0, 8)
                .Select(_ => Task.Factory.StartNew(
                    () => client.CloseAsync(LogCounters.Flush),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();
            closes = await Task.WhenAll(callers).WaitAsync(LogCounters.Patience);
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.WaitForCloseAsync(Task.WhenAll(closes), LogCounters.Flush);

        Assert.All(closes, close => Assert.Same(closes[0], close));
        TestClients.AssertMetrics(client, ("log_enqueued_total", EventCount), ("log_sent_total", EventCount));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Dispose_delivers_queued_events_first()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);

        for (int i = 0; i < EventCount; i++)
        {
            client.Log(StandardEvent.Create());
        }

        await client.WaitForCloseAsync(Task.Run(client.Dispose, TestContext.Current.CancellationToken));

        Assert.Equal(EventCount, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", EventCount), ("log_sent_total", EventCount));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
        await client.CloseWithinLimitAsync();
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Dispose_async_delivers_queued_events_first()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);

        for (int i = 0; i < EventCount; i++)
        {
            client.Log(StandardEvent.Create());
        }

        await client.WaitForCloseAsync(client.DisposeAsync().AsTask());

        Assert.Equal(EventCount, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", EventCount), ("log_sent_total", EventCount));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Infinite_deadline_waits_for_delivery()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: Hold);
        await using ClosingScope closing = client.ClosesAtEnd();

        Task close;
        try
        {
            client.Log(StandardEvent.Create());
            close = client.CloseAsync(Timeout.InfiniteTimeSpan);
            await Task.Delay(100);

            Assert.False(close.IsCompleted);
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.WaitForCloseAsync(close, Timeout.InfiniteTimeSpan);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(-2)]
    [InlineData(-5000)]
    public async Task Other_negative_deadlines_are_rejected_and_logging_continues(int milliseconds)
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = client.CloseAsync(TimeSpan.FromMilliseconds(milliseconds));
        });
        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Very_large_deadline_is_accepted()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(TimeSpan.MaxValue);

        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Cancelled_close_token_ends_the_wait_at_once()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: Hold);
        await using ClosingScope closing = client.ClosesAtEnd();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        try
        {
            for (int i = 0; i < 3; i++)
            {
                client.Log(StandardEvent.Create());
            }

            await client.CloseWithinLimitAsync(Timeout.InfiniteTimeSpan, cancellationToken: source.Token);
        }
        finally
        {
            gate.TrySetResult();
        }

        TestClients.AssertMetrics(client, ("log_enqueued_total", 3), ("""log_dropped_total{reason="closed"}""", 3));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Close_from_a_logger_on_the_worker_returns_promptly()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();
        var innerClose = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.Hook = entry =>
        {
            if (entry.Message == "signalgate.http.response")
            {
                innerClose.TrySetResult(client.CloseAsync());
            }
        };

        client.Log(StandardEvent.Create());
        Task inner = await innerClose.Task.WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
        await inner.WaitAsync(TimeSpan.FromSeconds(1));

        client.Log(StandardEvent.Create());
        Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(StandardEvent.Create());
        });

        Task outer = client.CloseAsync();
        await client.WaitForCloseAsync(outer);

        Assert.NotSame(inner, outer);
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Dispose_from_a_logger_on_the_worker_does_not_deadlock()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.Hook = entry =>
        {
            if (entry.Message == "signalgate.http.response")
            {
                client.Dispose();
                disposed.TrySetResult();
            }
        };

        client.Log(StandardEvent.Create());
        await disposed.Task.WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
        await client.CloseWithinLimitAsync();

        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Close_from_a_handler_on_the_worker_lets_queued_events_drain()
    {
        SignalGateClient? client = null;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var innerClose = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(
            FakeStep.Gated(
                gate,
                FakeStep.Observe(
                    _ => innerClose.TrySetResult(client!.CloseAsync()),
                    FakeStep.Respond(200, ResponseBodies.LogAcknowledged))),
            FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient created = TestClients.Create(handler, configure: Hold);
        await using ClosingScope createdClosing = created.ClosesAtEnd();
        client = created;

        try
        {
            client.Log(StandardEvent.Create());
            client.Log(StandardEvent.Create());
            client.Log(StandardEvent.Create());
        }
        finally
        {
            gate.TrySetResult();
        }

        Task inner = await innerClose.Task.WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
        await inner.WaitAsync(TimeSpan.FromSeconds(1));
        await client.CloseWithinLimitAsync();

        Assert.True(inner.IsCompletedSuccessfully);
        Assert.Equal(3, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 3), ("log_sent_total", 3));
    }

    // For a test that holds a delivery at a gate: an attempt deadline that the held delivery never reaches.
    private static void Hold(SignalGateClientOptions options)
    {
        options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs;
    }
}
