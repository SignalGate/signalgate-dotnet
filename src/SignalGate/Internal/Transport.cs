using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGate.Internal;

// Sends one POST attempt with the SDK's headers and deadline, and classifies the outcome. It never retries.
internal sealed class Transport
{
    // Upper bound on a buffered response body, in bytes.
    internal const int MaxResponseBytes = 1_048_576;

    internal const string JsonMediaType = "application/json";

    internal const string RedirectedMessage = "the response was redirected; redirects are not followed";

    private const string RequestIdHeader = "X-Request-Id";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly HttpClient _httpClient;
    private readonly SafeLogger _logger;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string _authorization;

    internal Transport(HttpClient httpClient, string apiKey, SafeLogger logger)
    {
        _httpClient = httpClient;
        _authorization = "Bearer " + apiKey;
        _logger = logger;
    }

    // Creates the HttpClient used for every request. Without a tenant handler the SDK owns a SocketsHttpHandler,
    // returned in ownedHandler, which the HttpClient disposes. A tenant handler is never disposed.
    internal static HttpClient CreateHttpClient(HttpMessageHandler? tenantHandler, out SocketsHttpHandler? ownedHandler)
    {
        HttpClient client;
        if (tenantHandler is null)
        {
            ownedHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ActivityHeadersPropagator = null,
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
                EnableMultipleHttp2Connections = true,
                UseProxy = true,
            };
            client = new HttpClient(ownedHandler, disposeHandler: true);
        }
        else
        {
            ownedHandler = null;
            client = new HttpClient(tenantHandler, disposeHandler: false);
        }

        client.Timeout = Timeout.InfiniteTimeSpan;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        return client;
    }

    // Sends body to uri once, with a fresh X-Request-Id, under a deadline of timeoutMs linked to outerToken.
    // The wait ends at the deadline even when the handler ignores cancellation; such a handler call is left to
    // finish on its own, and its late outcome is observed and released. Never throws: every failure is
    // classified and returned.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every failure of the handler pipeline is classified and returned.")]
    internal async Task<AttemptResult> SendAsync(
        Uri uri,
        byte[] body,
        string idempotencyKey,
        int timeoutMs,
        CancellationToken outerToken)
    {
        string requestId = Uuid.NewV4();
        if (outerToken.IsCancellationRequested)
        {
            return AttemptResult.FromFailure(AttemptOutcome.Canceled, requestId, new OperationCanceledException(outerToken));
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        HttpRequestMessage? request = null;
        Task<HttpResponseMessage>? send = null;
        HttpResponseMessage? response = null;
        try
        {
            deadline.CancelAfter(Deadline.ToTimeSpan(timeoutMs));

            request = CreateRequest(uri, body, idempotencyKey, requestId);
            LogRequest(uri, requestId, idempotencyKey);

            send = _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
            response = await send.WaitAsync(deadline.Token).ConfigureAwait(false);

            int statusCode = (int)response.StatusCode;
            LogResponse(uri, requestId, statusCode);

            if (IsRedirected(response, uri))
            {
                var redirected = new SignalGateNetworkException(RedirectedMessage);
                LogNetworkError(uri, requestId, redirected.Message);
                return AttemptResult.FromFailure(AttemptOutcome.NetworkError, requestId, redirected);
            }

            // ResponseContentRead has already buffered the body, so this read cannot wait on the handler. The
            // deadline no longer applies: a response that was received counts as received.
            byte[] responseBody = await response.Content.ReadAsByteArrayAsync(CancellationToken.None).ConfigureAwait(false);
            return AttemptResult.FromResponse(requestId, statusCode, responseBody, FirstResponseRequestId(response));
        }
        catch (Exception ex)
        {
            return Classify(ex, uri, requestId, timeoutMs, outerToken, deadline.Token);
        }
        finally
        {
            if (response is not null)
            {
                response.Dispose();
                request?.Dispose();
            }
            else if (send is not null)
            {
                ReleaseWhenDone(send, request!);
            }
            else
            {
                request?.Dispose();
            }
        }
    }

    // Disposes request once send has finished, together with a response that arrived after the wait ended, and
    // observes a late failure so that it is never reported as unobserved.
    private static void ReleaseWhenDone(Task<HttpResponseMessage> send, HttpRequestMessage request)
    {
        _ = send.ContinueWith(
            static (finished, state) => Release(finished, (HttpRequestMessage)state!),
            request,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Releasing an abandoned request must never fail.")]
    private static void Release(Task<HttpResponseMessage> finished, HttpRequestMessage request)
    {
        try
        {
            if (finished.IsCompletedSuccessfully)
            {
                finished.Result?.Dispose();
            }
            else
            {
                _ = finished.Exception;
            }

            request.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private AttemptResult Classify(
        Exception exception,
        Uri uri,
        string requestId,
        int timeoutMs,
        CancellationToken outerToken,
        CancellationToken deadlineToken)
    {
        if (outerToken.IsCancellationRequested)
        {
            return AttemptResult.FromFailure(AttemptOutcome.Canceled, requestId, exception);
        }

        if (deadlineToken.IsCancellationRequested)
        {
            var timeout = new SignalGateTimeoutException(
                string.Create(CultureInfo.InvariantCulture, $"request timed out after {timeoutMs} ms"),
                exception);
            LogTimeout(uri, requestId);
            return AttemptResult.FromFailure(AttemptOutcome.Timeout, requestId, timeout);
        }

        var network = new SignalGateNetworkException("network error: " + exception.Message, exception);
        LogNetworkError(uri, requestId, network.Message);
        return AttemptResult.FromFailure(AttemptOutcome.NetworkError, requestId, network);
    }

    private HttpRequestMessage CreateRequest(Uri uri, byte[] body, string idempotencyKey, string requestId)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(JsonMediaType);
        content.Headers.ContentLength = body.Length;

        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = content,
        };
        request.Headers.TryAddWithoutValidation("Authorization", _authorization);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent.Value);
        request.Headers.TryAddWithoutValidation(RequestIdHeader, requestId);
        request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
        return request;
    }

    // A response counts as redirected when the handler reports a final request URI other than the one sent.
    // A handler that reports no request message is trusted.
    private static bool IsRedirected(HttpResponseMessage response, Uri sentUri)
    {
        HttpRequestMessage? finalRequest = response.RequestMessage;
        return finalRequest is not null && finalRequest.RequestUri != sentUri;
    }

    private static string? FirstResponseRequestId(HttpResponseMessage response)
    {
        if (response.Headers.NonValidated.TryGetValues(RequestIdHeader, out HeaderStringValues values))
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private void LogRequest(Uri uri, string requestId, string idempotencyKey)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        var headers = new Dictionary<string, string>(5, StringComparer.Ordinal)
        {
            ["Authorization"] = Redaction.AuthorizationValue,
            ["User-Agent"] = UserAgent.Value,
            [RequestIdHeader] = requestId,
            [IdempotencyKeyHeader] = idempotencyKey,
            ["Content-Type"] = JsonMediaType,
        };

        _logger.Debug(LogEvents.HttpRequest, new Dictionary<string, object?>(3, StringComparer.Ordinal)
        {
            ["url"] = uri.AbsoluteUri,
            ["request_id"] = requestId,
            ["headers"] = new ReadOnlyDictionary<string, string>(headers),
        });
    }

    private void LogResponse(Uri uri, string requestId, int statusCode)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        _logger.Debug(LogEvents.HttpResponse, new Dictionary<string, object?>(3, StringComparer.Ordinal)
        {
            ["url"] = uri.AbsoluteUri,
            ["request_id"] = requestId,
            ["status"] = statusCode,
        });
    }

    private void LogTimeout(Uri uri, string requestId)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        _logger.Warn(LogEvents.HttpTimeout, new Dictionary<string, object?>(2, StringComparer.Ordinal)
        {
            ["url"] = uri.AbsoluteUri,
            ["request_id"] = requestId,
        });
    }

    private void LogNetworkError(Uri uri, string requestId, string error)
    {
        if (!_logger.IsEnabled)
        {
            return;
        }

        _logger.Warn(LogEvents.HttpNetworkError, new Dictionary<string, object?>(3, StringComparer.Ordinal)
        {
            ["url"] = uri.AbsoluteUri,
            ["request_id"] = requestId,
            ["error"] = error,
        });
    }
}
