using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace SignalGate.Internal;

// An immutable, validated copy of SignalGateClientOptions.
internal sealed class ClientSettings
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string _apiKey;

    private ClientSettings(
        string apiKey,
        int checkTimeoutMs,
        int logTimeoutMs,
        int logQueueCapacity,
        int logMaxRetries,
        int logRetryBaseMs,
        bool failOpen,
        ISignalGateLogger? logger,
        HttpMessageHandler? httpHandler)
    {
        _apiKey = apiKey;
        CheckTimeoutMs = checkTimeoutMs;
        LogTimeoutMs = logTimeoutMs;
        LogQueueCapacity = logQueueCapacity;
        LogMaxRetries = logMaxRetries;
        LogRetryBaseMs = logRetryBaseMs;
        FailOpen = failOpen;
        Logger = logger;
        HttpHandler = httpHandler;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string ApiKey => _apiKey;

    public int CheckTimeoutMs { get; }

    public int LogTimeoutMs { get; }

    public int LogQueueCapacity { get; }

    public int LogMaxRetries { get; }

    public int LogRetryBaseMs { get; }

    public bool FailOpen { get; }

    public ISignalGateLogger? Logger { get; }

    public HttpMessageHandler? HttpHandler { get; }

    // Validates options and returns an immutable copy of every value. Throws ArgumentNullException for null
    // options and SignalGateConfigException for an invalid option.
    internal static ClientSettings From(SignalGateClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Each value is read once, so validation and the copy always see the same value.
        string? apiKey = options.ApiKey;
        int checkTimeoutMs = options.CheckTimeoutMs;
        int logTimeoutMs = options.LogTimeoutMs;
        int logQueueCapacity = options.LogQueueCapacity;
        int logMaxRetries = options.LogMaxRetries;
        int logRetryBaseMs = options.LogRetryBaseMs;
        bool failOpen = options.FailOpen;
        ISignalGateLogger? logger = options.Logger;
        HttpMessageHandler? httpHandler = options.HttpHandler;

        ValidateApiKey(apiKey);
        ValidateTimeout("check_timeout_ms", checkTimeoutMs);
        ValidateTimeout("log_timeout_ms", logTimeoutMs);
        ValidateNonNegative("log_queue_capacity", logQueueCapacity);
        if (logQueueCapacity == 0)
        {
            throw new SignalGateConfigException("log_queue_capacity must be positive");
        }

        ValidateNonNegative("log_max_retries", logMaxRetries);
        ValidateNonNegative("log_retry_base_ms", logRetryBaseMs);

        return new ClientSettings(
            apiKey,
            checkTimeoutMs,
            logTimeoutMs,
            logQueueCapacity,
            logMaxRetries,
            logRetryBaseMs,
            failOpen,
            logger,
            httpHandler);
    }

    private static void ValidateApiKey([System.Diagnostics.CodeAnalysis.NotNull] string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SignalGateConfigException("api_key must be a non-empty string");
        }

        foreach (char c in apiKey)
        {
            if (c != '\t' && (c < '\x20' || c > '\x7E'))
            {
                throw new SignalGateConfigException("api_key contains characters that are not allowed in an HTTP header");
            }
        }
    }

    private static void ValidateTimeout(string name, int value)
    {
        ValidateNonNegative(name, value);
        if (value == 0)
        {
            throw new SignalGateConfigException(name + " must be at least 1");
        }
    }

    private static void ValidateNonNegative(string name, int value)
    {
        if (value < 0)
        {
            throw new SignalGateConfigException(
                string.Create(CultureInfo.InvariantCulture, $"{name} must be a non-negative int, got {value}"));
        }
    }
}
