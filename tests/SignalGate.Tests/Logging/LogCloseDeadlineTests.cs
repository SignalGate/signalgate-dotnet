using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

[Collection(TimingGroup.Name)]
public sealed class LogCloseDeadlineTests
{
    private const long LargestTimerMs = 4_294_967_294;

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(1, 5)]
    [InlineData(50, 250)]
    [InlineData(1000, 5000)]
    [InlineData(858_993_458, 4_294_967_290)]
    [InlineData(858_993_459, LargestTimerMs)]
    [InlineData(int.MaxValue, LargestTimerMs)]
    public async Task Default_deadline_is_exactly_five_times_the_log_timeout_clamped_to_the_timer_range(int logTimeoutMs, long expectedMs)
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, configure: options => options.LogTimeoutMs = logTimeoutMs);
        await using ClosingScope closing = client.ClosesAtEnd();

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), client.ResolveDeadline(null));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Explicit_deadlines_are_kept_or_clamped_to_the_timer_range()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        Assert.Equal(TimeSpan.Zero, client.ResolveDeadline(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(2), client.ResolveDeadline(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromMilliseconds(LargestTimerMs), client.ResolveDeadline(TimeSpan.FromMilliseconds(LargestTimerMs)));
        Assert.Equal(TimeSpan.FromMilliseconds(LargestTimerMs), client.ResolveDeadline(TimeSpan.FromMilliseconds(LargestTimerMs + 1)));
        Assert.Equal(TimeSpan.FromMilliseconds(LargestTimerMs), client.ResolveDeadline(TimeSpan.MaxValue));
        Assert.Equal(Timeout.InfiniteTimeSpan, client.ResolveDeadline(Timeout.InfiniteTimeSpan));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Default_deadline_is_five_times_the_log_timeout()
    {
        using var handler = new FakeHttpHandler(FakeStep.DelayUntilCancelled());
        SignalGateClient client = TestClients.Create(handler, configure: options =>
        {
            options.LogTimeoutMs = 50;
            options.LogMaxRetries = 0;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        // Draining would take about 100 x 50 ms; the default deadline is 250 ms.
        for (int i = 0; i < 100; i++)
        {
            client.Log(StandardEvent.Create());
        }

        var elapsed = Stopwatch.StartNew();
        await client.CloseWithinLimitAsync();
        elapsed.Stop();

        Assert.Equal(TimeSpan.FromMilliseconds(250), client.ResolveDeadline(null));
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(3));
        Assert.True(LogCounters.Dropped(client, "closed") > 0);
        Assert.Equal(100, LogCounters.Enqueued(client));
        LogCounters.AssertConserved(client);
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Zero_deadline_on_an_idle_client_completes_at_once()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CloseWithinLimitAsync(TimeSpan.Zero);

        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(client.Metrics.SnapshotFlat());
    }
}
