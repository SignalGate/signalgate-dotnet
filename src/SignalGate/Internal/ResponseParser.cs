using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace SignalGate.Internal;

// Reads response envelopes. Every method tolerates any body and never throws.
internal static class ResponseParser
{
    internal const string MalformedCode = "MALFORMED_RESPONSE";

    internal const string MalformedMessage = "response envelope missing data field";

    // Reads the verdict from a 2xx body. Returns false when the body is not a JSON object or its data member is
    // missing or not an object; result must then be ignored.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any unreadable body is reported as malformed.")]
    internal static bool TryParseCheckResult(byte[] body, out CheckResult result)
    {
        result = null!;
        try
        {
            using JsonDocument? document = TryParseDocument(body);
            if (document is null
                || document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            result = new CheckResult(
                GetString(data, "action"),
                GetScore(data),
                GetString(data, "request_id"),
                GetString(data, "tenant_id"),
                GetString(data, "timestamp"),
                GetProcessingTime(data),
                failedOpen: false);
            return true;
        }
        catch (Exception)
        {
            result = null!;
            return false;
        }
    }

    // Builds the exception for a non-2xx response. The request id is a non-empty error.request_id, else
    // responseRequestId when non-empty, else sentRequestId.
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any unreadable body yields empty error fields.")]
    internal static SignalGateServerException ToServerException(
        int statusCode,
        byte[] body,
        string? responseRequestId,
        string sentRequestId)
    {
        string code = string.Empty;
        string message = string.Empty;
        string errorRequestId = string.Empty;
        IReadOnlyDictionary<string, string>? details = null;

        try
        {
            using JsonDocument? document = TryParseDocument(body);
            if (document is not null
                && document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.Object)
            {
                code = GetString(error, "code");
                message = GetString(error, "message");
                errorRequestId = GetString(error, "request_id");
                if (error.TryGetProperty("details", out JsonElement detailsElement)
                    && detailsElement.ValueKind == JsonValueKind.Object)
                {
                    details = ReadDetails(detailsElement);
                }
            }
        }
        catch (Exception)
        {
            // Keep whatever was read before the failure.
        }

        string requestId;
        if (errorRequestId.Length > 0)
        {
            requestId = errorRequestId;
        }
        else if (!string.IsNullOrEmpty(responseRequestId))
        {
            requestId = responseRequestId;
        }
        else
        {
            requestId = sentRequestId ?? string.Empty;
        }

        return new SignalGateServerException(statusCode, code, message, requestId, details);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Any unreadable body is treated as an empty object.")]
    private static JsonDocument? TryParseDocument(byte[]? body)
    {
        if (body is null || body.Length == 0)
        {
            return null;
        }

        ReadOnlyMemory<byte> json = body;
        ReadOnlySpan<byte> byteOrderMark = [0xEF, 0xBB, 0xBF];
        if (json.Span.StartsWith(byteOrderMark))
        {
            json = json[3..];
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetString(JsonElement owner, string name)
    {
        if (owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return TryGetString(value, out string? text) ? text : string.Empty;
        }

        return string.Empty;
    }

    private static bool TryGetString(JsonElement value, [NotNullWhen(true)] out string? text)
    {
        try
        {
            text = value.GetString();
            return text is not null;
        }
        catch (InvalidOperationException)
        {
            text = null;
            return false;
        }
    }

    private static double GetScore(JsonElement data)
    {
        if (data.TryGetProperty("score", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double score)
            && double.IsFinite(score))
        {
            return score;
        }

        return 0.0;
    }

    private static long GetProcessingTime(JsonElement data)
    {
        if (!data.TryGetProperty("processing_time_us", out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (value.TryGetInt64(out long whole))
        {
            return whole;
        }

        // -2^63 is exactly representable; 2^63 is the first value above long.MaxValue.
        if (value.TryGetDouble(out double number)
            && double.IsFinite(number)
            && number >= -9_223_372_036_854_775_808.0
            && number < 9_223_372_036_854_775_808.0)
        {
            return (long)number;
        }

        return 0;
    }

    private static ReadOnlyDictionary<string, string> ReadDetails(JsonElement details)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in details.EnumerateObject())
        {
            JsonElement value = property.Value;
            entries[property.Name] = value.ValueKind == JsonValueKind.String && TryGetString(value, out string? text)
                ? text
                : value.GetRawText();
        }

        return new ReadOnlyDictionary<string, string>(entries);
    }
}
