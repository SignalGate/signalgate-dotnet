using System;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class CheckCancellationTests
{
    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelled_token_throws_with_that_token_and_never_fails_open(bool failOpen)
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, logger, options => options.FailOpen = failOpen);
        await using ClosingScope closing = client.ClosesAtEnd();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(
            () => client.CheckAsync(StandardEvent.Create(), source.Token));

        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(logger.WithMessage("signalgate.check.failed_open"));
        TestClients.AssertMetrics(client, ("check_total", 1));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelling_during_the_request_throws_with_the_callers_token(bool failOpen)
    {
        var logger = new CapturingLogger();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.AllowVerdict)));
        SignalGateClient client = TestClients.Create(handler, logger, options =>
        {
            options.FailOpen = failOpen;
            options.CheckTimeoutMs = TestClients.HeldRequestTimeoutMs;
        });
        await using ClosingScope closing = client.ClosesAtEnd();
        using var source = new CancellationTokenSource();

        Task<CheckResult> check = client.CheckAsync(StandardEvent.Create(), source.Token);
        await handler.WhenEntered().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await source.CancelAsync();
        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(() => check);

        Assert.Equal(source.Token, error.CancellationToken);
        Assert.NotNull(error.InnerException);
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(logger.WithMessage("signalgate.check.failed_open"));
        Assert.Empty(logger.WithMessage("signalgate.http.timeout"));
        Assert.Empty(logger.WithMessage("signalgate.http.network_error"));
        Assert.Empty(logger.WithMessage("signalgate.http.response"));
        TestClients.AssertMetrics(client, ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Uncancelled_token_does_not_affect_the_check()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        using var source = new CancellationTokenSource();

        CheckResult result = await client.CheckAsync(StandardEvent.Create(), source.Token);

        Assert.Equal("allow", result.Action);
        Assert.False(result.FailedOpen);
    }
}
