using System;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class RedirectTests
{
    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    public async Task Redirect_status_is_thrown_as_a_server_error_even_with_fail_open(int status)
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(status, "", ("Location", "https://elsewhere.example.test/v0/check")));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.FailOpen = true);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, ("""check_error_total{type="ServerError"}""", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Followed_redirect_is_a_network_error_when_fail_open_is_off()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(
            FakeStep.RespondAfterRedirectTo(new Uri("https://elsewhere.example.test/v0/check"), 200, ResponseBodies.AllowVerdict));
        SignalGateClient client = TestClients.Create(handler, logger, options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateNetworkException error = await Assert.ThrowsAsync<SignalGateNetworkException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal("the response was redirected; redirects are not followed", error.Message);
        LogEntry networkError = Assert.Single(logger.WithMessage("signalgate.http.network_error"));
        Assert.Equal(error.Message, networkError.Fields?["error"]);
        TestClients.AssertMetrics(client, ("""check_error_total{type="NetworkError"}""", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Followed_redirect_fails_open_as_a_network_error_by_default()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(
            FakeStep.RespondAfterRedirectTo(new Uri("https://elsewhere.example.test/v0/check"), 200, ResponseBodies.AllowVerdict));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        TestClients.AssertFailedOpen(result);
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.check.failed_open"));
        Assert.Equal("NetworkError", warning.Fields?["error_type"]);
        TestClients.AssertMetrics(
            client,
            ("check_failed_open_total", 1),
            ("""check_error_total{type="NetworkError"}""", 1),
            ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Response_without_a_request_message_is_processed_normally()
    {
        using var handler = new FakeHttpHandler(FakeStep.RespondWithoutRequestMessage(200, ResponseBodies.BlockVerdictInEnvelope));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create());

        Assert.Equal("block", result.Action);
        Assert.False(result.FailedOpen);
        TestClients.AssertMetrics(client, ("check_success_total", 1), ("check_total", 1));
    }
}
