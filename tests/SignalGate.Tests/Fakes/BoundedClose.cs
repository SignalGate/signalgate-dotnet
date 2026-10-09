using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SignalGate.Tests.Fakes;

// Bounded waits for closing a client. Tests close every client through these, so a close that never finishes
// fails its test within seconds, with a message saying so, instead of hanging the test run.
public static class BoundedClose
{
    private static readonly object Marker = new();

    // Clients whose close has already failed a test; their closing scope does not wait for them again.
    private static readonly ConditionalWeakTable<SignalGateClient, object> Stuck = new();

    // Closes client as CloseAsync does, and waits for the close within the limit. limit, when given, replaces
    // the computed limit, for a close that has a large queue to deliver.
    public static Task CloseWithinLimitAsync(
        this SignalGateClient client,
        TimeSpan? deadline = null,
        TimeSpan? limit = null,
        CancellationToken cancellationToken = default)
    {
        return client.WaitForCloseAsync(client.CloseAsync(deadline, cancellationToken), deadline, limit);
    }

    // Waits within the limit for a close of client that is already running: a task returned by CloseAsync or
    // DisposeAsync, or one that calls Dispose. deadline is the deadline that close was given; limit, when
    // given, replaces the computed limit.
    public static async Task WaitForCloseAsync(
        this SignalGateClient client,
        Task close,
        TimeSpan? deadline = null,
        TimeSpan? limit = null)
    {
        TimeSpan allowed = limit ?? LimitFor(client, deadline);
        try
        {
            await close.WaitAsync(allowed, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Stuck.AddOrUpdate(client, Marker);
            Assert.Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"closing the client did not finish within {allowed.TotalSeconds:0.###} s"));
        }
    }

    // Returns a scope that closes client, within the limit, when it is disposed. Tests declare it with
    // `await using` instead of declaring the client itself with `await using`.
    public static ClosingScope ClosesAtEnd(this SignalGateClient client)
    {
        return new ClosingScope(client);
    }

    internal static bool HasFailedToClose(SignalGateClient client)
    {
        return Stuck.TryGetValue(client, out _);
    }

    // A close may wait out its deadline only while accepted events are still undelivered; otherwise it ends
    // as soon as the worker stops. Deadlines longer than the limit are never waited out in tests.
    private static TimeSpan LimitFor(SignalGateClient client, TimeSpan? deadline)
    {
        TimeSpan resolved = client.ResolveDeadline(deadline);
        bool mayWaitOutDeadline = resolved >= TimeSpan.Zero
            && resolved <= HangGuard.CloseLimit
            && HasUndeliveredEvents(client);
        return mayWaitOutDeadline ? HangGuard.CloseLimit + resolved : HangGuard.CloseLimit;
    }

    private static bool HasUndeliveredEvents(SignalGateClient client)
    {
        SignalGateMetrics metrics = client.Metrics;
        long accounted = metrics.Get("log_sent_total")
            + metrics.Get("log_dropped_total", Reason("closed"))
            + metrics.Get("log_dropped_total", Reason("retry_exhausted"));
        return metrics.Get("log_enqueued_total") > accounted;
    }

    private static Dictionary<string, string> Reason(string reason)
    {
        return new Dictionary<string, string> { ["reason"] = reason };
    }
}

// Closes a client when a test ends, with the default deadline as `await using` on the client would, but fails
// the test instead of hanging when the close does not finish within the limit.
public sealed class ClosingScope : IAsyncDisposable
{
    private readonly SignalGateClient _client;

    internal ClosingScope(SignalGateClient client)
    {
        _client = client;
    }

    public async ValueTask DisposeAsync()
    {
        if (BoundedClose.HasFailedToClose(_client))
        {
            return;
        }

        await _client.WaitForCloseAsync(_client.DisposeAsync().AsTask());
    }
}
