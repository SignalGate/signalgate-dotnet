using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

[Collection(TimingGroup.Name)]
public sealed class LogSpeedTests
{
    private const int WarmUpCalls = 200;
    private const int MeasuredCalls = 1_000;

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_averages_under_one_millisecond_per_call()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        SignalGateEvent evt = StandardEvent.Create();

        for (int i = 0; i < WarmUpCalls; i++)
        {
            client.Log(evt);
        }

        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < MeasuredCalls; i++)
        {
            client.Log(evt);
        }

        elapsed.Stop();
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        double averageMs = elapsed.Elapsed.TotalMilliseconds / MeasuredCalls;
        Assert.True(averageMs < 1.0, $"average Log() time was {averageMs:F4} ms");
        Assert.Equal(WarmUpCalls + MeasuredCalls, LogCounters.Enqueued(client));
        Assert.Equal(WarmUpCalls + MeasuredCalls, LogCounters.Sent(client));
    }
}
