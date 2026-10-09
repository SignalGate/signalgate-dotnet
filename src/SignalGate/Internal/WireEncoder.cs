using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SignalGate.Internal;

// Serializes a SignalGateEvent into the request body with a hand-written Utf8JsonWriter: no reflection, so it
// is safe for trimming and Native AOT.
internal static class WireEncoder
{
    // Upper bound on a serialized event, in bytes.
    internal const int MaxBodyBytes = 1_048_576;

    // The maximum nesting depth of the body, counting the outer object.
    internal const int MaxDepth = 64;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        MaxDepth = MaxDepth,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonEncodedText UserIdName = JsonEncodedText.Encode("user_id");
    private static readonly JsonEncodedText IpName = JsonEncodedText.Encode("ip");
    private static readonly JsonEncodedText MethodName = JsonEncodedText.Encode("method");
    private static readonly JsonEncodedText TimestampName = JsonEncodedText.Encode("timestamp");
    private static readonly JsonEncodedText PayloadName = JsonEncodedText.Encode("payload");
    private static readonly JsonEncodedText EncryptedName = JsonEncodedText.Encode("encrypted");
    private static readonly JsonEncodedText NonceName = JsonEncodedText.Encode("nonce");
    private static readonly JsonEncodedText VName = JsonEncodedText.Encode("v");
    private static readonly JsonEncodedText CustomName = JsonEncodedText.Encode("custom");

    // Returns the UTF-8 request body for evt, without a byte order mark or trailing newline. A null event or a
    // value that cannot be sent throws WireEncodingException; no other exception type escapes.
    internal static byte[] Encode(SignalGateEvent evt)
    {
        if (evt is null)
        {
            throw new WireEncodingException("event is null");
        }

        try
        {
            var buffer = new ArrayBufferWriter<byte>(512);
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                EncryptedPayload payload = evt.Payload;

                writer.WriteStartObject();
                writer.WriteString(UserIdName, Sanitize(evt.UserId));
                writer.WriteString(IpName, Sanitize(evt.Ip));
                writer.WriteString(MethodName, Sanitize(evt.Method));
                writer.WriteString(TimestampName, Sanitize(evt.Timestamp));

                writer.WritePropertyName(PayloadName);
                writer.WriteStartObject();
                writer.WriteString(EncryptedName, Sanitize(payload.Encrypted ?? string.Empty));
                writer.WriteNumber(TimestampName, payload.Timestamp);
                writer.WriteString(NonceName, Sanitize(payload.Nonce ?? string.Empty));
                if (payload.V is int v)
                {
                    writer.WriteNumber(VName, v);
                }

                writer.WriteEndObject();

                if (evt.Custom is { } custom)
                {
                    writer.WritePropertyName(CustomName);
                    WriteObject(writer, custom);
                }

                writer.WriteEndObject();
                writer.Flush();
            }

            if (buffer.WrittenCount > MaxBodyBytes)
            {
                throw TooLarge();
            }

            return buffer.WrittenSpan.ToArray();
        }
        catch (Exception ex) when (ex is not WireEncodingException)
        {
            throw new WireEncodingException("the event could not be serialized", ex);
        }
    }

    // Replaces every unpaired UTF-16 surrogate in value with U+FFFD.
    internal static string Sanitize(string value)
    {
        if (value.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0 || !HasUnpairedSurrogate(value))
        {
            return value;
        }

        return string.Create(value.Length, value, static (destination, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsHighSurrogate(c) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
                {
                    destination[i] = c;
                    destination[i + 1] = source[i + 1];
                    i++;
                }
                else
                {
                    destination[i] = char.IsSurrogate(c) ? '\uFFFD' : c;
                }
            }
        });
    }

    private static bool HasUnpairedSurrogate(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                return true;
            }

            if (char.IsLowSurrogate(c))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case string text:
                writer.WriteStringValue(Sanitize(text));
                return;
            case bool flag:
                writer.WriteBooleanValue(flag);
                return;
            case sbyte number:
                writer.WriteNumberValue(number);
                return;
            case byte number:
                writer.WriteNumberValue(number);
                return;
            case short number:
                writer.WriteNumberValue(number);
                return;
            case ushort number:
                writer.WriteNumberValue(number);
                return;
            case int number:
                writer.WriteNumberValue(number);
                return;
            case uint number:
                writer.WriteNumberValue(number);
                return;
            case long number:
                writer.WriteNumberValue(number);
                return;
            case ulong number:
                writer.WriteNumberValue(number);
                return;
            case float number:
                if (!float.IsFinite(number))
                {
                    throw NotFinite();
                }

                writer.WriteNumberValue(number);
                return;
            case double number:
                if (!double.IsFinite(number))
                {
                    throw NotFinite();
                }

                writer.WriteNumberValue(number);
                return;
            case decimal number:
                writer.WriteNumberValue(number);
                return;
            case JsonElement element:
                if (element.ValueKind == JsonValueKind.Undefined)
                {
                    throw Unsupported(value);
                }

                element.WriteTo(writer);
                return;
            case JsonNode node:
                node.WriteTo(writer);
                return;
            case IReadOnlyDictionary<string, object?> map:
                WriteObject(writer, map);
                return;
            case IDictionary<string, object?> map:
                WriteObject(writer, map);
                return;
            case IDictionary map:
                WriteObject(writer, map);
                return;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                WriteObject(writer, pairs);
                return;
            case byte[]:
                throw Unsupported(value);
            case IEnumerable items:
                WriteArray(writer, items);
                return;
            default:
                throw Unsupported(value);
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        StartContainer(writer);
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> pair in pairs)
        {
            if (pair.Key is null)
            {
                throw NonStringKey();
            }

            writer.WritePropertyName(Sanitize(pair.Key));
            WriteValue(writer, pair.Value);
            EnsureWithinLimit(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteObject(Utf8JsonWriter writer, IDictionary map)
    {
        StartContainer(writer);
        writer.WriteStartObject();
        IDictionaryEnumerator entries = map.GetEnumerator();
        try
        {
            while (entries.MoveNext())
            {
                if (entries.Key is not string key)
                {
                    throw NonStringKey();
                }

                writer.WritePropertyName(Sanitize(key));
                WriteValue(writer, entries.Value);
                EnsureWithinLimit(writer);
            }
        }
        finally
        {
            (entries as IDisposable)?.Dispose();
        }

        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, IEnumerable items)
    {
        StartContainer(writer);
        writer.WriteStartArray();
        foreach (object? item in items)
        {
            WriteValue(writer, item);
            EnsureWithinLimit(writer);
        }

        writer.WriteEndArray();
    }

    private static void StartContainer(Utf8JsonWriter writer)
    {
        // The writer enforces the same limit; checking first gives a clear message and bounds recursion.
        if (writer.CurrentDepth >= MaxDepth)
        {
            throw new WireEncodingException("custom values are nested too deeply or contain a cycle");
        }
    }

    private static void EnsureWithinLimit(Utf8JsonWriter writer)
    {
        // Checked while writing, so a huge or endless sequence stops early.
        if (writer.BytesCommitted + writer.BytesPending > MaxBodyBytes)
        {
            throw TooLarge();
        }
    }

    private static WireEncodingException TooLarge()
    {
        return new WireEncodingException("the serialized event is too large");
    }

    private static WireEncodingException NotFinite()
    {
        return new WireEncodingException("custom numbers must be finite");
    }

    private static WireEncodingException NonStringKey()
    {
        return new WireEncodingException("custom object keys must be strings");
    }

    private static WireEncodingException Unsupported(object value)
    {
        return new WireEncodingException("custom values of type " + value.GetType().FullName + " are not supported");
    }
}
