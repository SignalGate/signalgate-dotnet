using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGate.Tests.Fakes;

// Response content that records whether it was disposed. Its body is written at once, whatever the token.
public sealed class TrackedContent : HttpContent
{
    private readonly byte[] _body;
    private int _disposed;

    public TrackedContent(string body)
    {
        _body = Encoding.UTF8.GetBytes(body);
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        stream.Write(_body);
        return Task.CompletedTask;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        return SerializeToStreamAsync(stream, context);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        Interlocked.Exchange(ref _disposed, 1);
        base.Dispose(disposing);
    }
}
