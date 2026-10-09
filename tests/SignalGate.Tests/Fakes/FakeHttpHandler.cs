using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using SignalGate.Tests.Fixtures;

namespace SignalGate.Tests.Fakes;

// A thread-safe, scripted HttpMessageHandler. Each call consumes the next step; the last step repeats once the
// script runs out. Without steps every call gets a 200 allow verdict. Every request is recorded synchronously,
// before its step runs. Header names are matched case-insensitively, as HTTP defines them.
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly object _sync = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<TaskCompletionSource> _entered = [];
    private readonly FakeStep[] _steps;
    private int _disposeCalls;

    public FakeHttpHandler(params FakeStep[] steps)
    {
        _steps = steps.Length == 0 ? [FakeStep.Respond(200, ResponseBodies.AllowVerdict)] : steps;
    }

    public int CallCount
    {
        get
        {
            lock (_sync)
            {
                return _requests.Count;
            }
        }
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    public bool IsDisposed => Volatile.Read(ref _disposeCalls) > 0;

    // Completes once call number callNumber (1-based) has entered SendAsync and been recorded.
    public Task WhenEntered(int callNumber = 1)
    {
        lock (_sync)
        {
            return EnteredSource(callNumber).Task;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RecordedRequest record = Record(request);

        FakeStep step;
        TaskCompletionSource entered;
        lock (_sync)
        {
            _requests.Add(record);
            int callNumber = _requests.Count;
            step = _steps[Math.Min(callNumber, _steps.Length) - 1];
            entered = EnteredSource(callNumber);
        }

        entered.TrySetResult();
        return RunAsync(step, request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        Interlocked.Increment(ref _disposeCalls);
        base.Dispose(disposing);
    }

    private static async Task<HttpResponseMessage> RunAsync(FakeStep step, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await step.Run(request, cancellationToken);
        if (step.AttachRequest)
        {
            response.RequestMessage = request;
        }

        return response;
    }

    private static RecordedRequest Record(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, HeaderStringValues> header in request.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToString();
        }

        var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] body = [];
        if (request.Content is { } content)
        {
            foreach (KeyValuePair<string, HeaderStringValues> header in content.Headers.NonValidated)
            {
                contentHeaders[header.Key] = header.Value.ToString();
            }

            using var buffer = new MemoryStream();
            content.CopyTo(buffer, null, CancellationToken.None);
            body = buffer.ToArray();
        }

        return new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Version,
            request.VersionPolicy,
            headers,
            contentHeaders,
            body);
    }

    // Must be called with _sync held.
    private TaskCompletionSource EnteredSource(int callNumber)
    {
        while (_entered.Count < callNumber)
        {
            _entered.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        return _entered[callNumber - 1];
    }
}
