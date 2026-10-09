using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SignalGate;

/// <summary>
/// One event sent to SignalGate by <c>CheckAsync</c> or <c>Log</c>. Instances are immutable.
/// </summary>
/// <remarks>
/// Every required value must be non-null; empty strings are passed through as given. Each call needs its
/// own freshly captured payload. Use <see cref="TryCreate"/> when the browser capture may not have produced
/// a payload.
/// </remarks>
public sealed class SignalGateEvent
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

    /// <summary>
    /// Creates an event with a timestamp string that is sent exactly as given.
    /// </summary>
    /// <param name="userId">Your identifier for the user performing the action.</param>
    /// <param name="ip">The client IP address of the request.</param>
    /// <param name="method">The name of the action, for example <c>login</c>.</param>
    /// <param name="timestamp">An ISO 8601 / RFC 3339 timestamp with an offset, e.g. <c>2026-04-01T13:08:50+00:00</c>.</param>
    /// <param name="payload">The opaque object produced by the SignalGate browser snippet; forward it unchanged.</param>
    /// <param name="custom">Optional extra values sent with the event. The dictionary is copied; nested values are read when the event is sent.</param>
    /// <exception cref="ArgumentNullException">A required argument is null, or <paramref name="payload"/> has a null
    /// <see cref="EncryptedPayload.Encrypted"/> or <see cref="EncryptedPayload.Nonce"/>, or <paramref name="custom"/> has a null key.</exception>
#pragma warning disable RS0026 // Two constructor overloads share the optional custom parameter by design.
    public SignalGateEvent(
        string userId,
        string ip,
        string method,
        string timestamp,
        EncryptedPayload payload,
        IReadOnlyDictionary<string, object?>? custom = null)
#pragma warning restore RS0026
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(ip);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(timestamp);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Encrypted is null)
        {
            throw new ArgumentNullException(nameof(payload), "The payload's Encrypted value is null.");
        }

        if (payload.Nonce is null)
        {
            throw new ArgumentNullException(nameof(payload), "The payload's Nonce value is null.");
        }

        if (!TryCopyCustom(custom, out ReadOnlyDictionary<string, object?>? copy))
        {
            throw new ArgumentNullException(nameof(custom), "The custom dictionary contains a null key.");
        }

        UserId = userId;
        Ip = ip;
        Method = method;
        Timestamp = timestamp;
        Payload = payload;
        Custom = copy;
    }

    /// <summary>
    /// Creates an event whose timestamp is formatted as <c>yyyy-MM-dd'T'HH:mm:ss.fffzzz</c>, so a UTC value
    /// is written with a <c>+00:00</c> offset.
    /// </summary>
    /// <param name="userId">Your identifier for the user performing the action.</param>
    /// <param name="ip">The client IP address of the request.</param>
    /// <param name="method">The name of the action, for example <c>login</c>.</param>
    /// <param name="timestamp">When the action happened.</param>
    /// <param name="payload">The opaque object produced by the SignalGate browser snippet; forward it unchanged.</param>
    /// <param name="custom">Optional extra values sent with the event. The dictionary is copied; nested values are read when the event is sent.</param>
    /// <exception cref="ArgumentNullException">A required argument is null, or <paramref name="payload"/> has a null
    /// <see cref="EncryptedPayload.Encrypted"/> or <see cref="EncryptedPayload.Nonce"/>, or <paramref name="custom"/> has a null key.</exception>
#pragma warning disable RS0026 // Two constructor overloads share the optional custom parameter by design.
    public SignalGateEvent(
        string userId,
        string ip,
        string method,
        DateTimeOffset timestamp,
        EncryptedPayload payload,
        IReadOnlyDictionary<string, object?>? custom = null)
#pragma warning restore RS0026
        : this(userId, ip, method, FormatTimestamp(timestamp), payload, custom)
    {
    }

    /// <summary>
    /// Your identifier for the user performing the action.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// The client IP address of the request.
    /// </summary>
    public string Ip { get; }

    /// <summary>
    /// The name of the action, for example <c>login</c>.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// An ISO 8601 / RFC 3339 timestamp with an offset, e.g. <c>2026-04-01T13:08:50+00:00</c>.
    /// </summary>
    public string Timestamp { get; }

    /// <summary>
    /// The opaque object produced by the SignalGate browser snippet; forward it unchanged.
    /// </summary>
    public EncryptedPayload Payload { get; }

    /// <summary>
    /// Optional extra values sent with the event, or <see langword="null"/> when none were given.
    /// This is a read-only copy of the dictionary passed to the constructor.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Custom { get; }

    /// <summary>
    /// Creates an event when every required value is present, and returns <see langword="false"/> instead of
    /// throwing when one is null. Use it when the browser capture may not have produced a payload; if it did not,
    /// skip the call.
    /// </summary>
    /// <param name="userId">Your identifier for the user performing the action.</param>
    /// <param name="ip">The client IP address of the request.</param>
    /// <param name="method">The name of the action, for example <c>login</c>.</param>
    /// <param name="timestamp">When the action happened.</param>
    /// <param name="payload">The opaque object produced by the SignalGate browser snippet; forward it unchanged.</param>
    /// <param name="evt">The created event, or <see langword="null"/> when the method returns <see langword="false"/>.</param>
    /// <param name="custom">Optional extra values sent with the event. The dictionary is copied; nested values are read when the event is sent.</param>
    /// <returns><see langword="true"/> when the event was created; otherwise <see langword="false"/>.</returns>
    public static bool TryCreate(
        string? userId,
        string? ip,
        string? method,
        DateTimeOffset timestamp,
        EncryptedPayload? payload,
        [NotNullWhen(true)] out SignalGateEvent? evt,
        IReadOnlyDictionary<string, object?>? custom = null)
    {
        evt = null;
        if (userId is null
            || ip is null
            || method is null
            || payload is null
            || payload.Encrypted is null
            || payload.Nonce is null
            || !TryCopyCustom(custom, out ReadOnlyDictionary<string, object?>? copy))
        {
            return false;
        }

        evt = new SignalGateEvent(userId, ip, method, FormatTimestamp(timestamp), payload, copy);
        return true;
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    private static bool TryCopyCustom(
        IReadOnlyDictionary<string, object?>? custom,
        out ReadOnlyDictionary<string, object?>? copy)
    {
        copy = null;
        if (custom is null)
        {
            return true;
        }

        var entries = new Dictionary<string, object?>(Math.Max(custom.Count, 0), StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> entry in custom)
        {
            if (entry.Key is null)
            {
                return false;
            }

            entries[entry.Key] = entry.Value;
        }

        copy = new ReadOnlyDictionary<string, object?>(entries);
        return true;
    }
}
