using System;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogBackoffTests
{
    private const long MaxDelayMs = 4_294_967_294;

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(200, 0L, 200L)]
    [InlineData(200, 1L, 400L)]
    [InlineData(200, 2L, 800L)]
    [InlineData(1, 31L, 2_147_483_648L)]
    [InlineData(1, 32L, MaxDelayMs)]
    [InlineData(1, 1_000L, MaxDelayMs)]
    [InlineData(int.MaxValue, 0L, 2_147_483_647L)]
    [InlineData(int.MaxValue, 1L, MaxDelayMs)]
    [InlineData(int.MaxValue, 31L, MaxDelayMs)]
    [InlineData(0, 0L, 0L)]
    [InlineData(0, 40L, 0L)]
    public void Delay_doubles_per_attempt_and_is_clamped(int retryBaseMs, long attempt, long expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), LogWorker.BackoffDelay(retryBaseMs, attempt));
    }
}
