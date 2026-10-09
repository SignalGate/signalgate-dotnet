using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Redaction;

// Collects everything a client produced in one scenario (logger lines, thrown exceptions, descriptions and
// metric keys) and checks that the API key appears in none of it.
public sealed class KeyProbe
{
    public const string Key = StandardEvent.ApiKey;

    public const string MaskedAuthorization = "Bearer ***REDACTED***";

    private const int MaxScanDepth = 32;

    private readonly object _sync = new();
    private readonly List<Exception> _errors = [];

    public CapturingLogger Logger { get; } = new();

    public IReadOnlyList<Exception> Errors
    {
        get
        {
            lock (_sync)
            {
                return _errors.ToArray();
            }
        }
    }

    public SignalGateClientOptions Options(FakeHttpHandler handler, Action<SignalGateClientOptions>? configure = null)
    {
        var options = new SignalGateClientOptions
        {
            ApiKey = Key,
            HttpHandler = handler,
            Logger = Logger,
        };
        configure?.Invoke(options);
        return options;
    }

    // Runs action and records the exception it throws, synchronously or from the returned task.
    public async Task<TException> CaptureAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        TException error = await Assert.ThrowsAnyAsync<TException>(action);
        Record(error);
        return error;
    }

    // Runs action and records the exception it throws.
    public TException Capture<TException>(Action action)
        where TException : Exception
    {
        TException error = Assert.ThrowsAny<TException>(action);
        Record(error);
        return error;
    }

    // The key really was sent, so its absence everywhere else is meaningful.
    public static void AssertKeyWasSent(FakeHttpHandler handler)
    {
        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer " + Key, request.Headers["Authorization"]));
    }

    // Every request line shows the masked authorization value.
    public void AssertRequestLinesAreMasked(int expectedRequests)
    {
        IReadOnlyList<LogEntry> lines = Logger.WithMessage("signalgate.http.request");
        Assert.Equal(expectedRequests, lines.Count);
        Assert.All(lines, line =>
        {
            Assert.NotNull(line.Fields);
            IReadOnlyDictionary<string, string> headers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(line.Fields["headers"]);
            Assert.Equal(MaskedAuthorization, headers["Authorization"]);
        });
    }

    // Scans every logger line, every recorded exception, both descriptions and every metric key.
    public void AssertKeyNeverAppears(SignalGateClient client, SignalGateClientOptions options)
    {
        foreach (LogEntry entry in Logger.Entries)
        {
            string where = entry.Level + " " + entry.Message;
            AssertNoKey(entry.Message, where);
            if (entry.Fields is not null)
            {
                foreach (KeyValuePair<string, object?> field in entry.Fields)
                {
                    AssertNoKey(field.Key, where);
                    Scan(field.Value, where + " field " + field.Key, depth: 0);
                }
            }
        }

        foreach (Exception error in Errors)
        {
            AssertExceptionIsClean(error);
        }

        AssertNoKey(options.ToString(), "options description");
        AssertNoKey(client.ToString(), "client description");

        foreach (string flatKey in client.Metrics.SnapshotFlat().Keys)
        {
            AssertNoKey(flatKey, "metric key");
        }

        foreach (MetricSample sample in client.Metrics.Snapshot())
        {
            AssertNoKey(sample.Name, "metric name");
            Scan(sample.Labels, "metric labels", depth: 0);
            AssertNoKey(sample.ToString(), "metric sample");
        }
    }

    public static void AssertExceptionIsClean(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            AssertNoKey(current.Message, current.GetType().Name + " message");
        }

        AssertNoKey(error.ToString(), error.GetType().Name + " description");
    }

    private void Record(Exception error)
    {
        lock (_sync)
        {
            _errors.Add(error);
        }
    }

    // Visits nested dictionaries and sequences, and turns every other value into text.
    private static void Scan(object? value, string where, int depth)
    {
        Assert.True(depth < MaxScanDepth, "logged values are nested too deeply to scan: " + where);
        switch (value)
        {
            case null:
                return;
            case string text:
                AssertNoKey(text, where);
                return;
            case Exception error:
                AssertExceptionIsClean(error);
                return;
            case IDictionary map:
                foreach (DictionaryEntry entry in map)
                {
                    Scan(entry.Key, where, depth + 1);
                    Scan(entry.Value, where, depth + 1);
                }

                return;
            case IEnumerable items:
                foreach (object? item in items)
                {
                    Scan(item, where, depth + 1);
                }

                return;
            default:
                AssertNoKey(Convert.ToString(value, CultureInfo.InvariantCulture), where);
                AssertNoKey(value.ToString(), where);
                return;
        }
    }

    private static void AssertNoKey(string? text, string where)
    {
        if (text is not null && text.Contains(Key, StringComparison.Ordinal))
        {
            Assert.Fail("the API key appeared in " + where);
        }
    }
}
