using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace SignalGate.Tests.Logging;

public static class LogCounters
{
    // A close deadline long enough for every queued event to be delivered.
    public static readonly TimeSpan Flush = TimeSpan.FromSeconds(30);

    // How long a test waits for something that should happen promptly.
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public static long Enqueued(SignalGateClient client) => client.Metrics.Get("log_enqueued_total");

    public static long Sent(SignalGateClient client) => client.Metrics.Get("log_sent_total");

    public static long Dropped(SignalGateClient client, string reason)
    {
        return client.Metrics.Get("log_dropped_total", new Dictionary<string, string> { ["reason"] = reason });
    }

    public static long HttpErrors(SignalGateClient client, string status)
    {
        return client.Metrics.Get("log_http_error_total", new Dictionary<string, string> { ["status"] = status });
    }

    // Polls until condition holds; fails the test if it does not hold within Patience.
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < Patience, "the expected state was not reached in time");
            await Task.Delay(5);
        }
    }

    // Every accepted event ended as sent, closed or retry_exhausted.
    public static void AssertConserved(SignalGateClient client)
    {
        Assert.Equal(
            Enqueued(client),
            Sent(client) + Dropped(client, "closed") + Dropped(client, "retry_exhausted"));
    }
}
