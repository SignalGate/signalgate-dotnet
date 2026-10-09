using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGate.AotSmoke;

// Answers every request in process: a block verdict for checks and an acknowledgement for log events. It
// records the path and body of each request.
public sealed class InProcessHandler : HttpMessageHandler
{
    private const string BlockVerdict =
        """{"ok":true,"data":{"request_id":"req_smoke","action":"block","score":1.0,"tenant_id":"t_smoke","processing_time_us":10,"timestamp":"2026-04-01T13:08:50Z"},"error":null}""";

    private const string Acknowledged = """{"ok":true,"data":null,"error":null}""";

    private readonly object _sync = new();
    private readonly List<(string Path, byte[] Body)> _requests = [];

    public IReadOnlyList<(string Path, byte[] Body)> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? string.Empty;
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        lock (_sync)
        {
            _requests.Add((path, body));
        }

        string json = path.EndsWith("/v0/check", StringComparison.Ordinal) ? BlockVerdict : Acknowledged;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json)),
            RequestMessage = request,
        };
    }
}
