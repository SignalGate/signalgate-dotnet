using System.Text.Json.Serialization;

namespace SignalGate.DocSnippets.Snippets;

// Stand-ins for application types that the README examples refer to.
public sealed record AppUser(string Id);

public sealed record SignupRequest(
    string Phone,
    EncryptedPayload? SignalGate,
    [property: JsonPropertyName("signalgate_log")] EncryptedPayload? SignalGateLog);
