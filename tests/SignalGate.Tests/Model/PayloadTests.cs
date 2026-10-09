using System.Text.Json;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class PayloadTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Constructor_keeps_every_value()
    {
        var payload = new EncryptedPayload("BASE64", 1748102400000, "aZ19bCde3fGhI4jK", 2);

        Assert.Equal("BASE64", payload.Encrypted);
        Assert.Equal(1748102400000, payload.Timestamp);
        Assert.Equal("aZ19bCde3fGhI4jK", payload.Nonce);
        Assert.Equal(2, payload.V);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Constructor_accepts_null_values_without_throwing()
    {
        var payload = new EncryptedPayload(null, 0, null);

        Assert.Null(payload.Encrypted);
        Assert.Equal(0, payload.Timestamp);
        Assert.Null(payload.Nonce);
        Assert.Null(payload.V);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Binds_from_a_json_request_body_including_the_optional_integer()
    {
        const string json = """{"encrypted":"BASE64","timestamp":1748102400000,"nonce":"aZ19bCde3fGhI4jK","v":2}""";

        EncryptedPayload? payload = JsonSerializer.Deserialize<EncryptedPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("BASE64", payload.Encrypted);
        Assert.Equal(1748102400000, payload.Timestamp);
        Assert.Equal("aZ19bCde3fGhI4jK", payload.Nonce);
        Assert.Equal(2, payload.V);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Binds_without_the_optional_integer()
    {
        const string json = """{"encrypted":"BASE64","timestamp":1748102400000,"nonce":"aZ19bCde3fGhI4jK"}""";

        EncryptedPayload? payload = JsonSerializer.Deserialize<EncryptedPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("BASE64", payload.Encrypted);
        Assert.Null(payload.V);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Binds_a_body_with_missing_members_without_throwing()
    {
        EncryptedPayload? payload = JsonSerializer.Deserialize<EncryptedPayload>("{}");

        Assert.NotNull(payload);
        Assert.Null(payload.Encrypted);
        Assert.Equal(0, payload.Timestamp);
        Assert.Null(payload.Nonce);
        Assert.Null(payload.V);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Binds_a_body_with_null_members_without_throwing()
    {
        const string json = """{"encrypted":null,"timestamp":5,"nonce":null,"v":null}""";

        EncryptedPayload? payload = JsonSerializer.Deserialize<EncryptedPayload>(json);

        Assert.NotNull(payload);
        Assert.Null(payload.Encrypted);
        Assert.Equal(5, payload.Timestamp);
        Assert.Null(payload.Nonce);
        Assert.Null(payload.V);
    }

#if NET9_0_OR_GREATER
    private static readonly JsonSerializerOptions StrictNullability = new() { RespectNullableAnnotations = true };

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Binds_null_members_when_nullable_annotations_are_enforced()
    {
        const string json = """{"encrypted":null,"timestamp":5,"nonce":null}""";

        EncryptedPayload? payload = JsonSerializer.Deserialize<EncryptedPayload>(json, StrictNullability);

        Assert.NotNull(payload);
        Assert.Null(payload.Encrypted);
        Assert.Null(payload.Nonce);
    }
#endif

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Serializes_with_the_documented_member_names()
    {
        var payload = new EncryptedPayload("BASE64", 1748102400000, "aZ19bCde3fGhI4jK", 2);

        string json = JsonSerializer.Serialize(payload);

        Assert.Equal("""{"encrypted":"BASE64","timestamp":1748102400000,"nonce":"aZ19bCde3fGhI4jK","v":2}""", json);
    }
}
