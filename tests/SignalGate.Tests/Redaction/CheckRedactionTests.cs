using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Redaction;

public sealed class CheckRedactionTests
{
    public static TheoryData<string, bool> Failures => new()
    {
        { "timeout", true },
        { "timeout", false },
        { "network", true },
        { "network", false },
        { "server error", true },
        { "server error", false },
        { "malformed", true },
        { "malformed", false },
        { "redirected", true },
        { "redirected", false },
    };

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Successful_check_never_reveals_the_key()
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.EveryVerdictField));
        SignalGateClientOptions options = probe.Options(handler);
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult verdict = await client.CheckAsync(StandardEvent.Create());
        await client.CloseWithinLimitAsync();

        Assert.Equal("admin_alert", verdict.Action);
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(1);
        Assert.Single(probe.Logger.WithMessage("signalgate.http.response"));
        probe.AssertKeyNeverAppears(client, options);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(Failures))]
    public async Task Failed_check_never_reveals_the_key(string failure, bool failOpen)
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler(Step(failure));
        SignalGateClientOptions options = probe.Options(handler, o =>
        {
            o.FailOpen = failOpen;
            if (failure == "timeout")
            {
                o.CheckTimeoutMs = 50;
            }
        });
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        if (failOpen)
        {
            CheckResult verdict = await client.CheckAsync(StandardEvent.Create()).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(verdict.FailedOpen);
            LogEntry warning = Assert.Single(probe.Logger.WithMessage("signalgate.check.failed_open"));
            Assert.False(string.IsNullOrEmpty(Assert.IsType<string>(warning.Fields?["error"])));
        }
        else
        {
            SignalGateException error = await probe.CaptureAsync<SignalGateException>(() => client.CheckAsync(StandardEvent.Create()));
            Assert.False(string.IsNullOrEmpty(error.Message));
        }

        await client.CloseWithinLimitAsync();

        Assert.Equal(1, client.Metrics.Get("check_total"));
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(1);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejected_check_never_reveals_the_key(bool failOpen)
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler(FakeStep.Respond(401, ResponseBodies.Unauthorized));
        SignalGateClientOptions options = probe.Options(handler, o => o.FailOpen = failOpen);
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await probe.CaptureAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));
        await client.CloseWithinLimitAsync();

        Assert.Equal("[401] UNAUTHORIZED: Invalid API key", error.Message);
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(1);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Cancelled_check_never_reveals_the_key()
    {
        var probe = new KeyProbe();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.AllowVerdict)));
        SignalGateClientOptions options = probe.Options(handler, o => o.CheckTimeoutMs = TestClients.HeldRequestTimeoutMs);
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();
        using var cancellation = new CancellationTokenSource();

        try
        {
            Task<CheckResult> pending = client.CheckAsync(StandardEvent.Create(), cancellation.Token);
            await handler.WhenEntered(1).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            await probe.CaptureAsync<OperationCanceledException>(() => pending);
            await probe.CaptureAsync<OperationCanceledException>(
                () => client.CheckAsync(StandardEvent.Create(), new CancellationToken(canceled: true)));
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync();

        Assert.Equal(2, probe.Errors.Count);
        KeyProbe.AssertKeyWasSent(handler);
        probe.AssertRequestLinesAreMasked(handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Caller_errors_never_reveal_the_key()
    {
        var probe = new KeyProbe();
        using var handler = new FakeHttpHandler();
        SignalGateClientOptions options = probe.Options(handler);
        var client = new SignalGateClient(options);
        await using ClosingScope closing = client.ClosesAtEnd();

        probe.Capture<ArgumentNullException>(() => _ = client.CheckAsync(null!));
        probe.Capture<ArgumentException>(() => _ = client.CheckAsync(
            StandardEvent.WithCustom(new Dictionary<string, object?> { ["value"] = Guid.NewGuid() })));
        probe.Capture<ArgumentOutOfRangeException>(() => _ = client.CloseAsync(TimeSpan.FromMilliseconds(-2)));
        await client.CloseWithinLimitAsync();
        probe.Capture<SignalGateConfigException>(() => _ = client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(4, probe.Errors.Count);
        Assert.Equal(0, handler.CallCount);
        probe.AssertKeyNeverAppears(client, options);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\0")]
    [InlineData("é")]
    public void Rejected_key_never_appears_in_the_configuration_error(string suffix)
    {
        using var handler = new FakeHttpHandler();
        var options = new SignalGateClientOptions { ApiKey = KeyProbe.Key + suffix, HttpHandler = handler };

        SignalGateConfigException error = Assert.Throws<SignalGateConfigException>(() => new SignalGateClient(options));

        Assert.Equal("api_key contains characters that are not allowed in an HTTP header", error.Message);
        KeyProbe.AssertExceptionIsClean(error);
        Assert.DoesNotContain(KeyProbe.Key, options.ToString(), StringComparison.Ordinal);
    }

    private static FakeStep Step(string failure)
    {
        return failure switch
        {
            "timeout" => FakeStep.DelayUntilCancelled(),
            "network" => FakeStep.Throw(() => new HttpRequestException("connection refused")),
            "server error" => FakeStep.Respond(502, ResponseBodies.HtmlBadGateway, ("X-Request-Id", ResponseBodies.HtmlBadGatewayRequestId)),
            "malformed" => FakeStep.Respond(200, """{"ok":true}"""),
            "redirected" => FakeStep.RespondAfterRedirectTo(new Uri("https://elsewhere.example.test/v0/check"), 200, ResponseBodies.AllowVerdict),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "unknown failure"),
        };
    }
}
