using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SignalGate.DocSnippets.Snippets;

internal static class ErrorHandlingSnippets
{
    internal static async Task<IResult> CheckAsync(
        SignalGateClient client,
        SignalGateEvent checkEvent,
        CancellationToken cancellationToken)
    {
        #region readme:error-handling
        try
        {
            CheckResult verdict = await client.CheckAsync(checkEvent, cancellationToken);
            if (verdict.Action == CheckActions.Block)
            {
                return Results.StatusCode(403);
            }
        }
        catch (SignalGateServerException ex) when (ex.StatusCode < 500)
        {
            // The check was rejected, e.g. "[401] UNAUTHORIZED: ..." (check the API key) or
            // "[422] REQUEST_REJECTED: ..." (the payload was already used).
            // This example refuses the request; decide what your application should do.
            Console.Error.WriteLine($"SignalGate check rejected: {ex.Message} (request id {ex.RequestId})");
            return Results.BadRequest();
        }
        #endregion

        return Results.Ok();
    }
}
