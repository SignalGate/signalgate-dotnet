using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SignalGate.AotSmoke;

// One check and one log event through the public API, against InProcessHandler.
public static class SmokeRun
{
    private const string ExpectedBody =
        """{"user_id":"u_123","ip":"203.0.113.7","method":"login","timestamp":"2026-04-01T13:08:50+00:00","payload":{"encrypted":"BASE64","timestamp":1748102400000,"nonce":"aZ19bCde3fGhI4jK","v":2},"custom":{"plan":"pro","account":{"seats":5,"role":"owner"},"items":[1,2,3],"meta":{"source":"smoke","tags":["a","b"]},"big":9007199254740993}}""";

    // Returns a description of every expectation that did not hold; empty when everything worked.
    public static async Task<IReadOnlyList<string>> RunAsync()
    {
        var failures = new List<string>();
        void Expect(bool condition, string failure)
        {
            if (!condition)
            {
                failures.Add(failure);
            }
        }

        using var handler = new InProcessHandler();
        using JsonDocument meta = JsonDocument.Parse("""{"source":"smoke","tags":["a","b"]}""");
        var custom = new Dictionary<string, object?>
        {
            ["plan"] = "pro",
            ["account"] = new Dictionary<string, object?> { ["seats"] = 5, ["role"] = "owner" },
            ["items"] = new List<int> { 1, 2, 3 },
            ["meta"] = meta.RootElement,
            ["big"] = 9007199254740993L,
        };
        var evt = new SignalGateEvent(
            "u_123",
            "203.0.113.7",
            "login",
            "2026-04-01T13:08:50+00:00",
            new EncryptedPayload("BASE64", 1748102400000, "aZ19bCde3fGhI4jK", 2),
            custom);

        var options = new SignalGateClientOptions { ApiKey = "pk_test_unit_key", HttpHandler = handler };
        await using (var client = new SignalGateClient(options))
        {
            CheckResult verdict = await client.CheckAsync(evt);
            client.Log(evt);
            await client.CloseAsync(TimeSpan.FromSeconds(30));

            Expect(verdict.Action == CheckActions.Block, "the check returned the action '" + verdict.Action + "'");
            Expect(!verdict.FailedOpen, "the check failed open");
            Expect(client.Metrics.Get("check_success_total") == 1, "check_success_total is not 1");
            Expect(client.Metrics.Get("log_sent_total") == 1, "log_sent_total is not 1");
        }

        IReadOnlyList<(string Path, byte[] Body)> requests = handler.Requests;
        Expect(requests.Count == 2, "the handler received " + requests.Count.ToString(CultureInfo.InvariantCulture) + " requests");
        if (requests.Count == 2)
        {
            byte[] expected = Encoding.UTF8.GetBytes(ExpectedBody);
            Expect(requests[0].Path == "/v0/check", "the first request went to " + requests[0].Path);
            Expect(requests[1].Path == "/v0/log", "the second request went to " + requests[1].Path);
            Expect(requests[0].Body.SequenceEqual(expected), "the check body differs from the expected body");
            Expect(requests[1].Body.SequenceEqual(expected), "the log body differs from the expected body");
        }

        return failures;
    }
}
