using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace SignalGate.DocSnippets.Snippets;

#region readme:aspnetcore-request-type
public sealed record CheckoutRequest(string CartId, EncryptedPayload? SignalGate, EncryptedPayload? SignalGateLog);
#endregion

internal static class AspNetCoreSnippets
{
    internal static void Register(WebApplicationBuilder builder)
    {
        #region readme:aspnetcore-register
        builder.Services.AddSingleton(_ => new SignalGateClient(builder.Configuration["SignalGate:ApiKey"]!));
        #endregion
    }

    internal static void MapCheckout(WebApplication app)
    {
        #region readme:aspnetcore-endpoint
        app.MapPost("/checkout", async (
            CheckoutRequest body,
            ClaimsPrincipal user,
            HttpContext http,
            SignalGateClient signalGate,
            CancellationToken cancellationToken) =>
        {
            // A missing value makes TryCreate return false, so the call is skipped.
            string? userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            IPAddress? remoteIp = http.Connection.RemoteIpAddress;
            string? clientIp = (remoteIp is { IsIPv4MappedToIPv6: true } ? remoteIp.MapToIPv4() : remoteIp)?.ToString();

            if (SignalGateEvent.TryCreate(userId, clientIp, "checkout",
                    DateTimeOffset.UtcNow, body.SignalGate, out var checkEvent))
            {
                CheckResult verdict = await signalGate.CheckAsync(checkEvent, cancellationToken);
                if (verdict.Action == CheckActions.Block)
                {
                    return Results.StatusCode(403);
                }
            }

            // ... place the order, then Log with body.SignalGateLog ...

            return Results.Ok();
        });
        #endregion
    }

    internal static WebApplication ConfigureForwardedHeaders(WebApplicationBuilder builder)
    {
        #region readme:aspnetcore-forwarded-headers
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownProxies.Add(IPAddress.Parse("10.0.0.10"));
            options.ForwardLimit = 1;
        });

        var app = builder.Build();
        app.UseForwardedHeaders();
        #endregion

        return app;
    }
}
