# SignalGate .NET SDK

[![NuGet](https://img.shields.io/nuget/v/SignalGate.svg)](https://www.nuget.org/packages/SignalGate)
[![CI](https://github.com/SignalGate/signalgate-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/SignalGate/signalgate-dotnet/actions/workflows/ci.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/SignalGate/signalgate-dotnet/blob/main/LICENSE)

The official .NET backend SDK for [SignalGate](https://signalgate.ai). It is a thin server-side forwarder: your
backend builds an event (who, from which IP, which action, when) and attaches the payload that the SignalGate
browser snippet produced on the page. The SDK sends it to the SignalGate API and returns the verdict, or delivers
it in the background after the action. It performs no cryptography and never looks inside the payload: the
payload is an opaque object that you forward unchanged.

## Install

```bash
dotnet add package SignalGate
```

Supports .NET 8 and later: the package targets `net8.0` and `net10.0` and follows Microsoft's .NET support policy.
It has no dependencies, and it is trimming- and Native AOT-compatible.

## Quickstart

```csharp
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
```

- Call `CheckAsync` before the action and `Log` after it.
- Each call needs its own freshly captured payload; a reused payload is rejected (`422 REQUEST_REJECTED`).
- If the browser capture produced no payload, skip the call (current policy).
- Gate on `block`; never on `score`. Every other action, including values added later, lets the request through.
- `CheckAsync` sends one request and never retries it. `Log` returns at once and never throws: a single background
  task delivers the event and retries it on failure.
- A client is safe for concurrent use. In a real application create **one client per process** and share it (see
  [ASP.NET Core](#aspnet-core)), then dispose it on shutdown.

### Events

A `SignalGateEvent` holds:

| Field | Meaning |
|---|---|
| `userId` | Your identifier for the user or account the action is about. |
| `ip` | The client IP address of the request (see [client IP](#client-ip) behind a proxy). |
| `method` | The name of the action, for example `login`, `signup.send_code` or `checkout`. |
| `timestamp` | An ISO 8601 / RFC 3339 timestamp with an offset, e.g. `2026-04-01T13:08:50+00:00`. The `DateTimeOffset` overloads format it for you. |
| `payload` | An `EncryptedPayload`: the opaque object produced by the SignalGate browser snippet; forward it unchanged. |
| `custom` | Optional extra fields of your own (see [supported `custom` values](#custom-values)). |

`SignalGateEvent.TryCreate` returns `false` when a required value is null or the payload is missing or incomplete,
so a request without a payload simply skips the call. It only checks for nulls and never inspects the payload. The
constructors throw `ArgumentNullException` in the same cases. Empty strings are passed through as they are.

```csharp
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
```

## ASP.NET Core

Register the client as a singleton **through a factory**. The container then owns it and disposes it on host
shutdown, which delivers the queued log events:

```csharp
builder.Services.AddSingleton(_ => new SignalGateClient(builder.Configuration["SignalGate:ApiKey"]!));
```

An instance passed directly, as in `AddSingleton(new SignalGateClient(...))`, is **not** disposed by the container.

`EncryptedPayload` binds from a JSON request body with `System.Text.Json`, so the payloads your frontend sends can
be properties of your own request type. With the frontend sending them as `signalgate` and `signalgate_log`, the
first binds to `SignalGate` by name; the second needs `[JsonPropertyName]` (from `System.Text.Json.Serialization`):

```csharp
public sealed record CheckoutRequest(
    string CartId,
    EncryptedPayload? SignalGate,
    [property: JsonPropertyName("signalgate_log")] EncryptedPayload? SignalGateLog);
```

Keep handlers async: await `CheckAsync`, and never call `.Result` or `.Wait()` on it.

```csharp
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
```

### Client IP

Behind a reverse proxy or load balancer, `HttpContext.Connection.RemoteIpAddress` is the proxy's address until you
enable forwarded headers. Trust only your own proxies: limit `KnownProxies` / `KnownNetworks` (`KnownIPNetworks`
on .NET 10) to them and never clear these lists, and set `ForwardLimit` to the number of proxy hops in front of the
app. Then pass `HttpContext.Connection.RemoteIpAddress`. On a dual-stack listener an IPv4 client appears as an
IPv4-mapped IPv6 address such as `::ffff:203.0.113.7`; the example above converts it back with `MapToIPv4()`.

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.KnownProxies.Add(IPAddress.Parse("10.0.0.10"));
    options.ForwardLimit = 1;
});

var app = builder.Build();
app.UseForwardedHeaders();
```

## Configuration

| Option | Default | Rules |
|---|---|---|
| `ApiKey` | required | Not empty or whitespace. Every character must be a tab or printable ASCII. Sent exactly as given, never trimmed. |
| `CheckTimeoutMs` | `3000` | At least 1. The deadline for one check, covering connect, send and the whole response. |
| `LogTimeoutMs` | `1000` | At least 1. The deadline for each delivery attempt of a logged event. |
| `LogQueueCapacity` | `10000` | At least 1. Events waiting for delivery; when the queue is full, the newest event is dropped and counted. The SDK also bounds the total size of queued events. |
| `LogMaxRetries` | `3` | Zero or more. Retries after a failed delivery attempt, so up to 4 attempts by default. |
| `LogRetryBaseMs` | `200` | Zero or more. The delay before the first retry; it doubles for each later retry: 200, 400 and 800 ms by default, with no jitter. |
| `FailOpen` | `true` | See [Error handling](#error-handling). |
| `Logger` | `null` | An `ISignalGateLogger`; `null` disables logging. See [Logging](#logging). |
| `HttpHandler` | `null` | An `HttpMessageHandler` for every request; `null` means the SDK creates and owns its own. |

An invalid option makes the constructor throw `SignalGateConfigException`. The client copies every value when it is
constructed, so changing the options object afterwards has no effect. `new SignalGateClient(apiKey)` uses the
defaults for everything else.

A logged event is retried after a 5xx status, a timeout or a network error. A 4xx or 3xx status is not retried.

### Custom `HttpHandler`

Pass your own handler for a proxy, custom TLS settings or tracing:

```csharp
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
```

- The SDK never disposes it.
- Set `AllowAutoRedirect = false`. The SDK never follows redirects, and a redirected response is treated as a
  network error.
- Do not add a retry handler: the SDK already applies the retry policy the service expects, and extra retries are
  rejected.
- Make the handler observe the cancellation token it receives. The SDK stops waiting for a request at its deadline
  (`CheckTimeoutMs`, `LogTimeoutMs` or the `CloseAsync` deadline) even if the handler ignores the token while it
  waits asynchronously, but it cannot stop that handler, which keeps running in the background until it returns.
  A handler or logger that blocks its calling thread can delay `CheckAsync` and `CloseAsync` until it returns.

### Shutdown

Dispose the client on shutdown with `CloseAsync`, `DisposeAsync` or `Dispose`, so that queued log events are
delivered. Events still queued when the process exits are lost.

```csharp
// Deliver queued log events, waiting at most 2 seconds.
await client.CloseAsync(TimeSpan.FromSeconds(2));
```

- Without an argument, `CloseAsync` waits up to `5 × LogTimeoutMs` (5 seconds by default). `TimeSpan.Zero` stops
  at once and `Timeout.InfiniteTimeSpan` waits for every queued event.
- When the deadline passes, the attempt in flight is cancelled, and every event not yet delivered is counted as
  `log_dropped_total{reason="closed"}`.
- Calling it again, or concurrently, returns the same task; the arguments of later calls are ignored. After close,
  `CheckAsync` throws `SignalGateConfigException` and `Log` ignores events.
- A negative deadline other than `Timeout.InfiniteTimeSpan` throws `ArgumentOutOfRangeException`.
- Called from a logger or handler while it runs on the background delivery task, `CloseAsync` only stops accepting
  events and returns at once; close the client again from outside to wait for delivery.
- Once `CloseAsync` returns, every accepted event is counted as sent or dropped, and no log delivery starts
  afterwards. A `CheckAsync` call that started before close may still complete, or fail open, after that.

### Requests

- `POST https://api.signalgate.ai/v0/check` and `POST https://api.signalgate.ai/v0/log`, with
  `Authorization: Bearer pk_live_…` and `Content-Type: application/json`.
- `User-Agent: signalgate-backend-sdk/0.1.0 (dotnet/<runtime version>; <os>)`.
- `X-Request-Id`: a new random id for every HTTP attempt. `SignalGateServerException.RequestId` carries the id the
  service reported, or the one that was sent.
- `Idempotency-Key` is sent on every request and is identical across retries of one log event; it is accepted but not yet processed server-side.

## Error handling

With `FailOpen = true` (the default), a check that cannot get a verdict does not break your request: it returns
`allow` with score `0.0`, empty ids and `FailedOpen = true`, and logs a `signalgate.check.failed_open` warning.

| Outcome of `CheckAsync` | `FailOpen = true` (default) | `FailOpen = false` |
|---|---|---|
| Timeout (`CheckTimeoutMs`) | `allow`, `FailedOpen = true` | `SignalGateTimeoutException` |
| Network error, or a redirected response | `allow`, `FailedOpen = true` | `SignalGateNetworkException` |
| 5xx status | `allow`, `FailedOpen = true` | `SignalGateServerException` |
| 2xx without a usable verdict | `allow`, `FailedOpen = true` | `SignalGateServerException` with code `MALFORMED_RESPONSE` |
| 4xx or 3xx status | `SignalGateServerException` | `SignalGateServerException` |
| Your `cancellationToken` is cancelled | `OperationCanceledException` | `OperationCanceledException` |

```csharp
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
```

### Exceptions

The SDK's own exception types all derive from `SignalGateException`.

| Exception | When |
|---|---|
| `SignalGateConfigException` | An option is invalid (constructor), or `CheckAsync` was called on a closed client. |
| `SignalGateTimeoutException` | The check deadline passed (only with `FailOpen = false`). |
| `SignalGateNetworkException` | DNS, TCP, TLS or I/O failure, any other exception from the handler, or a redirected response (only with `FailOpen = false`). `InnerException` holds the underlying exception, when there is one. |
| `SignalGateServerException` | A 4xx or 3xx status; or, with `FailOpen = false`, a 5xx status or a 2xx without a usable verdict. |
| `ArgumentNullException`, `ArgumentException` | `CheckAsync` got a null event, an unsupported `custom` value or an event too large to send. Thrown synchronously, before any request. |
| `OperationCanceledException` | Your `cancellationToken` was cancelled. It carries your token and never fails open. |

`SignalGateServerException` exposes `StatusCode`, `Code`, `ServerMessage`, `RequestId` and `Details`, and its
`Message` has the format `[status] code: message`, for example `[422] REQUEST_REJECTED: ...`.

| Status | Code | Meaning |
|---|---|---|
| 400 | `BAD_REQUEST` | The request is malformed. |
| 400 | `INVALID_PAYLOAD` | The payload is not usable; forward the browser snippet's object unchanged. |
| 401 | `UNAUTHORIZED` | The API key is missing or not valid. |
| 422 | `REQUEST_REJECTED` | The request was rejected, for example because its payload was already used. |
| 5xx | | Service degraded; check fails open by default. |

### `custom` values

`custom` values are serialized when you call `CheckAsync` or `Log`. Supported values:

- `null`, `string`, `bool`;
- every integer type from `sbyte` to `ulong` (64-bit values are sent exactly);
- `float`, `double` and `decimal`, when finite;
- `JsonElement` and `JsonNode`, written as they are;
- dictionaries with `string` keys (any `IDictionary`, `IDictionary<string, object?>`,
  `IReadOnlyDictionary<string, object?>` or `IEnumerable<KeyValuePair<string, object?>>`), written as objects;
- other collections (arrays, lists, sets), written as arrays.

Anything else is unsupported: for example `Guid`, enums, `DateTime`, `DateTimeOffset`, `char`, `byte[]`, NaN,
infinity, a dictionary with non-string keys, a cycle, or nesting more than 63 levels deep (counting `custom`
itself). Convert such values to strings or numbers first. The SDK also limits the serialized size of an event, so
an event that is too large, with or without `custom`, cannot be sent either. On an unsupported value or an event
that is too large:

- `CheckAsync` throws `ArgumentException` (with `InnerException` set) before sending anything;
- `Log` drops the event and logs the error `signalgate.log.invalid_event`. `Log` never throws.

### Verdicts

`CheckResult` has `Action`, `Score`, `RequestId`, `TenantId`, `Timestamp`, `ProcessingTimeUs` and `FailedOpen`.

| `Action` | `CheckActions` constant | Score |
|---|---|---|
| `allow` | `CheckActions.Allow` | 0.0 |
| `admin_alert` | `CheckActions.AdminAlert` | 0.25 |
| `dry_run_block` | `CheckActions.DryRunBlock` | 0.5 |
| `block` | `CheckActions.Block` | 1.0 |

Gate on `block`; never on `score`. An action the SDK does not know is returned as it is, with a
`signalgate.check.unknown_action` warning; let it through.

## Metrics

`client.Metrics` holds eight counters. A counter exists only after its first increment.

| Counter | Labels | Counts |
|---|---|---|
| `check_total` | | Checks accepted for sending: the client was open and the event was valid. Counted before the request goes out. |
| `check_success_total` | | Checks that returned a real verdict. |
| `check_failed_open_total` | | Checks that returned a synthesized `allow`. |
| `check_error_total` | `type`: `TimeoutError`, `NetworkError`, `ServerError` or `MalformedResponse` | Checks that hit a timeout, a network error, a non-2xx status or a 2xx without a usable verdict, including those that then failed open. Cancellation by your token and argument errors are not counted. |
| `log_enqueued_total` | | Events that `Log` accepted into the queue. |
| `log_sent_total` | | Delivery attempts that received a 2xx status. |
| `log_http_error_total` | `status`: the status code, e.g. `"503"`, or `"network"` (timeouts count as `network`) | Every failed delivery attempt, including 4xx statuses and the last attempt. Attempts aborted by `CloseAsync` are not counted. |
| `log_dropped_total` | `reason`: `queue_full`, `closed` or `retry_exhausted` | Events that were not delivered. |

Once `CloseAsync` returns, every enqueued event is counted exactly once:
`log_enqueued_total = log_sent_total + log_dropped_total{reason="closed"} + log_dropped_total{reason="retry_exhausted"}`.
While the client is open, each `Log` call with a valid event adds one to either `log_enqueued_total` or
`log_dropped_total{reason="queue_full"}`.

`Get(name, labels)` reads one counter (0 when it does not exist yet), `Snapshot()` returns every counter as a
`MetricSample` (name, labels, value), and `SnapshotFlat()` returns them keyed as `name` or `name{key="value"}`:

```csharp
long failedOpen = client.Metrics.Get("check_failed_open_total");
long timeouts = client.Metrics.Get("check_error_total",
    new Dictionary<string, string> { ["type"] = "TimeoutError" });
Console.WriteLine($"failed open: {failedOpen}, timeouts: {timeouts}");

// For example: check_error_total{type="TimeoutError"}=1
foreach (KeyValuePair<string, long> counter in client.Metrics.SnapshotFlat())
{
    Console.WriteLine($"{counter.Key}={counter.Value}");
}
```

## Logging

The SDK has no logging dependency. Pass an `ISignalGateLogger` as `Logger` to receive its diagnostic events. The
interface has four methods, `Debug`, `Info`, `Warn` and `Error`, each taking
`(string message, IReadOnlyDictionary<string, object?>? fields = null)`; `NoopSignalGateLogger.Instance` discards
everything.

The message is an event name and the fields use snake_case keys. The API key is never logged: the `Authorization`
header always appears as `Bearer ***REDACTED***`. Calls may come from a background thread, are never made while the
SDK holds a lock, and any exception they throw is caught and ignored. Implementations should return quickly and must
not block: some calls are made on the thread that called `Log` or `CheckAsync`.

| Level | Event | Fields |
|---|---|---|
| debug | `signalgate.http.request` | `url`, `request_id`, `headers` |
| debug | `signalgate.http.response` | `url`, `request_id`, `status` |
| warn | `signalgate.http.timeout` | `url`, `request_id` |
| warn | `signalgate.http.network_error` | `url`, `request_id`, `error` |
| warn | `signalgate.check.failed_open` | `error_type` (`TimeoutError`, `NetworkError`, `ServerError` or `MalformedResponse`), `error` |
| warn | `signalgate.check.unknown_action` | `action` |
| error | `signalgate.log.queue_full` | none |
| error | `signalgate.log.invalid_event` | `reason` |
| warn | `signalgate.log.dropped_4xx` | `status`, `code` |
| warn | `signalgate.log.dropped_network` | `error` |
| error | `signalgate.log.worker_unhandled` | `error` |

`signalgate.log.dropped_4xx` is logged for a 3xx status too.

An adapter to `Microsoft.Extensions.Logging` (it needs `using Microsoft.Extensions.Logging;`):

```csharp
public sealed class SignalGateLoggerAdapter(ILogger<SignalGateClient> logger) : ISignalGateLogger
{
    public void Debug(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Debug, message, fields);

    public void Info(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Information, message, fields);

    public void Warn(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Warning, message, fields);

    public void Error(string message, IReadOnlyDictionary<string, object?>? fields = null) =>
        Write(LogLevel.Error, message, fields);

    private void Write(LogLevel level, string message, IReadOnlyDictionary<string, object?>? fields)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        // The fields travel as structured state; the text is the event name.
        var state = new List<KeyValuePair<string, object?>> { new("event", message) };
        if (fields is not null)
        {
            state.AddRange(fields);
        }

        logger.Log(level, default, state, null, (_, _) => message);
    }
}
```

Register it together with the client:

```csharp
builder.Services.AddSingleton<SignalGateLoggerAdapter>();
builder.Services.AddSingleton(services => new SignalGateClient(new SignalGateClientOptions
{
    ApiKey = builder.Configuration["SignalGate:ApiKey"]!,
    Logger = services.GetRequiredService<SignalGateLoggerAdapter>(),
}));
```

## Development

Requires the .NET 10 SDK (pinned in `global.json`) and the .NET 8 runtime; the tests run on both. The same
commands run in CI:

```bash
dotnet format SignalGate.slnx --verify-no-changes
dotnet build SignalGate.slnx -c Release
dotnet test --solution SignalGate.slnx -c Release --no-build
dotnet pack src/SignalGate/SignalGate.csproj -c Release -o artifacts --no-build
```

The tests drive the client through an in-process fake HTTP handler, so they need no network access and no API key.
Every C# example in this README is compiled and compared with the code under `tests/SignalGate.DocSnippets`. See
[CONTRIBUTING.md](https://github.com/SignalGate/signalgate-dotnet/blob/main/CONTRIBUTING.md) and
[SECURITY.md](https://github.com/SignalGate/signalgate-dotnet/blob/main/SECURITY.md).

## License

Licensed under the [Apache License, Version 2.0](https://github.com/SignalGate/signalgate-dotnet/blob/main/LICENSE).

## Links

- SignalGate: <https://signalgate.ai>
- Backend integration docs for C#: <https://signalgate.ai/docs/backend?stack=csharp>
- Changelog: <https://github.com/SignalGate/signalgate-dotnet/blob/main/CHANGELOG.md>
