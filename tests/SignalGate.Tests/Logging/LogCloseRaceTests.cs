using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogCloseRaceTests
{
    // A Log call that is still running when close returns is caught only when its thread is held up at the wrong
    // moment, which happens in a small fraction of rounds; this many rounds make the test fail on such a race in
    // practically every run, and take about a second on an idle machine.
    private const int Rounds = 5_000;
    private const int Producers = 8;

    // Producer threads (Producers of them) call Log in a tight loop, always on the current client. Each round puts
    // a fresh client in place, lets Log calls reach it, closes it, and reads its counters the moment the close
    // completes, while a producer may still be inside Log on it.
    [Fact(Timeout = HangGuard.RepeatedTimeoutMs)]
    public async Task Accepted_events_are_all_accounted_for_the_moment_close_returns_while_other_threads_log()
    {
        CancellationToken testToken = TestContext.Current.CancellationToken;
        var closed = new List<(SignalGateClient Client, long Enqueued)>(Rounds);
        var handlers = new List<FakeHttpHandler>(Rounds);
        SignalGateClient? current = null;
        using var stop = new CancellationTokenSource();

        Task[] producers = Enumerable.Range(0, Producers)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        Volatile.Read(ref current)?.Log(StandardEvent.Create());
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        try
        {
            for (int round = 0; round < Rounds; round++)
            {
                var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
                handlers.Add(handler);
                SignalGateClient client = TestClients.Create(handler, configure: options => options.LogQueueCapacity = 16);
                TimeSpan deadline = round % 2 == 0 ? TimeSpan.Zero : LogCounters.Flush;

                Volatile.Write(ref current, client);
                WaitForLogCalls(client);
                Task close = client.CloseAsync(deadline, CancellationToken.None);
                await client.WaitForCloseAsync(close, deadline);

                long enqueued = LogCounters.Enqueued(client);
                long accounted = LogCounters.Sent(client)
                    + LogCounters.Dropped(client, "closed")
                    + LogCounters.Dropped(client, "retry_exhausted");
                TaskStatus worker = client.Worker.Status;
                closed.Add((client, enqueued));

                Assert.True(enqueued == accounted, $"round {round}: {enqueued} accepted, {accounted} accounted for when close returned");
                Assert.Equal(TaskStatus.RanToCompletion, worker);
            }
        }
        finally
        {
            Volatile.Write(ref current, null);
            await stop.CancelAsync();
            await Task.WhenAll(producers).WaitAsync(LogCounters.Patience, testToken);
            foreach (FakeHttpHandler handler in handlers)
            {
                handler.Dispose();
            }
        }

        // Nothing was counted as accepted after its close returned, even by a Log call that was still running.
        Assert.All(closed, entry =>
        {
            Assert.Equal(entry.Enqueued, LogCounters.Enqueued(entry.Client));
            LogCounters.AssertConserved(entry.Client);
        });
    }

    // Waits until the producers' Log calls have reached client, as accepted or dropped events.
    private static void WaitForLogCalls(SignalGateClient client)
    {
        var elapsed = Stopwatch.StartNew();
        while (LogCounters.Enqueued(client) + LogCounters.Dropped(client, "queue_full") < Producers)
        {
            Assert.True(elapsed.Elapsed < LogCounters.Patience, "no Log call reached the client in time");
            Thread.SpinWait(20);
        }
    }
}
