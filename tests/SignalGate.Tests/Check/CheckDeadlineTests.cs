using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

[Collection(TimingGroup.Name)]
public sealed class CheckDeadlineTests
{
    private const string ErrorTypeTimeout = """check_error_total{type="TimeoutError"}""";

    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Deadline_fails_open_by_default()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.DelayUntilCancelled());
        SignalGateClient client = TestClients.Create(handler, logger, options => options.CheckTimeoutMs = 50);
        await using ClosingScope closing = client.ClosesAtEnd();

        CheckResult result = await client.CheckAsync(StandardEvent.Create()).WaitAsync(Patience, TestContext.Current.CancellationToken);

        TestClients.AssertFailedOpen(result);
        Assert.Equal(1, handler.CallCount);
        LogEntry warning = Assert.Single(logger.WithMessage("signalgate.check.failed_open"));
        Assert.Equal(CapturingLogger.WarnLevel, warning.Level);
        Assert.Equal("TimeoutError", warning.Fields?["error_type"]);
        Assert.Equal("request timed out after 50 ms", warning.Fields?["error"]);
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeTimeout, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Deadline_throws_a_timeout_when_fail_open_is_off()
    {
        using var handler = new FakeHttpHandler(FakeStep.DelayUntilCancelled());
        SignalGateClient client = TestClients.Create(handler, configure: options =>
        {
            options.CheckTimeoutMs = 50;
            options.FailOpen = false;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateTimeoutException error = await Assert.ThrowsAsync<SignalGateTimeoutException>(
            () => client.CheckAsync(StandardEvent.Create()).WaitAsync(Patience, TestContext.Current.CancellationToken));

        Assert.Equal("request timed out after 50 ms", error.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, (ErrorTypeTimeout, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Deadline_is_reported_as_an_http_timeout()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.DelayUntilCancelled());
        SignalGateClient client = TestClients.Create(handler, logger, options => options.CheckTimeoutMs = 50);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create()).WaitAsync(Patience, TestContext.Current.CancellationToken);

        LogEntry timeout = Assert.Single(logger.WithMessage("signalgate.http.timeout"));
        Assert.Equal(CapturingLogger.WarnLevel, timeout.Level);
        Assert.Equal("https://api.signalgate.ai/v0/check", timeout.Fields?["url"]);
        Assert.Equal(Assert.Single(handler.Requests).Headers["X-Request-Id"], timeout.Fields?["request_id"]);
        Assert.Empty(logger.WithMessage("signalgate.http.response"));
        Assert.Empty(logger.WithMessage("signalgate.http.network_error"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Deadline_covers_a_slow_response_body()
    {
        using var handler = new FakeHttpHandler(FakeStep.RespondWithContent(200, () => new NeverEndingContent()));
        SignalGateClient client = TestClients.Create(handler, configure: options =>
        {
            options.CheckTimeoutMs = 50;
            options.FailOpen = false;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        await Assert.ThrowsAsync<SignalGateTimeoutException>(
            () => client.CheckAsync(StandardEvent.Create()).WaitAsync(Patience, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Deadline_fails_open_while_a_handler_that_ignores_cancellation_is_still_running()
    {
        var logger = new CapturingLogger();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TrackedContent(ResponseBodies.AllowVerdict);
        using var handler = new FakeHttpHandler(FakeStep.IgnoringCancellation(
            release.Task,
            FakeStep.Observe(_ => released.TrySetResult(), FakeStep.RespondWithContent(200, () => late))));
        SignalGateClient client = TestClients.Create(handler, logger, options => options.CheckTimeoutMs = 50);
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            var elapsed = Stopwatch.StartNew();
            CheckResult result = await client.CheckAsync(StandardEvent.Create()).WaitAsync(Patience, TestContext.Current.CancellationToken);
            elapsed.Stop();

            TestClients.AssertFailedOpen(result);
            Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Prompt);
            Assert.False(released.Task.IsCompleted);
            Assert.Equal("TimeoutError", Assert.Single(logger.WithMessage("signalgate.check.failed_open")).Fields?["error_type"]);
            TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeTimeout, 1), ("check_total", 1));
        }
        finally
        {
            release.TrySetResult();
        }

        await released.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => late.IsDisposed);
        Assert.Equal(1, handler.CallCount);
        TestClients.AssertMetrics(client, ("check_failed_open_total", 1), (ErrorTypeTimeout, 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Deadline_throws_a_timeout_while_a_handler_that_ignores_cancellation_is_still_running()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.IgnoringCancellation(
            release.Task,
            FakeStep.Observe(_ => released.TrySetResult(), FakeStep.Respond(200, ResponseBodies.AllowVerdict))));
        SignalGateClient client = TestClients.Create(handler, configure: options =>
        {
            options.CheckTimeoutMs = 50;
            options.FailOpen = false;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            var elapsed = Stopwatch.StartNew();
            SignalGateTimeoutException error = await Assert.ThrowsAsync<SignalGateTimeoutException>(
                () => client.CheckAsync(StandardEvent.Create()).WaitAsync(Patience, TestContext.Current.CancellationToken));
            elapsed.Stop();

            Assert.Equal("request timed out after 50 ms", error.Message);
            Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Prompt);
            Assert.False(released.Task.IsCompleted);
            TestClients.AssertMetrics(client, (ErrorTypeTimeout, 1), ("check_total", 1));
        }
        finally
        {
            release.TrySetResult();
        }

        await released.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.CallCount);
    }

    // Polls until condition holds; fails the test if it does not hold within Patience.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < Patience, "the expected state was not reached in time");
            await Task.Delay(5);
        }
    }

    // Content whose body never arrives; it only completes when the read is cancelled.
    private sealed class NeverEndingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new NotSupportedException("only the cancellable overload is expected");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
