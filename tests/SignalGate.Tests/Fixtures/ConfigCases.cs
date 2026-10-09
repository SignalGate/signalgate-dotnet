using System;
using Xunit;

namespace SignalGate.Tests.Fixtures;

public static class ConfigCases
{
    public static TheoryData<string, string> Rejected => new()
    {
        { "empty key", "api_key must be a non-empty string" },
        { "whitespace key", "api_key must be a non-empty string" },
        { "null key", "api_key must be a non-empty string" },
        { "key with carriage return", "api_key contains characters that are not allowed in an HTTP header" },
        { "key with line feed", "api_key contains characters that are not allowed in an HTTP header" },
        { "key with nul", "api_key contains characters that are not allowed in an HTTP header" },
        { "key with non-ascii letter", "api_key contains characters that are not allowed in an HTTP header" },
        { "key with delete", "api_key contains characters that are not allowed in an HTTP header" },
        { "negative check timeout", "check_timeout_ms must be a non-negative int, got -1" },
        { "negative log timeout", "log_timeout_ms must be a non-negative int, got -1" },
        { "negative queue capacity", "log_queue_capacity must be a non-negative int, got -1" },
        { "negative max retries", "log_max_retries must be a non-negative int, got -1" },
        { "negative retry base", "log_retry_base_ms must be a non-negative int, got -1" },
        { "minimum int retry base", "log_retry_base_ms must be a non-negative int, got -2147483648" },
        { "zero queue capacity", "log_queue_capacity must be positive" },
        { "zero check timeout", "check_timeout_ms must be at least 1" },
        { "zero log timeout", "log_timeout_ms must be at least 1" },
        { "empty key and negative check timeout", "api_key must be a non-empty string" },
        { "zero check timeout and negative log timeout", "check_timeout_ms must be at least 1" },
        { "negative log timeout and zero queue capacity", "log_timeout_ms must be a non-negative int, got -1" },
        { "zero queue capacity and negative max retries", "log_queue_capacity must be positive" },
        { "negative max retries and negative retry base", "log_max_retries must be a non-negative int, got -1" },
    };

    public static TheoryData<string> Accepted => new()
    {
        "plain key",
        "key without prefix",
        "key with tab",
        "key with surrounding spaces",
        "zero max retries",
        "zero retry base",
        "minimum timeouts and capacity",
    };

    public static SignalGateClientOptions Build(string name)
    {
        return name switch
        {
            "empty key" => Options(""),
            "whitespace key" => Options("   "),
            "null key" => Options(null!),
            "key with carriage return" => Options("pk\rinner"),
            "key with line feed" => Options("pk\ninner"),
            "key with nul" => Options("pk\0inner"),
            "key with non-ascii letter" => Options("pk\u00e9inner"),
            "key with delete" => Options("pk\u007finner"),
            "negative check timeout" => Options(checkTimeoutMs: -1),
            "negative log timeout" => Options(logTimeoutMs: -1),
            "negative queue capacity" => Options(logQueueCapacity: -1),
            "negative max retries" => Options(logMaxRetries: -1),
            "negative retry base" => Options(logRetryBaseMs: -1),
            "minimum int retry base" => Options(logRetryBaseMs: int.MinValue),
            "zero queue capacity" => Options(logQueueCapacity: 0),
            "zero check timeout" => Options(checkTimeoutMs: 0),
            "zero log timeout" => Options(logTimeoutMs: 0),
            "empty key and negative check timeout" => Options("", checkTimeoutMs: -1),
            "zero check timeout and negative log timeout" => Options(checkTimeoutMs: 0, logTimeoutMs: -1),
            "negative log timeout and zero queue capacity" => Options(logTimeoutMs: -1, logQueueCapacity: 0),
            "zero queue capacity and negative max retries" => Options(logQueueCapacity: 0, logMaxRetries: -1),
            "negative max retries and negative retry base" => Options(logMaxRetries: -1, logRetryBaseMs: -1),
            "plain key" => Options(StandardEvent.ApiKey),
            "key without prefix" => Options("test-key"),
            "key with tab" => Options("pk\tinner"),
            "key with surrounding spaces" => Options("  pk_test_unit_key  "),
            "zero max retries" => Options(logMaxRetries: 0),
            "zero retry base" => Options(logRetryBaseMs: 0),
            "minimum timeouts and capacity" => Options(checkTimeoutMs: 1, logTimeoutMs: 1, logQueueCapacity: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown case"),
        };
    }

    private static SignalGateClientOptions Options(
        string apiKey = StandardEvent.ApiKey,
        int checkTimeoutMs = 3000,
        int logTimeoutMs = 1000,
        int logQueueCapacity = 10_000,
        int logMaxRetries = 3,
        int logRetryBaseMs = 200)
    {
        return new SignalGateClientOptions
        {
            ApiKey = apiKey,
            CheckTimeoutMs = checkTimeoutMs,
            LogTimeoutMs = logTimeoutMs,
            LogQueueCapacity = logQueueCapacity,
            LogMaxRetries = logMaxRetries,
            LogRetryBaseMs = logRetryBaseMs,
        };
    }
}
