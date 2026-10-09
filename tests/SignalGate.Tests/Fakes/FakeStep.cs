using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGate.Tests.Fakes;

// One scripted reaction of FakeHttpHandler to a request.
public sealed class FakeStep
{
    private FakeStep(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run, bool attachRequest)
    {
        Run = run;
        AttachRequest = attachRequest;
    }

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Run { get; }

    // When true, the handler sets the response's RequestMessage to the request it received.
    public bool AttachRequest { get; }

    public static FakeStep Respond(int status, string body = "", params (string Name, string Value)[] headers)
    {
        return new FakeStep((_, _) => Task.FromResult(BuildResponse(status, body, headers)), attachRequest: true);
    }

    public static FakeStep RespondWithContent(int status, Func<HttpContent> content)
    {
        return new FakeStep(
            (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content() }),
            attachRequest: true);
    }

    public static FakeStep RespondWithoutRequestMessage(int status, string body)
    {
        return new FakeStep((_, _) => Task.FromResult(BuildResponse(status, body, [])), attachRequest: false);
    }

    // A response whose RequestMessage reports a different final URI, as a handler that followed a redirect does.
    public static FakeStep RespondAfterRedirectTo(Uri finalUri, int status, string body)
    {
        return new FakeStep(
            (_, _) =>
            {
                HttpResponseMessage response = BuildResponse(status, body, []);
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri);
                return Task.FromResult(response);
            },
            attachRequest: false);
    }

    public static FakeStep Throw(Func<Exception> exception)
    {
        return new FakeStep((_, _) => Task.FromException<HttpResponseMessage>(exception()), attachRequest: true);
    }

    // Waits until the request's token is cancelled, then throws the cancellation.
    public static FakeStep DelayUntilCancelled()
    {
        return new FakeStep(
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            },
            attachRequest: true);
    }

    // Runs observe on the request, then continues with then.
    public static FakeStep Observe(Action<HttpRequestMessage> observe, FakeStep then)
    {
        return new FakeStep(
            (request, cancellationToken) =>
            {
                observe(request);
                return then.Run(request, cancellationToken);
            },
            then.AttachRequest);
    }

    // Reacts with check for requests to the check endpoint and with log for every other request.
    public static FakeStep PerEndpoint(FakeStep check, FakeStep log)
    {
        return new FakeStep(
            async (request, cancellationToken) =>
            {
                bool isCheck = request.RequestUri?.AbsolutePath.EndsWith("/v0/check", StringComparison.Ordinal) == true;
                FakeStep chosen = isCheck ? check : log;
                HttpResponseMessage response = await chosen.Run(request, cancellationToken);
                if (chosen.AttachRequest)
                {
                    response.RequestMessage = request;
                }

                return response;
            },
            attachRequest: false);
    }

    // Waits for gate, observing the request's token, then continues with then.
    public static FakeStep Gated(TaskCompletionSource gate, FakeStep then)
    {
        return new FakeStep(
            async (request, cancellationToken) =>
            {
                await gate.Task.WaitAsync(cancellationToken);
                return await then.Run(request, cancellationToken);
            },
            then.AttachRequest);
    }

    // Waits for release without observing the request's token, as a handler that ignores cancellation does,
    // then continues with then.
    public static FakeStep IgnoringCancellation(Task release, FakeStep then)
    {
        return new FakeStep(
            async (request, _) =>
            {
                await release;
                return await then.Run(request, CancellationToken.None);
            },
            then.AttachRequest);
    }

    private static HttpResponseMessage BuildResponse(int status, string body, (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
        };
        foreach ((string name, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
