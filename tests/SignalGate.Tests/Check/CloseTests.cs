using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class CloseTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Check_after_close_throws_synchronously_without_counting()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CloseWithinLimitAsync();
        SignalGateConfigException error = Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(StandardEvent.Create());
        });

        Assert.Equal("client is closed", error.Message);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, client.Metrics.Get("check_total"));
        Assert.Empty(client.Metrics.SnapshotFlat());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Closed_check_is_reported_before_a_null_event()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CloseWithinLimitAsync();

        Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(null!);
        });
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Close_returns_the_same_task_every_time()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        Task first = client.CloseAsync();
        Task second = client.CloseAsync();
        Task third = client.CloseAsync(TimeSpan.Zero);
        await client.WaitForCloseAsync(first);

        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Same(first, client.CloseAsync());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Concurrent_close_calls_share_one_task()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        Task<Task>[] callers = Enumerable.Range(0, 8)
            .Select(_ => Task.Factory.StartNew(
                () => client.CloseAsync(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();
        Task[] closes = await Task.WhenAll(callers);
        await client.WaitForCloseAsync(Task.WhenAll(closes));

        Assert.All(closes, close => Assert.Same(closes[0], close));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Infinite_deadline_is_accepted()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CloseWithinLimitAsync(Timeout.InfiniteTimeSpan);

        Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(StandardEvent.Create());
        });
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(-2)]
    [InlineData(-1000)]
    public async Task Other_negative_deadlines_are_rejected_synchronously(int milliseconds)
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = client.CloseAsync(TimeSpan.FromMilliseconds(milliseconds));
        });

        Assert.Equal("deadline", error.ParamName);
        CheckResult result = await client.CheckAsync(StandardEvent.Create());
        Assert.Equal("allow", result.Action);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Negative_deadline_is_rejected_even_after_close()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CloseWithinLimitAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = client.CloseAsync(TimeSpan.FromMilliseconds(-2));
        });
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Cancelled_token_still_closes()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await client.CloseWithinLimitAsync(cancellationToken: source.Token);

        Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(StandardEvent.Create());
        });
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Dispose_closes_the_client()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);

        await client.WaitForCloseAsync(Task.Run(
            () =>
            {
                client.Dispose();
                client.Dispose();
            },
            TestContext.Current.CancellationToken));

        Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(StandardEvent.Create());
        });
        Assert.False(handler.IsDisposed);
        await client.CloseWithinLimitAsync();
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Dispose_async_closes_the_client()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);

        await client.WaitForCloseAsync(client.DisposeAsync().AsTask());
        await client.WaitForCloseAsync(client.DisposeAsync().AsTask());

        Assert.Throws<SignalGateConfigException>(() =>
        {
            _ = client.CheckAsync(StandardEvent.Create());
        });
        Assert.False(handler.IsDisposed);
    }
}
