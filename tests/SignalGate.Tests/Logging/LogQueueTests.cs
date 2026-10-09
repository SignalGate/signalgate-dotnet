using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogQueueTests
{
    private const string QueueFull = """log_dropped_total{reason="queue_full"}""";

    public static TheoryData<string> UnsupportedCustomValues => new()
    {
        "guid",
        "enum",
        "not a number",
        "infinity",
        "bytes",
        "cycle",
        "oversized",
    };

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Full_queue_drops_the_newest_events_and_reports_each_one()
    {
        var logger = new CapturingLogger();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, logger, HoldWithCapacity(1));
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            for (int i = 0; i < 19; i++)
            {
                client.Log(StandardEvent.Create());
            }

            TestClients.AssertMetrics(client, ("log_enqueued_total", 2), (QueueFull, 18));
            IReadOnlyList<LogEntry> errors = logger.WithMessage("signalgate.log.queue_full");
            Assert.Equal(18, errors.Count);
            Assert.All(errors, entry =>
            {
                Assert.Equal(CapturingLogger.ErrorLevel, entry.Level);
                Assert.Null(entry.Fields);
            });
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(2, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 2), ("log_sent_total", 2), (QueueFull, 18));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Queued_bytes_are_bounded()
    {
        const long Budget = 64 * 1024 * 1024;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: HoldWithCapacity(10_000));
        await using ClosingScope closing = client.ClosesAtEnd();
        SignalGateEvent large = StandardEvent.WithCustom(new Dictionary<string, object?> { ["pad"] = new string('x', 1_000_000) });
        int bodyLength = WireEncoder.Encode(large).Length;
        int fitting = (int)(Budget / bodyLength);
        const int Extra = 3;

        try
        {
            // The first event is picked up by the worker, so its bytes no longer count as queued.
            client.Log(large);
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            for (int i = 0; i < fitting + Extra; i++)
            {
                client.Log(large);
            }

            Assert.InRange(bodyLength, 1_000_000, 1_048_576);
            Assert.Equal((long)fitting * bodyLength, client.QueuedLogBytes);
            TestClients.AssertMetrics(client, ("log_enqueued_total", fitting + 1), (QueueFull, Extra));
        }
        finally
        {
            await client.CloseWithinLimitAsync(TimeSpan.Zero);
            gate.TrySetResult();
        }

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, client.QueuedLogBytes);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", fitting + 1),
            ("""log_dropped_total{reason="closed"}""", fitting + 1),
            (QueueFull, Extra));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Queued_bytes_can_fill_the_bound_exactly_and_the_next_event_is_dropped()
    {
        // Events of the largest size fill the queued-bytes bound exactly, so the last fitting event lands on it.
        const long Budget = 64 * 1024 * 1024;
        const int Fitting = 64;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: HoldWithCapacity(10_000));
        await using ClosingScope closing = client.ClosesAtEnd();
        SignalGateEvent largest = StandardEvent.WithBodyLength(WireEncoder.MaxBodyBytes);

        try
        {
            Assert.Equal(Budget / Fitting, WireEncoder.Encode(largest).Length);

            // The first event is picked up by the worker, so its bytes no longer count as queued.
            client.Log(largest);
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            for (int i = 0; i < Fitting; i++)
            {
                client.Log(largest);
            }

            Assert.Equal(Budget, client.QueuedLogBytes);
            TestClients.AssertMetrics(client, ("log_enqueued_total", Fitting + 1));

            client.Log(largest);

            Assert.Equal(Budget, client.QueuedLogBytes);
            TestClients.AssertMetrics(client, ("log_enqueued_total", Fitting + 1), (QueueFull, 1));
        }
        finally
        {
            await client.CloseWithinLimitAsync(TimeSpan.Zero);
            gate.TrySetResult();
        }

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, client.QueuedLogBytes);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", Fitting + 1),
            ("""log_dropped_total{reason="closed"}""", Fitting + 1),
            (QueueFull, 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Events_are_delivered_one_at_a_time_in_order()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        string[] users = Enumerable.Range(0, 20).Select(i => "user_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        foreach (string user in users)
        {
            client.Log(new SignalGateEvent(user, StandardEvent.Ip, StandardEvent.Method, StandardEvent.Timestamp, StandardEvent.Payload()));
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);

        string[] delivered = handler.Requests
            .Select(request =>
            {
                using JsonDocument document = JsonDocument.Parse(request.Body);
                return document.RootElement.GetProperty("user_id").GetString()!;
            })
            .ToArray();
        Assert.Equal(users, delivered);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 20), ("log_sent_total", 20));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_returns_while_delivery_is_stalled()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, configure: HoldWithCapacity(10_000));
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            for (int i = 0; i < 50; i++)
            {
                client.Log(StandardEvent.Create());
            }

            Assert.Equal(50, LogCounters.Enqueued(client));
            Assert.Equal(0, LogCounters.Sent(client));
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 50), ("log_sent_total", 50));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Null_event_is_reported_and_not_counted()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(null);
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        LogEntry entry = Assert.Single(logger.WithMessage("signalgate.log.invalid_event"));
        Assert.Equal(CapturingLogger.ErrorLevel, entry.Level);
        Assert.Equal("event is null", entry.Fields?["reason"]);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(client.Metrics.SnapshotFlat());
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(UnsupportedCustomValues))]
    public async Task Unsupported_custom_value_is_reported_and_not_counted(string caseName)
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.WithCustom(BuildCustom(caseName)));
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        LogEntry entry = Assert.Single(logger.WithMessage("signalgate.log.invalid_event"));
        Assert.Equal(CapturingLogger.ErrorLevel, entry.Level);
        Assert.False(string.IsNullOrEmpty(Assert.IsType<string>(entry.Fields?["reason"])));
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(client.Metrics.SnapshotFlat());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_after_close_is_ignored_silently()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CloseWithinLimitAsync();
        client.Log(StandardEvent.Create());
        client.Log(null);

        Assert.Equal(0, handler.CallCount);
        Assert.Empty(client.Metrics.SnapshotFlat());
        Assert.Empty(logger.Entries);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Throwing_logger_never_escapes_and_delivery_still_succeeds()
    {
        CapturingLogger logger = CapturingLogger.Throwing();
        using var handler = new FakeHttpHandler(
            FakeStep.Respond(200, ResponseBodies.AllowVerdict),
            FakeStep.Respond(503),
            FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff(), logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult verdict = await client.CheckAsync(StandardEvent.Create());
        client.Log(StandardEvent.Create());
        client.Log(null);
        client.Log(StandardEvent.WithCustom(new Dictionary<string, object?> { ["value"] = Guid.NewGuid() }));
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal("allow", verdict.Action);
        Assert.Equal(1, LogCounters.Sent(client));
        Assert.Equal(1, LogCounters.HttpErrors(client, "503"));
        Assert.Equal(1, client.Metrics.Get("check_success_total"));
        Assert.NotEmpty(logger.WithMessage("signalgate.http.response"));
        Assert.Equal(2, logger.WithMessage("signalgate.log.invalid_event").Count);
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Throwing_logger_on_a_full_queue_never_escapes()
    {
        CapturingLogger logger = CapturingLogger.Throwing();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, logger, HoldWithCapacity(1));
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            client.Log(StandardEvent.Create());
            client.Log(StandardEvent.Create());
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 2), ("log_sent_total", 2), (QueueFull, 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Slow_logger_on_one_thread_does_not_block_log_on_another()
    {
        var logger = new CapturingLogger();
        using var firstErrorEntered = new ManualResetEventSlim();
        using var releaseFirstError = new ManualResetEventSlim();
        int errorCalls = 0;
        logger.Hook = entry =>
        {
            if (entry.Level == CapturingLogger.ErrorLevel && Interlocked.Increment(ref errorCalls) == 1)
            {
                firstErrorEntered.Set();
                releaseFirstError.Wait(LogCounters.Patience);
            }
        };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, logger, HoldWithCapacity(1));
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            client.Log(StandardEvent.Create());

            Task threadA = StartOnOwnThread(() => client.Log(StandardEvent.Create()));
            Assert.True(firstErrorEntered.Wait(LogCounters.Patience));

            Task threadB = StartOnOwnThread(() => client.Log(StandardEvent.Create()));
            // Well within the time thread A stays blocked in the logger.
            await threadB.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(threadA.IsCompleted);
            releaseFirstError.Set();
            await threadA.WaitAsync(LogCounters.Patience);
        }
        finally
        {
            releaseFirstError.Set();
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 2), ("log_sent_total", 2), (QueueFull, 2));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Worker_does_not_inherit_the_callers_async_local_state()
    {
        var marker = new AsyncLocal<string?> { Value = "caller" };
        string? seenByHandler = "unset";
        using var handler = new FakeHttpHandler(FakeStep.Observe(
            _ => seenByHandler = marker.Value,
            FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(1, LogCounters.Sent(client));
        Assert.Null(seenByHandler);
        Assert.Equal("caller", marker.Value);
    }

    // Options for a test that holds the first delivery at a gate: the given queue capacity, and an attempt
    // deadline that the held delivery never reaches.
    private static Action<SignalGateClientOptions> HoldWithCapacity(int capacity)
    {
        return options =>
        {
            options.LogQueueCapacity = capacity;
            options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs;
        };
    }

    private static Task StartOnOwnThread(Action action)
    {
        return Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static Dictionary<string, object?> BuildCustom(string caseName)
    {
        switch (caseName)
        {
            case "guid":
                return new Dictionary<string, object?> { ["value"] = Guid.NewGuid() };
            case "enum":
                return new Dictionary<string, object?> { ["value"] = DayOfWeek.Friday };
            case "not a number":
                return new Dictionary<string, object?> { ["value"] = double.NaN };
            case "infinity":
                return new Dictionary<string, object?> { ["value"] = double.PositiveInfinity };
            case "bytes":
                return new Dictionary<string, object?> { ["value"] = new byte[] { 1, 2, 3 } };
            case "cycle":
                var cycle = new Dictionary<string, object?>();
                cycle["self"] = cycle;
                return new Dictionary<string, object?> { ["value"] = cycle };
            case "oversized":
                return new Dictionary<string, object?> { ["value"] = new string('a', 1_100_000) };
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "unknown case");
        }
    }
}
