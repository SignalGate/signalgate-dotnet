using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SignalGate.DocSnippets.Snippets;

internal static class QuickstartSnippets
{
    internal static async Task<IResult> QuickstartAsync(
        AppUser user,
        string clientIp,
        SignupRequest body,
        CancellationToken cancellationToken)
    {
        #region readme:quickstart
        // Once, at startup: one client per process, shared by every request (see ASP.NET Core).
        await using var client = new SignalGateClient(new SignalGateClientOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("SIGNALGATE_API_KEY")!,
        });

        // Per request, before the action. If the browser capture produced no payload, skip the call (current policy).
        if (SignalGateEvent.TryCreate(user.Id, clientIp, "signup.send_code",
                DateTimeOffset.UtcNow, body.SignalGate, out var checkEvent))
        {
            CheckResult verdict = await client.CheckAsync(checkEvent, cancellationToken);
            if (verdict.Action == CheckActions.Block)
            {
                return Results.StatusCode(403);
            }
        }

        // ... perform the action ...

        // After it succeeded, with a second payload captured for this call.
        if (SignalGateEvent.TryCreate(user.Id, clientIp, "signup.code_sent",
                DateTimeOffset.UtcNow, body.SignalGateLog, out var logEvent))
        {
            client.Log(logEvent);
        }
        #endregion

        return Results.Ok();
    }

    internal static void CustomFields(SignalGateClient client, AppUser user, string clientIp, SignupRequest body)
    {
        #region readme:custom-fields
        var custom = new Dictionary<string, object?>
        {
            ["plan"] = "pro",
            ["seats"] = 12,
            ["trial"] = false,
            ["tags"] = new List<string> { "beta", "eu" },
            ["referral"] = new Dictionary<string, string> { ["source"] = "newsletter" },
        };

        if (SignalGateEvent.TryCreate(user.Id, clientIp, "plan.upgraded",
                DateTimeOffset.UtcNow, body.SignalGateLog, out var upgradeEvent, custom))
        {
            client.Log(upgradeEvent);
        }
        #endregion
    }
}
