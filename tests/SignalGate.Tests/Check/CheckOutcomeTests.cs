using System.Net.Http;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class CheckOutcomeTests
{
    private const string ErrorTypeServer = """check_error_total{type="ServerError"}""";
    private const string ErrorTypeNetwork = """check_error_total{type="NetworkError"}""";
    private const string ErrorTypeMalformed = """check_error_total{type="MalformedResponse"}""";

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Allow_verdict_is_returned()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.AllowVerdict));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        AssertResult(result, "allow", 0.0, "req_1", "acme", "2026-04-01T13:08:50Z", 812);
        TestClients.AssertMetrics(client, ("check_success_total", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Block_verdict_inside_the_envelope_with_an_integer_score_is_returned()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.BlockVerdictInEnvelope));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        AssertResult(result, "block", 1.0, "req_happy_1", "t_fake", "2026-04-01T13:08:50Z", 812);
        TestClients.AssertMetrics(client, ("check_success_total", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Every_verdict_field_is_returned()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.EveryVerdictField));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        AssertResult(result, "admin_alert", 0.25, "req_all_fields", "tenant_all_fields", "2026-07-06T12:00:00Z", 1234);
        Assert.Empty(logger.WithMessage("signalgate.check.unknown_action"));
        TestClients.AssertMetrics(client, ("check_success_total", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Empty_data_gives_every_default_and_one_unknown_action_warning()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.EmptyData));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        AssertResult(result, "", 0.0, "", "", "", 0);
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.check.unknown_action"));
        Assert.Equal(CapturingLogger.WarnLevel, warning.Level);
        Assert.Equal("", warning.Fields?["action"]);
        TestClients.AssertMetrics(client, ("check_success_total", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Unknown_action_is_returned_verbatim_with_one_warning()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.UnknownAction));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        Assert.Equal("not_a_known_action", result.Action);
        Assert.False(result.FailedOpen);
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.check.unknown_action"));
        Assert.Equal(CapturingLogger.WarnLevel, warning.Level);
        Assert.Equal("not_a_known_action", warning.Fields?["action"]);
        TestClients.AssertMetrics(client, ("check_success_total", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Network_failure_fails_open()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Throw(() => new HttpRequestException("connection refused")));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        Assert.Equal(1, handler.CallCount);
        AssertSingleFailedOpenWarning(logger, "NetworkError");
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeNetwork, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Bad_gateway_fails_open()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(502));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        Assert.Equal(1, handler.CallCount);
        AssertSingleFailedOpenWarning(logger, "ServerError");
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeServer, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Success_without_data_fails_open()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, """{"ok":true}"""));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        Assert.Equal(1, handler.CallCount);
        LogEntry warning = AssertSingleFailedOpenWarning(logger, "MalformedResponse");
        Assert.Equal("response envelope missing data field", warning.Fields?["error"]);
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeMalformed, 1), ("check_total", 1));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ResponseBodies.ClientErrors), MemberType = typeof(ResponseBodies))]
    public async Task Client_error_is_thrown_even_with_fail_open(int status, string body, string code, string message, string requestId)
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(status, body));
        SignalGateClient client = TestClients.Create(handler, logger, options => options.FailOpen = true);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.ServerMessage);
        Assert.Equal($"[{status}] {code}: {message}", error.Message);
        Assert.Equal(requestId, error.RequestId);
        Assert.Null(error.Details);
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(logger.WithMessage("signalgate.check.failed_open"));
        TestClients.AssertMetrics(client, (ErrorTypeServer, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Bad_gateway_without_an_envelope_throws_with_the_response_request_id_when_fail_open_is_off()
    {
        using var handler = new FakeHttpHandler(
            FakeStep.Respond(502, ResponseBodies.HtmlBadGateway, ("X-Request-Id", ResponseBodies.HtmlBadGatewayRequestId)));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal("", error.Code);
        Assert.Equal("", error.ServerMessage);
        Assert.Equal("req_hdr", error.RequestId);
        Assert.Equal("[502] : ", error.Message);
        TestClients.AssertMetrics(client, (ErrorTypeServer, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Bad_gateway_without_an_envelope_fails_open_by_default()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(
            FakeStep.Respond(502, ResponseBodies.HtmlBadGateway, ("X-Request-Id", ResponseBodies.HtmlBadGatewayRequestId)));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        LogEntry warning = AssertSingleFailedOpenWarning(logger, "ServerError");
        Assert.Equal("[502] : ", warning.Fields?["error"]);
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeServer, 1), ("check_total", 1));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ResponseBodies.MalformedSuccessBodies), MemberType = typeof(ResponseBodies))]
    public async Task Malformed_success_throws_when_fail_open_is_off(string body)
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, body));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(200, error.StatusCode);
        Assert.Equal("MALFORMED_RESPONSE", error.Code);
        Assert.Equal("response envelope missing data field", error.ServerMessage);
        Assert.Equal("[200] MALFORMED_RESPONSE: response envelope missing data field", error.Message);
        Assert.Equal(Assert.Single(handler.Requests).Headers["X-Request-Id"], error.RequestId);
        TestClients.AssertMetrics(client, (ErrorTypeMalformed, 1), ("check_total", 1));
        Assert.Equal(0, client.Metrics.Get("check_failed_open_total"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Malformed_success_reports_the_response_request_id_when_present()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(201, """{"ok":true}""", ("X-Request-Id", "req_from_header")));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(201, error.StatusCode);
        Assert.Equal("req_from_header", error.RequestId);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Service_unavailable_is_sent_exactly_once()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        Assert.Equal(1, handler.CallCount);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Service_unavailable_is_thrown_as_a_server_error_when_fail_open_is_off()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(503));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, (ErrorTypeServer, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Network_failure_is_thrown_with_its_cause_when_fail_open_is_off()
    {
        var cause = new HttpRequestException("connection refused");
        using var handler = new FakeHttpHandler(FakeStep.Throw(() => cause));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateNetworkException error = await Assert.ThrowsAsync<SignalGateNetworkException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Same(cause, error.InnerException);
        Assert.Equal("network error: connection refused", error.Message);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, (ErrorTypeNetwork, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Any_handler_exception_counts_as_a_network_failure()
    {
        using var handler = new FakeHttpHandler(FakeStep.Throw(() => new System.InvalidOperationException("handler bug")));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeNetwork, 1), ("check_total", 1));
    }

    private static LogEntry AssertSingleFailedOpenWarning(CapturingLogger logger, string errorType)
    {
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.check.failed_open"));
        Assert.Equal(CapturingLogger.WarnLevel, warning.Level);
        Assert.Equal(errorType, warning.Fields?["error_type"]);
        Assert.IsType<string>(warning.Fields?["error"]);
        return warning;
    }

    private static void AssertResult(
        CheckResult result,
        string action,
        double score,
        string requestId,
        string tenantId,
        string timestamp,
        long processingTimeUs)
    {
        Assert.Equal(action, result.Action);
        Assert.Equal(score, result.Score);
        Assert.Equal(requestId, result.RequestId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(processingTimeUs, result.ProcessingTimeUs);
        Assert.False(result.FailedOpen);
    }
}
