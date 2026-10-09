using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using SignalGate.Tests.Logging;
using Xunit;

namespace SignalGate.Tests.Redaction;

public sealed class LogRedactionTests
{
    public static TheoryData<string> DeliveryOutcomes => new()
    {
        "sent",
        "client error",
        "server errors",
        "network errors",
        "timeouts",
    };

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(DeliveryOutcomes))]
    public async Task Delivery_outcome_never_reveals_the_key(string outcome)
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler(Step(outcome));
        SignalGateClientOptions options = probe.Options(handler, o => o.LogTimeoutMs = outcome == "timeouts" ? 50 : 1000);
        var client = new SignalGateClient(options, TestClients.ServiceAddress, new RecordingBackoff().Delay);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        AssertOutcome(outcome, client, probe.Logger, handler);
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Full_queue_never_reveals_the_key()
    {
        var probe = new KeyProbe();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClientOptions options = probe.Options(handler, o =>
        {
            o.LogQueueCapacity = 1;
            o.LogTimeoutMs = TestClients.HeldRequestTimeoutMs;
        });
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            for (int i = 0; i < 4; i++)
            {
                client.Log(StandardEvent.Create());
            }
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(3, probe.Logger.WithMessage("signalgate.log.queue_full").Count);
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Invalid_events_never_reveal_the_key()
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler();
        SignalGateClientOptions options = probe.Options(handler);
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(null);
        client.Log(StandardEvent.WithCustom(new Dictionary<string, object?> { ["value"] = Guid.NewGuid() }));
        client.Log(StandardEvent.WithCustom(new Dictionary<string, object?> { ["value"] = double.NaN }));
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(3, probe.Logger.WithMessage("signalgate.log.invalid_event").Count);
        Assert.Equal(0, handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Closing_with_events_in_flight_never_reveals_the_key()
    {
        var probe = new KeyProbe();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClientOptions options = probe.Options(handler, o => o.LogTimeoutMs = TestClients.HeldRequestTimeoutMs);
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            client.Log(StandardEvent.Create());
            await handler.WhenEntered(1).WaitAsync(LogCounters.Patience, TestContext.Current.CancellationToken);
            client.Log(StandardEvent.Create());
            client.Log(StandardEvent.Create());
            await client.CloseWithinLimitAsync(TimeSpan.Zero);
        }
        finally
        {
            gate.TrySetResult();
        }

        client.Log(StandardEvent.Create());
        probe.Capture<SignalGateConfigException>(() => _ = client.CheckAsync(StandardEvent.Create()));
        await client.WaitForCloseAsync(Task.Run(client.Dispose, TestContext.Current.CancellationToken));
        await client.WaitForCloseAsync(client.DisposeAsync().AsTask());

        Assert.Equal(3, client.Metrics.Get("log_dropped_total", new Dictionary<string, string> { ["reason"] = "closed" }));
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Unexpected_worker_failure_never_reveals_the_key()
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler(FakeStep.Respond(503), FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClientOptions options = probe.Options(handler);
        var backoff = new RecordingBackoff(() => new InvalidOperationException("wait failed"));
        var client = new SignalGateClient(options, TestClients.ServiceAddress, backoff.Delay);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        LogEntry line = Assert.Single(probe.Logger.WithMessage("signalgate.log.worker_unhandled"));
        Assert.Contains("wait failed", Assert.IsType<string>(line.Fields?["error"]), StringComparison.Ordinal);
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    private static FakeStep Step(string outcome)
    {
        return outcome switch
        {
            "sent" => FakeStep.Respond(200, ResponseBodies.LogAcknowledged),
            "client error" => FakeStep.Respond(400, ResponseBodies.BadRequest),
            "server errors" => FakeStep.Respond(502, ResponseBodies.HtmlBadGateway, ("X-Request-Id", ResponseBodies.HtmlBadGatewayRequestId)),
            "network errors" => FakeStep.Throw(() => new HttpRequestException("connection refused")),
            "timeouts" => FakeStep.DelayUntilCancelled(),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unknown outcome"),
        };
    }

    // Confirms that the scenario took the intended path.
    private static void AssertOutcome(string outcome, SignalGateClient client, CapturingLogger logger, FakeHttpHandler handler)
    {
        long sent = client.Metrics.Get("log_sent_total");
        long exhausted = client.Metrics.Get("log_dropped_total", new Dictionary<string, string> { ["reason"] = "retry_exhausted" });
        Assert.Equal(1, client.Metrics.Get("log_enqueued_total"));
        switch (outcome)
        {
            case "sent":
                Assert.Equal(1, sent);
                Assert.Equal(1, handler.CallCount);
                break;
            case "client error":
                Assert.Equal(1, exhausted);
                Assert.Equal(1, handler.CallCount);
                Assert.Single(logger.WithMessage("signalgate.log.dropped_4xx"));
                break;
            case "server errors":
                Assert.Equal(1, exhausted);
                Assert.Equal(4, handler.CallCount);
                Assert.Equal(4, logger.WithMessage("signalgate.http.response").Count);
                break;
            case "network errors":
                Assert.Equal(1, exhausted);
                Assert.Equal(4, handler.CallCount);
                Assert.Equal(4, logger.WithMessage("signalgate.http.network_error").Count);
                Assert.Single(logger.WithMessage("signalgate.log.dropped_network"));
                break;
            case "timeouts":
                Assert.Equal(1, exhausted);
                Assert.Equal(4, handler.CallCount);
                Assert.Equal(4, logger.WithMessage("signalgate.http.timeout").Count);
                Assert.Single(logger.WithMessage("signalgate.log.dropped_network"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unknown outcome");
        }
    }
}
