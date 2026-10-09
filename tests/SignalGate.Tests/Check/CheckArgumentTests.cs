using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class CheckArgumentTests
{
    public static TheoryData<string> UnsupportedCustomValues => new()
    {
        "guid",
        "enum",
        "not a number",
        "cycle",
        "oversized",
    };

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(UnsupportedCustomValues))]
    public async Task Unsupported_custom_value_throws_synchronously_before_any_request(string caseName)
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        SignalGateEvent evt = StandardEvent.WithCustom(BuildCustom(caseName));

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
        {
            _ = client.CheckAsync(evt);
        });

        Assert.Equal("evt", error.ParamName);
        Assert.NotNull(error.InnerException);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(client.Metrics.SnapshotFlat());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Null_event_throws_synchronously_before_any_request()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = client.CheckAsync(null!);
        });

        Assert.Equal("evt", error.ParamName);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(client.Metrics.SnapshotFlat());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Supported_nested_custom_values_are_sent()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        var custom = new Dictionary<string, object?>
        {
            ["nested"] = new Dictionary<string, string> { ["k"] = "v" },
            ["list"] = new List<int> { 1, 2 },
            ["big"] = 9007199254740993L,
        };

        await client.CheckAsync(StandardEvent.WithCustom(custom));

        string body = System.Text.Encoding.UTF8.GetString(Assert.Single(handler.Requests).Body);
        Assert.EndsWith(""","custom":{"nested":{"k":"v"},"list":[1,2],"big":9007199254740993}}""", body, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> BuildCustom(string caseName)
    {
        switch (caseName)
        {
            case "guid":
                return new Dictionary<string, object?> { ["value"] = Guid.NewGuid() };
            case "enum":
                return new Dictionary<string, object?> { ["value"] = DayOfWeek.Friday };
            case "not a number":
                return new Dictionary<string, object?> { ["value"] = double.NaN };
            case "cycle":
                var cycle = new Dictionary<string, object?>();
                cycle["self"] = cycle;
                return new Dictionary<string, object?> { ["value"] = cycle };
            case "oversized":
                return new Dictionary<string, object?> { ["value"] = new string('a', 1_100_000) };
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "unknown case");
        }
    }
}
