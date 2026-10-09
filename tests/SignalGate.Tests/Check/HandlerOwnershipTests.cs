using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Check;

public sealed class HandlerOwnershipTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Owned_handler_uses_the_expected_settings()
    {
        var client = new SignalGateClient(StandardEvent.ApiKey);
        await using ClosingScope closing = client.ClosesAtEnd();

        SocketsHttpHandler handler = Assert.IsType<SocketsHttpHandler>(client.OwnedHandler);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromSeconds(90), handler.PooledConnectionIdleTimeout);
        Assert.True(handler.EnableMultipleHttp2Connections);
        Assert.True(handler.UseProxy);
        Assert.Equal(int.MaxValue, handler.MaxConnectionsPerServer);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Http_client_has_no_overall_timeout_and_a_bounded_response_buffer()
    {
        using var fake = new FakeHttpHandler();
        var owned = new SignalGateClient(StandardEvent.ApiKey);
        await using ClosingScope ownedClosing = owned.ClosesAtEnd();
        SignalGateClient custom = TestClients.Create(fake);
        await using ClosingScope customClosing = custom.ClosesAtEnd();

        foreach (SignalGateClient client in new[] { owned, custom })
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);
            Assert.Equal(1_048_576, client.HttpClient.MaxResponseContentBufferSize);
            Assert.Empty(client.HttpClient.DefaultRequestHeaders);
        }
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Custom_handler_is_used_and_never_owned()
    {
        using var fake = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(fake);
        await using ClosingScope closing = client.ClosesAtEnd();

        await client.CheckAsync(StandardEvent.Create());
        await client.CloseWithinLimitAsync();

        Assert.Null(client.OwnedHandler);
        Assert.Equal(1, fake.CallCount);
        Assert.False(fake.IsDisposed);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Owned_handler_is_disposed_on_close()
    {
        var client = new SignalGateClient(StandardEvent.ApiKey);
        await using ClosingScope closing = client.ClosesAtEnd();
        SocketsHttpHandler owned = Assert.IsType<SocketsHttpHandler>(client.OwnedHandler);

        await client.CloseWithinLimitAsync();

        using var invoker = new HttpMessageInvoker(owned, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invoker.SendAsync(request, CancellationToken.None));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task No_request_reaches_the_handler_after_close()
    {
        using var fake = new FakeHttpHandler();
        SignalGateClient client = TestClients.Create(fake);
        await using ClosingScope closing = client.ClosesAtEnd();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.signalgate.ai/v0/check"));

        await client.CloseWithinLimitAsync();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = client.HttpClient.Send(request);
        });
        Assert.Equal(0, fake.CallCount);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Response_body_over_the_buffer_limit_is_a_network_error()
    {
        using var fake = new FakeHttpHandler(
            FakeStep.RespondWithContent(200, () => new StreamContent(new MemoryStream(new byte[1_048_577]))));
        SignalGateClient client = TestClients.Create(fake, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateNetworkException error = await Assert.ThrowsAsync<SignalGateNetworkException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.NotNull(error.InnerException);
        TestClients.AssertMetrics(client, ("""check_error_total{type="NetworkError"}""", 1), ("check_total", 1));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task Response_body_at_the_buffer_limit_is_read()
    {
        using var fake = new FakeHttpHandler(
            FakeStep.RespondWithContent(200, () => new StreamContent(new MemoryStream(new byte[1_048_576]))));
        SignalGateClient client = TestClients.Create(fake, configure: options => options.FailOpen = false);
        await using ClosingScope closing = client.ClosesAtEnd();

        SignalGateServerException error = await Assert.ThrowsAsync<SignalGateServerException>(
            () => client.CheckAsync(StandardEvent.Create()));

        Assert.Equal("MALFORMED_RESPONSE", error.Code);
    }
}
