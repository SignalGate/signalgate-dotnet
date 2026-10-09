using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Logging;

public sealed class LogRequestTests
{
    private static readonly string[] RequestHeaderNames = ["Authorization", "Idempotency-Key", "User-Agent", "X-Request-Id"];
    private static readonly string[] ContentHeaderNames = ["Content-Length", "Content-Type"];
    private static readonly string[] TopLevelKeys = ["ip", "method", "payload", "timestamp", "user_id"];
    private static readonly string[] TopLevelKeysWithCustom = ["custom", "ip", "method", "payload", "timestamp", "user_id"];
    private static readonly string[] PayloadKeys = ["encrypted", "nonce", "timestamp"];
    private static readonly string[] PayloadKeysWithV = ["encrypted", "nonce", "timestamp", "v"];

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_is_a_post_to_the_log_endpoint_preferring_http2()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        RecordedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.signalgate.ai/v0/log", request.Uri?.AbsoluteUri);
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, request.VersionPolicy);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Every_attempt_carries_exactly_the_allowed_headers()
    {
        using var handler = new FakeHttpHandler(
            FakeStep.Respond(503),
            FakeStep.Respond(503),
            FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff());
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        IReadOnlyList<RecordedRequest> requests = handler.Requests;
        Assert.Equal(3, requests.Count);
        foreach (RecordedRequest request in requests)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.signalgate.ai/v0/log", request.Uri?.AbsoluteUri);
            Assert.Equal(RequestHeaderNames, Sorted(request.Headers.Keys), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(ContentHeaderNames, Sorted(request.ContentHeaders.Keys), StringComparer.OrdinalIgnoreCase);
            Assert.Equal("Bearer pk_test_unit_key", request.Headers["Authorization"]);
            Assert.Equal("application/json", request.ContentHeaders["Content-Type"]);
            Assert.Equal(request.Body.Length.ToString(CultureInfo.InvariantCulture), request.ContentHeaders["Content-Length"]);
            Assert.Matches(TestClients.UserAgentPattern, request.Headers["User-Agent"]);
            Assert.Matches(TestClients.UuidV4, request.Headers["X-Request-Id"]);
            Assert.Matches(TestClients.UuidV4, request.Headers["Idempotency-Key"]);
            Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.Full), request.Body);
        }

        Assert.Single(requests.Select(request => request.Headers["Idempotency-Key"]).Distinct());
        Assert.Equal(3, requests.Select(request => request.Headers["X-Request-Id"]).Distinct().Count());
        Assert.DoesNotContain(requests[0].Headers["Idempotency-Key"], requests.Select(request => request.Headers["X-Request-Id"]));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_body_is_identical_to_the_check_body_for_the_same_event()
    {
        using var handler = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();
        SignalGateEvent evt = StandardEvent.Create();

        await client.CheckAsync(evt);
        client.Log(evt);
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        IReadOnlyList<RecordedRequest> requests = handler.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Equal("https://api.signalgate.ai/v0/check", requests[0].Uri?.AbsoluteUri);
        Assert.Equal("https://api.signalgate.ai/v0/log", requests[1].Uri?.AbsoluteUri);
        Assert.Equal(requests[0].Body, requests[1].Body);
        Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.Full), requests[1].Body);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_body_omits_v_and_custom_when_absent()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.WithoutVOrCustom());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.WithoutVOrCustom), Assert.Single(handler.Requests).Body);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Log_body_keys_match_the_allow_lists()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        client.Log(StandardEvent.WithoutVOrCustom());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        IReadOnlyList<RecordedRequest> requests = handler.Requests;
        Assert.Equal(2, requests.Count);
        AssertKeys(requests[0].Body, TopLevelKeysWithCustom, PayloadKeysWithV);
        AssertKeys(requests[1].Body, TopLevelKeys, PayloadKeys);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Each_event_gets_its_own_idempotency_key()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        IReadOnlyList<RecordedRequest> requests = handler.Requests;
        Assert.Equal(2, requests.Count);
        Assert.NotEqual(requests[0].Headers["Idempotency-Key"], requests[1].Headers["Idempotency-Key"]);
        Assert.NotEqual(requests[0].Headers["X-Request-Id"], requests[1].Headers["X-Request-Id"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Key_with_a_tab_is_sent_byte_for_byte()
    {
        using var handler = new FakeHttpHandler(FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.Create(handler, configure: options => options.ApiKey = "pk\tinner");
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        Assert.Equal("Bearer pk\tinner", Assert.Single(handler.Requests).Headers["Authorization"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Request_and_response_events_are_logged_for_each_attempt()
    {
        var logger = new CapturingLogger();
        using var handler = new FakeHttpHandler(FakeStep.Respond(503), FakeStep.Respond(200, ResponseBodies.LogAcknowledged));
        SignalGateClient client = TestClients.CreateWithBackoff(handler, new RecordingBackoff(), logger);
        await using ClosingScope closing = client.ClosesAtEnd();

        client.Log(StandardEvent.Create());
        await client.CloseWithinLimitAsync(LogCounters.Flush);

        IReadOnlyList<LogEntry> requestEvents = logger.WithMessage("signalgate.http.request");
        IReadOnlyList<LogEntry> responseEvents = logger.WithMessage("signalgate.http.response");
        Assert.Equal(2, requestEvents.Count);
        Assert.Equal(new object?[] { 503, 200 }, responseEvents.Select(entry => entry.Fields?["status"]).ToArray());
        Assert.All(requestEvents, entry =>
        {
            Assert.Equal("https://api.signalgate.ai/v0/log", entry.Fields?["url"]);
            IReadOnlyDictionary<string, string> headers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(entry.Fields?["headers"]);
            Assert.Equal("Bearer ***REDACTED***", headers["Authorization"]);
        });
    }

    private static void AssertKeys(byte[] body, string[] topLevel, string[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(topLevel, Sorted(document.RootElement.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(payload, Sorted(document.RootElement.GetProperty("payload").EnumerateObject().Select(property => property.Name)));
    }

    private static string[] Sorted(IEnumerable<string> values)
    {
        return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
