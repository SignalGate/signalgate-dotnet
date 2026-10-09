using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Fakes;

public static class TestClients
{
    // The request deadline for tests that hold a request at a gate. It is far longer than the test's own work, so
    // the held request never times out, even on a loaded machine.
    public const int HeldRequestTimeoutMs = 120_000;

    public static readonly Regex UuidV4 = new("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

    public static readonly Regex UserAgentPattern = new(@"^signalgate-backend-sdk/\d+\.\d+\.\d+ \(dotnet/[^;)]+; (linux|darwin|windows|freebsd|unknown)\)$");

    public static readonly Uri ServiceAddress = new("https://api.signalgate.ai");

    // A client built through the public constructor, so requests go to the service address via the fake.
    public static SignalGateClient Create(
        FakeHttpHandler handler,
        CapturingLogger? logger = null,
        Action<SignalGateClientOptions>? configure = null)
    {
        var options = new SignalGateClientOptions
        {
            ApiKey = StandardEvent.ApiKey,
            HttpHandler = handler,
            Logger = logger,
        };
        configure?.Invoke(options);
        return new SignalGateClient(options);
    }

    // A client built through the internal constructor: the waits between log delivery attempts go to backoff
    // instead of real timers.
    public static SignalGateClient CreateWithBackoff(
        FakeHttpHandler handler,
        RecordingBackoff backoff,
        CapturingLogger? logger = null,
        Action<SignalGateClientOptions>? configure = null)
    {
        var options = new SignalGateClientOptions
        {
            ApiKey = StandardEvent.ApiKey,
            HttpHandler = handler,
            Logger = logger,
        };
        configure?.Invoke(options);
        return new SignalGateClient(options, ServiceAddress, backoff.Delay);
    }

    // Asserts that the flat snapshot holds exactly the given counters.
    public static void AssertMetrics(SignalGateClient client, params (string Key, long Value)[] expected)
    {
        Assert.Equal(
            Render(expected.Select(pair => new KeyValuePair<string, long>(pair.Key, pair.Value))),
            Render(client.Metrics.SnapshotFlat()));
    }

    public static void AssertFailedOpen(CheckResult result)
    {
        Assert.Equal("allow", result.Action);
        Assert.Equal(0.0, result.Score);
        Assert.Equal("", result.RequestId);
        Assert.Equal("", result.TenantId);
        Assert.Equal("", result.Timestamp);
        Assert.Equal(0, result.ProcessingTimeUs);
        Assert.True(result.FailedOpen);
    }

    private static string Render(IEnumerable<KeyValuePair<string, long>> counters)
    {
        return string.Join(
            "\n",
            counters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + " = " + pair.Value));
    }
}
