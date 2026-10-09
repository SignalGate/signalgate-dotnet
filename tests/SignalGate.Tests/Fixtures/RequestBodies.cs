namespace SignalGate.Tests.Fixtures;

public static class RequestBodies
{
    public const string Full =
        """{"user_id":"u_123","ip":"203.0.113.7","method":"login","timestamp":"2026-04-01T13:08:50+00:00","payload":{"encrypted":"BASE64","timestamp":1748102400000,"nonce":"aZ19bCde3fGhI4jK","v":2},"custom":{"plan":"pro"}}""";

    public const string WithoutVOrCustom =
        """{"user_id":"u_123","ip":"203.0.113.7","method":"login","timestamp":"2026-04-01T13:08:50+00:00","payload":{"encrypted":"BASE64","timestamp":1748102400000,"nonce":"aZ19bCde3fGhI4jK"}}""";
}
