using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogRetryTests
{
    private const string Status503 = """log_http_error_total{status="503"}""";
    private const string NetworkErrors = """log_http_error_total{status="network"}""";
    private const string RetryExhausted = """log_dropped_total{reason="retry_exhausted"}""";

    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Event_is_retried_after_503_with_the_same_idempotency_key_and_then_sent()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503), FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(handler, backoff);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        IReadOnlyList<RecordedRequest> requests = handler.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].Headers["Idempotency-Key"], requests[1].Headers["Idempotency-Key"]);
        Assert.NotEqual(requests[0].Headers["X-Request-Id"], requests[1].Headers["X-Request-Id"]);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(200) }, backoff.Delays);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (Status503, 1), ("log_sent_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Flat_snapshot_after_one_retry_holds_exactly_three_counters()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503), FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff());
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(
            """{"log_enqueued_total":1,"log_http_error_total{status=\"503\"}":1,"log_sent_total":1}""",
            JsonSerializer.Serialize(client.Metrics.SnapshotFlat(), RelaxedJson));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Event_is_dropped_after_four_503_responses_with_doubling_delays()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(handler, backoff, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(4, handler.CallCount);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800) },
            backoff.Delays);
        Assert.Single(handler.Requests.Select(request => request.Headers["Idempotency-Key"]).Distinct());
        Assert.Equal(4, handler.Requests.Select(request => request.Headers["X-Request-Id"]).Distinct().Count());
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (Status503, 4), (RetryExhausted, 1));
        Assert.Empty(logger.WithMessage("signalgate.log.dropped_4xx"));
        Assert.Empty(logger.WithMessage("signalgate.log.dropped_network"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Client_error_is_not_retried_and_is_reported_once()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(400, ResponseBodies.BadRequest));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(handler, backoff, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(backoff.Delays);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", 1),
            ("""log_http_error_total{status="400"}""", 1),
            (RetryExhausted, 1));
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.log.dropped_4xx"));
        Assert.Equal(CapturingLogger.WarnLevel, warning.Level);
        Assert.NotNull(warning.Fields);
        Assert.Equal(400, Assert.IsType<int>(warning.Fields["status"]));
        Assert.Equal("BAD_REQUEST", warning.Fields["code"]);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ResponseBodies.ClientErrorCodes), MemberType = typeof(ResponseBodies))]
    public async Task Every_client_error_code_is_reported_with_its_status(int status, string body, string code)
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(status, body));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff(), logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, LogCounters.HttpErrors(client, status.ToString(CultureInfo.InvariantCulture)));
        Assert.Equal(1, LogCounters.Dropped(client, "retry_exhausted"));
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.log.dropped_4xx"));
        Assert.Equal(status, warning.Fields?["status"]);
        Assert.Equal(code, warning.Fields?["code"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Timeouts_are_retried_then_dropped_as_network_errors()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.DelayUntilCancelled());
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(
            handler,
            backoff,
            logger,
            options => options.LogTimeoutMs = 50);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(4, handler.CallCount);
        Assert.Equal(3, backoff.Delays.Count);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (NetworkErrors, 4), (RetryExhausted, 1));
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.log.dropped_network"));
        Assert.Equal(CapturingLogger.WarnLevel, warning.Level);
        Assert.Equal("request timed out after 50 ms", warning.Fields?["error"]);
        Assert.Equal(4, logger.WithMessage("signalgate.http.timeout").Count);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Network_failures_are_retried_then_dropped()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Throw(() => new System.Net.Http.HttpRequestException("connection refused")));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff(), logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(4, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (NetworkErrors, 4), (RetryExhausted, 1));
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.log.dropped_network"));
        Assert.Equal("network error: connection refused", warning.Fields?["error"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Non_envelope_bad_gateway_is_retried()
    {
        using var handler = new FakeHttpHandler(
            FakeStep.Respond(502, ResponseBodies.HtmlBadGateway, ("X-Request-Id", ResponseBodies.HtmlBadGatewayRequestId)),
            FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff());
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(2, handler.CallCount);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", 1),
            ("""log_http_error_total{status="502"}""", 1),
            ("log_sent_total", 1));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(204)]
    public async Task Any_success_status_counts_as_sent_and_the_body_is_ignored(int status)
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(status, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff());
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), ("log_sent_total", 1));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(301)]
    [InlineData(302)]
    public async Task Redirect_status_is_dropped_without_retry(int status)
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(status, "", ("Location", "https://elsewhere.test/")));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff(), logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", 1),
            ("log_http_error_total{status=\"" + status.ToString(CultureInfo.InvariantCulture) + "\"}", 1),
            (RetryExhausted, 1));
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.log.dropped_4xx"));
        Assert.Equal(status, warning.Fields?["status"]);
        Assert.Equal("", warning.Fields?["code"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Followed_redirect_is_a_network_error_and_is_retried()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(
            FakeStep.RespondAfterRedirectTo(new Uri("https://elsewhere.test/v0/log"), 200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff(), logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(4, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (NetworkErrors, 4), (RetryExhausted, 1));
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.log.dropped_network"));
        Assert.Equal("the response was redirected; redirects are not followed", warning.Fields?["error"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Zero_retries_means_a_single_attempt()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(
            handler,
            backoff,
            configure: options => options.LogMaxRetries = 0);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(backoff.Delays);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (Status503, 1), (RetryExhausted, 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Zero_retry_base_retries_without_waiting()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(
            handler,
            backoff,
            configure: options => options.LogRetryBaseMs = 0);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(4, handler.CallCount);
        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero }, backoff.Delays);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Large_retry_settings_produce_clamped_delays_without_overflow()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        var backoff = new RecordingBackoff();
        SignalGateClient client = TestClients.CreateWithBackoff(handler, backoff, configure: options =>
        {
            options.LogRetryBaseMs = int.MaxValue;
            options.LogMaxRetries = 40;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        TimeSpan max = TimeSpan.FromMilliseconds(4_294_967_294);
        Assert.Equal(41, handler.CallCount);
        Assert.Equal(40, backoff.Delays.Count);
        Assert.All(backoff.Delays, delay => Assert.InRange(delay, TimeSpan.FromMilliseconds(1), max));
        Assert.Equal(TimeSpan.FromMilliseconds(int.MaxValue), backoff.Delays[0]);
        Assert.All(backoff.Delays.Skip(1), delay => Assert.Equal(max, delay));
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (Status503, 41), (RetryExhausted, 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Failure_between_attempts_drops_the_event_and_the_next_event_is_still_sent()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(503), FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        var backoff = new RecordingBackoff(() => new InvalidOperationException("backoff failure"));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, backoff, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(2, handler.CallCount);
        LogEntry error = Assert.Single(logger.WithMessage("signalgate.log.worker_unhandled"));
        Assert.Equal(CapturingLogger.ErrorLevel, error.Level);
        Assert.Contains("backoff failure", Assert.IsType<string>(error.Fields?["error"]), StringComparison.Ordinal);
        TestClients.AssertMetrics(
            client,
            ("log_enqueued_total", 2),
            (Status503, 1),
            (RetryExhausted, 1),
            ("log_sent_total", 1));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Public_constructor_waits_between_attempts_with_real_delays()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.LogRetryBaseMs = 1);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(4, handler.CallCount);
        TestClients.AssertMetrics(client, ("log_enqueued_total", 1), (Status503, 4), (RetryExhausted, 1));
    }
}
