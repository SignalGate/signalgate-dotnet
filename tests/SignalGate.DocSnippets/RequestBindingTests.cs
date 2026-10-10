using System.Text.Json;
using SignalGate.DocSnippets.Snippets;
using Xunit;

namespace SignalGate.DocSnippets;

// The README request types must bind both payloads that a frontend sends, under ASP.NET Core's JSON defaults.
public sealed class RequestBindingTests
{
    private const string Body = """
        {
          "cartId": "cart-1",
          "phone": "+15550100",
          "signalgate": { "encrypted": "first", "timestamp": 1760000000000, "nonce": "n1", "v": 2 },
          "signalgate_log": { "encrypted": "second", "timestamp": 1760000000001, "nonce": "n2", "v": 2 }
        }
        """;

    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Checkout_request_binds_both_payloads()
    {
        CheckoutRequest? body = JsonSerializer.Deserialize<CheckoutRequest>(Body, WebDefaults);

        Assert.NotNull(body);
        Assert.Equal("first", body.SignalGate?.Encrypted);
        Assert.Equal("second", body.SignalGateLog?.Encrypted);
        Assert.Equal(1760000000001, body.SignalGateLog?.Timestamp);
    }

    [Fact]
    public void Signup_request_binds_both_payloads()
    {
        SignupRequest? body = JsonSerializer.Deserialize<SignupRequest>(Body, WebDefaults);

        Assert.NotNull(body);
        Assert.Equal("first", body.SignalGate?.Encrypted);
        Assert.Equal("second", body.SignalGateLog?.Encrypted);
    }
}
