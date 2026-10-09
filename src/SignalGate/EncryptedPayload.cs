using System.Text.Json.Serialization;

namespace SignalGate;

/// <summary>
/// The opaque object produced by the SignalGate browser snippet; forward it unchanged.
/// </summary>
/// <remarks>
/// The type binds directly from a JSON request body. Its constructor never throws, so a request body
/// with missing or null members still binds; such a payload is rejected later by
/// <see cref="SignalGateEvent"/> and <see cref="SignalGateEvent.TryCreate"/>.
/// </remarks>
public sealed class EncryptedPayload
{
    /// <summary>
    /// Creates a payload from the values produced by the SignalGate browser snippet.
    /// </summary>
    /// <param name="encrypted">Part of the opaque object produced by the SignalGate browser snippet; forward it unchanged.</param>
    /// <param name="timestamp">Part of the opaque object produced by the SignalGate browser snippet; forward it unchanged.</param>
    /// <param name="nonce">Part of the opaque object produced by the SignalGate browser snippet; forward it unchanged.</param>
    /// <param name="v">An optional integer produced by the browser snippet; forward it unchanged; omit it when absent.</param>
    [JsonConstructor]
    public EncryptedPayload(string? encrypted, long timestamp, string? nonce, int? v = null)
    {
        Encrypted = encrypted;
        Timestamp = timestamp;
        Nonce = nonce;
        V = v;
    }

    /// <summary>
    /// Part of the opaque object produced by the SignalGate browser snippet; forward it unchanged.
    /// </summary>
    [JsonPropertyName("encrypted")]
    public string? Encrypted { get; }

    /// <summary>
    /// Part of the opaque object produced by the SignalGate browser snippet; forward it unchanged.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; }

    /// <summary>
    /// Part of the opaque object produced by the SignalGate browser snippet; forward it unchanged.
    /// </summary>
    [JsonPropertyName("nonce")]
    public string? Nonce { get; }

    /// <summary>
    /// An optional integer produced by the browser snippet; forward it unchanged; omit it when absent.
    /// </summary>
    [JsonPropertyName("v")]
    public int? V { get; }
}
