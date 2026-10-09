using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace SignalGate.DocSnippets.Snippets;

internal static class ConfigurationSnippets
{
    internal static async Task CustomHandlerAsync(string apiKey)
    {
        #region readme:configuration-http-handler
        // The SDK never disposes a handler you pass in; dispose it after the client.
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            Proxy = new WebProxy("http://proxy.example.internal:3128"),
        };

        await using var client = new SignalGateClient(new SignalGateClientOptions
        {
            ApiKey = apiKey,
            HttpHandler = handler,
        });
        #endregion
    }

    internal static async Task ShutdownAsync(SignalGateClient client)
    {
        #region readme:configuration-shutdown
        // Deliver queued log events, waiting at most 2 seconds.
        await client.CloseAsync(TimeSpan.FromSeconds(2));
        #endregion
    }
}
