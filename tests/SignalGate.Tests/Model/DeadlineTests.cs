using System;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class DeadlineTests
{
    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(200, 200)]
    [InlineData(4_294_967_294, 4_294_967_294)]
    [InlineData(4_294_967_295, 4_294_967_294)]
    [InlineData(long.MaxValue, 4_294_967_294)]
    [InlineData(long.MinValue, 0)]
    public void Delays_are_clamped_to_the_timer_range(long input, long expected)
    {
        Assert.Equal(expected, Deadline.ClampMs(input));
        Assert.Equal(TimeSpan.FromMilliseconds(expected), Deadline.ToTimeSpan(input));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void The_largest_clamped_delay_is_accepted_by_timers()
    {
        using var source = new System.Threading.CancellationTokenSource();

        source.CancelAfter(Deadline.ToTimeSpan(long.MaxValue));

        Assert.False(source.IsCancellationRequested);
    }
}
