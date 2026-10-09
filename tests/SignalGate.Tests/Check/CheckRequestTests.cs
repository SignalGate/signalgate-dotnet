using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class CheckRequestTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Request_carries_exactly_the_allowed_headers()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(
            Sorted("Authorization", "Idempotency-Key", "User-Agent", "X-Request-Id"),
            Sorted(request.Headers.Keys),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Sorted("Content-Length", "Content-Type"), Sorted(request.ContentHeaders.Keys), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Bearer pk_test_unit_key", request.Headers["Authorization"]);
        Assert.Equal("application/json", request.ContentHeaders["Content-Type"]);
        Assert.Equal(request.Body.Length.ToString(CultureInfo.InvariantCulture), request.ContentHeaders["Content-Length"]);
        Assert.Matches(TestClients.UserAgentPattern, request.Headers["User-Agent"]);
        Assert.Matches(TestClients.UuidV4, request.Headers["X-Request-Id"]);
        Assert.Matches(TestClients.UuidV4, request.Headers["Idempotency-Key"]);
        Assert.NotEqual(request.Headers["X-Request-Id"], request.Headers["Idempotency-Key"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Request_is_a_post_to_the_check_endpoint_preferring_http2()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.signalgate.ai/v0/check", request.Uri?.AbsoluteUri);
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, request.VersionPolicy);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Body_is_the_expected_encoding_of_the_standard_event()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.Full), Assert.Single(handler.Requests).Body);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Body_omits_v_and_custom_when_absent()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.WithoutVOrCustom());

        Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.WithoutVOrCustom), Assert.Single(handler.Requests).Body);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Ids_are_fresh_on_every_call()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());
        await client.CheckAsync(StandardEvent.Create());

        string[] ids = handler.Requests
            .SelectMany(request => new[] { request.Headers["X-Request-Id"], request.Headers["Idempotency-Key"] })
            .ToArray();
        Assert.Equal(4, ids.Length);
        Assert.Equal(4, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches(TestClients.UuidV4, id));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("  pk_test_unit_key  ")]
    [InlineData("pk\tinner")]
    [InlineData("test-key")]
    public async Task Key_is_sent_byte_for_byte(string apiKey)
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler, configure: options => options.ApiKey = apiKey);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        Assert.Equal("Bearer " + apiKey, Assert.Single(handler.Requests).Headers["Authorization"]);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("https://api.signalgate.ai")]
    [InlineData("https://api.signalgate.ai/")]
    public async Task Endpoints_are_built_from_the_service_address(string serviceAddress)
    {
        using var handler = new FakeHttpHandler();
        var options = new SignalGateClientOptions { ApiKey = StandardEvent.ApiKey, HttpHandler = handler };
        var client = new SignalGateClient(options, new Uri(serviceAddress));
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());

        Assert.Equal("https://api.signalgate.ai/v0/check", Assert.Single(handler.Requests).Uri?.AbsoluteUri);
    }

    private static string[] Sorted(params string[] names)
    {
        return Sorted((IEnumerable<string>)names);
    }

    private static string[] Sorted(IEnumerable<string> names)
    {
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
