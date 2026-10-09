using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class CheckLoggingTests
{
    private static readonly string[] LoggedHeaderNames = ["Authorization", "Content-Type", "Idempotency-Key", "User-Agent", "X-Request-Id"];

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Request_event_lists_the_headers_with_the_authorization_masked()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        RecordedRequest sent = Assert.Single(handler.Requests);
        LogEntry entry = Assert.Single(logger.WithMessage("signalgate.http.request"));
        Assert.Equal(CapturingLogger.DebugLevel, entry.Level);
        Assert.NotNull(entry.Fields);
        Assert.Equal("https://api.signalgate.ai/v0/check", entry.Fields["url"]);
        Assert.Equal(sent.Headers["X-Request-Id"], entry.Fields["request_id"]);
        IReadOnlyDictionary<string, string> headers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(entry.Fields["headers"]);
        Assert.Equal(LoggedHeaderNames, headers.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal("Bearer ***REDACTED***", headers["Authorization"]);
        Assert.Equal(sent.Headers["User-Agent"], headers["User-Agent"]);
        Assert.Equal(sent.Headers["X-Request-Id"], headers["X-Request-Id"]);
        Assert.Equal(sent.Headers["Idempotency-Key"], headers["Idempotency-Key"]);
        Assert.Equal("application/json", headers["Content-Type"]);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(503)]
    public async Task Response_event_carries_the_status_as_an_integer(int status)
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(status, ResponseBodies.AllowVerdict));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            await client.CheckAsync(StandardEvent.Create());
        }
        catch (SignalGateServerException)
        {
        }

        LogEntry entry = Assert.Single(logger.WithMessage("signalgate.http.response"));
        Assert.Equal(CapturingLogger.DebugLevel, entry.Level);
        Assert.Equal("https://api.signalgate.ai/v0/check", entry.Fields?["url"]);
        Assert.Equal(Assert.Single(handler.Requests).Headers["X-Request-Id"], entry.Fields?["request_id"]);
        Assert.Equal(status, Assert.IsType<int>(entry.Fields?["status"]));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Network_failure_event_carries_the_error()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Throw(() => new HttpRequestException("connection refused")));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        LogEntry entry = Assert.Single(logger.WithMessage("signalgate.http.network_error"));
        Assert.Equal(CapturingLogger.WarnLevel, entry.Level);
        Assert.Equal("https://api.signalgate.ai/v0/check", entry.Fields?["url"]);
        Assert.Equal(Assert.Single(handler.Requests).Headers["X-Request-Id"], entry.Fields?["request_id"]);
        Assert.Equal("network error: connection refused", entry.Fields?["error"]);
        Assert.Empty(logger.WithMessage("signalgate.http.response"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Logger_that_always_throws_never_escapes()
    {
        CapturingLogger logger = CapturingLogger.Throwing();
        using var handler = new FakeHttpHandler(
            FakeStep.Respond(200, ResponseBodies.AllowVerdict),
            FakeStep.Respond(200, ResponseBodies.UnknownAction),
            FakeStep.Respond(502),
            FakeStep.Throw(() => new HttpRequestException("connection refused")),
            FakeStep.Respond(200, """{"ok":true}"""));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult allowed = await client.CheckAsync(StandardEvent.Create());
        CheckResult unknown = await client.CheckAsync(StandardEvent.Create());
        CheckResult degraded = await client.CheckAsync(StandardEvent.Create());
        CheckResult unreachable = await client.CheckAsync(StandardEvent.Create());
        CheckResult malformed = await client.CheckAsync(StandardEvent.Create());

        Assert.Equal("allow", allowed.Action);
        Assert.Equal("not_a_known_action", unknown.Action);
        TestClients.AssertFailedOpen(degraded);
        TestClients.AssertFailedOpen(unreachable);
        TestClients.AssertFailedOpen(malformed);
        Assert.NotEmpty(logger.Entries);
        Assert.Equal(5, client.Metrics.Get("check_total"));
        Assert.Equal(2, client.Metrics.Get("check_success_total"));
        Assert.Equal(3, client.Metrics.Get("check_failed_open_total"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Logger_that_throws_does_not_change_a_thrown_error()
    {
        CapturingLogger logger = CapturingLogger.Throwing();
        using var handler = new FakeHttpHandler(FakeStep.Respond(401, ResponseBodies.Unauthorized));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal("UNAUTHORIZED", error.Code);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task No_op_logger_receives_nothing_and_changes_nothing()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(502));
        SignalGateClient client = TestClients.Create(
            handler,
            configure: options => options.Logger = NoopSignalGateLogger.Instance);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
    }
}
