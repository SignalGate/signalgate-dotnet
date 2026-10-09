using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Concurrency;

public sealed class ParallelCallerTests
{
    private const int Callers = 32;
    private const int CallsPerCaller = 50;
    private const int TotalCalls = Callers * CallsPerCaller;

    private static readonly TimeSpan Flush = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Fact(Timeout = HangGuard.RepeatedTimeoutMs)]
    public async Task Parallel_checks_and_logs_on_one_client_are_all_counted_exactly()
    {
        using var handler = new FakeHttpHandler(FakeStep.PerEndpoint(
            check: FakeStep.Respond(200, ResponseBodies.AllowVerdict),
            log: FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        IReadOnlyList<CheckResult> verdicts = await RunCallersAsync(async () =>
        {
            CheckResult verdict = await client.CheckAsync(StandardEvent.Create());
            client.Log(StandardEvent.Create());
            return verdict;
        });
        await client.CloseWithinLimitAsync(Flush, limit: Patience);

        Assert.Equal(TotalCalls, verdicts.Count);
        Assert.All(verdicts, verdict =>
        {
            Assert.Equal("allow", verdict.Action);
            Assert.Equal("req_1", verdict.RequestId);
            Assert.False(verdict.FailedOpen);
        });
        TestClients.AssertMetrics(
            client,
            ("check_success_total", TotalCalls),
            ("check_total", TotalCalls),
            ("log_enqueued_total", TotalCalls),
            ("log_sent_total", TotalCalls));
        Assert.Equal(2 * TotalCalls, handler.CallCount);
        Assert.Equal(TotalCalls, CountRequestsTo(handler, "https://api.signalgate.ai/v0/check"));
        Assert.Equal(TotalCalls, CountRequestsTo(handler, "https://api.signalgate.ai/v0/log"));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    [Fact(Timeout = HangGuard.RepeatedTimeoutMs)]
    public async Task Parallel_callers_get_one_request_and_one_response_event_per_request()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.PerEndpoint(
            check: FakeStep.Respond(200, ResponseBodies.AllowVerdict),
            log: FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        await RunCallersAsync(async () =>
        {
            CheckResult verdict = await client.CheckAsync(StandardEvent.Create());
            client.Log(StandardEvent.Create());
            return verdict;
        });
        await client.CloseWithinLimitAsync(Flush, limit: Patience);

        IReadOnlyList<LogEntry> requests = logger.WithMessage("signalgate.http.request");
        IReadOnlyList<LogEntry> responses = logger.WithMessage("signalgate.http.response");
        Assert.Equal(2 * TotalCalls, requests.Count);
        Assert.Equal(2 * TotalCalls, responses.Count);
        Assert.All(responses, entry => Assert.Equal(200, entry.Fields?["status"]));
        Assert.Equal(
            handler.Requests.Select(request => request.Headers["X-Request-Id"]).OrderBy(id => id, StringComparer.Ordinal),
            requests.Select(entry => Assert.IsType<string>(entry.Fields?["request_id"])).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(2 * TotalCalls, handler.Requests.Select(request => request.Headers["X-Request-Id"]).Distinct(StringComparer.Ordinal).Count());
        TestClients.AssertMetrics(
            client,
            ("check_success_total", TotalCalls),
            ("check_total", TotalCalls),
            ("log_enqueued_total", TotalCalls),
            ("log_sent_total", TotalCalls));
    }

    [Fact(Timeout = HangGuard.RepeatedTimeoutMs)]
    public async Task Parallel_logs_into_a_small_queue_are_each_accepted_or_dropped_once()
    {
        const int Capacity = 64;
        var logger = new CapturingLogger();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHttpHandler(FakeStep.Gated(gate, FakeStep.Respond(200, ResponseBodies.LogAcknowledged)));
        SignalGateClient client = TestClients.Create(handler, logger, options =>
        {
            options.LogQueueCapacity = Capacity;
            options.LogTimeoutMs = TestClients.HeldRequestTimeoutMs;
        });
        await using ClosingScope closing = client.ClosesAtEnd();

        try
        {
            await RunCallersAsync(() =>
            {
                client.Log(StandardEvent.Create());
                return Task.FromResult(true);
            });

            long enqueued = client.Metrics.Get("log_enqueued_total");
            long queueFull = client.Metrics.Get("log_dropped_total", new Dictionary<string, string> { ["reason"] = "queue_full" });
            Assert.Equal(TotalCalls, enqueued + queueFull);
            Assert.InRange(enqueued, Capacity, Capacity + 1);
            Assert.Equal(queueFull, logger.WithMessage("signalgate.log.queue_full").Count);
        }
        finally
        {
            gate.TrySetResult();
        }

        await client.CloseWithinLimitAsync(Flush);

        long accepted = client.Metrics.Get("log_enqueued_total");
        Assert.Equal(accepted, client.Metrics.Get("log_sent_total"));
        Assert.Equal(accepted, handler.CallCount);
        Assert.Equal(
            TotalCalls,
            accepted + client.Metrics.Get("log_dropped_total", new Dictionary<string, string> { ["reason"] = "queue_full" }));
        Assert.Equal(TaskStatus.RanToCompletion, client.Worker.Status);
    }

    // Starts every caller at the same moment on the thread pool; each runs call CallsPerCaller times.
    private static async Task<IReadOnlyList<T>> RunCallersAsync<T>(Func<Task<T>> call)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<List<T>>[] callers = Enumerable.Range(0, Callers)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                var results = new List<T>(CallsPerCaller);
                for (int i = 0; i < CallsPerCaller; i++)
                {
                    results.Add(await call());
                }

                return results;
            }))
            .ToArray();

        start.SetResult();
        List<T>[] all = await Task.WhenAll(callers).WaitAsync(Patience);
        return all.SelectMany(results => results).ToArray();
    }

    private static int CountRequestsTo(FakeHttpHandler handler, string uri)
    {
        return handler.Requests.Count(request => request.Uri?.AbsoluteUri == uri);
    }
}
