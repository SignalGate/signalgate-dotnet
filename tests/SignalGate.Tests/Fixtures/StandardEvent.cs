using System.Collections.Generic;
using SignalGate.Internal;

namespace SignalGate.Tests.Fixtures;

public static class StandardEvent
{
    public const string ApiKey = "pk_test_unit_key";
    public const string UserId = "u_123";
    public const string Ip = "203.0.113.7";
    public const string Method = "login";
    public const string Timestamp = "2026-04-01T13:08:50+00:00";
    public const string Encrypted = "BASE64";
    public const long PayloadTimestamp = 1748102400000;
    public const string Nonce = "aZ19bCde3fGhI4jK";
    public const int V = 2;

    public static EncryptedPayload Payload(int? v = V)
    {
        return new EncryptedPayload(Encrypted, PayloadTimestamp, Nonce, v);
    }

    public static Dictionary<string, object?> Custom()
    {
        return new Dictionary<string, object?> { ["plan"] = "pro" };
    }

    public static SignalGateEvent Create()
    {
        return new SignalGateEvent(UserId, Ip, Method, Timestamp, Payload(), Custom());
    }

    public static SignalGateEvent WithoutVOrCustom()
    {
        return new SignalGateEvent(UserId, Ip, Method, Timestamp, Payload(v: null));
    }

    public static SignalGateEvent WithCustom(IReadOnlyDictionary<string, object?>? custom, int? v = V)
    {
        return new SignalGateEvent(UserId, Ip, Method, Timestamp, Payload(v), custom);
    }

    public static SignalGateEvent WithBodyLength(int length)
    {
        int baseLength = WireEncoder.Encode(WithCustom(new Dictionary<string, object?> { ["pad"] = "" })).Length;
        string pad = new('a', length - baseLength);
        return WithCustom(new Dictionary<string, object?> { ["pad"] = pad });
    }
}
