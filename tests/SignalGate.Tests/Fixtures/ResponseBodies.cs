using Xunit;

namespace SignalGate.Tests.Fixtures;

public static class ResponseBodies
{
    public const string AllowVerdict =
        """{"data":{"action":"allow","score":0.0,"request_id":"req_1","tenant_id":"acme","timestamp":"2026-04-01T13:08:50Z","processing_time_us":812}}""";

    public const string BlockVerdictInEnvelope =
        """{"ok":true,"data":{"request_id":"req_happy_1","action":"block","score":1,"tenant_id":"t_fake","processing_time_us":812,"timestamp":"2026-04-01T13:08:50Z"},"error":null}""";

    public const string EveryVerdictField =
        """{"ok":true,"data":{"request_id":"req_all_fields","action":"admin_alert","score":0.25,"tenant_id":"tenant_all_fields","processing_time_us":1234,"timestamp":"2026-07-06T12:00:00Z"},"error":null}""";

    public const string EmptyData =
        """{"ok":true,"data":{},"error":null}""";

    public const string UnknownAction =
        """{"ok":true,"data":{"request_id":"req_unknown_action","action":"not_a_known_action","score":0.3,"tenant_id":"t_fake","processing_time_us":5,"timestamp":"2026-04-01T13:08:50Z"},"error":null}""";

    public const string LogAcknowledged =
        """{"ok":true,"data":null,"error":null}""";

    public const string Unauthorized =
        """{"ok":false,"data":null,"error":{"code":"UNAUTHORIZED","message":"Invalid API key","request_id":"req_401"}}""";

    public const string BadRequest =
        """{"ok":false,"data":null,"error":{"code":"BAD_REQUEST","message":"Invalid request format","request_id":"req_400a"}}""";

    public const string InvalidPayload =
        """{"ok":false,"data":null,"error":{"code":"INVALID_PAYLOAD","message":"Invalid request payload","request_id":"req_400b"}}""";

    public const string RequestRejected =
        """{"ok":false,"data":null,"error":{"code":"REQUEST_REJECTED","message":"Request rejected","request_id":"req_422"}}""";

    public const string HtmlBadGateway = "<html>bad gateway</html>";

    public const string HtmlBadGatewayRequestId = "req_hdr";

    private static readonly (int Status, string Body, string Code, string Message, string RequestId)[] ClientErrorCases =
    [
        (401, Unauthorized, "UNAUTHORIZED", "Invalid API key", "req_401"),
        (400, BadRequest, "BAD_REQUEST", "Invalid request format", "req_400a"),
        (400, InvalidPayload, "INVALID_PAYLOAD", "Invalid request payload", "req_400b"),
        (422, RequestRejected, "REQUEST_REJECTED", "Request rejected", "req_422"),
    ];

    public static TheoryData<int, string, string, string, string> ClientErrors
    {
        get
        {
            var data = new TheoryData<int, string, string, string, string>();
            foreach ((int status, string body, string code, string message, string requestId) in ClientErrorCases)
            {
                data.Add(status, body, code, message, requestId);
            }

            return data;
        }
    }

    // The status, body and code of each client error, for tests that see only those.
    public static TheoryData<int, string, string> ClientErrorCodes
    {
        get
        {
            var data = new TheoryData<int, string, string>();
            foreach ((int status, string body, string code, _, _) in ClientErrorCases)
            {
                data.Add(status, body, code);
            }

            return data;
        }
    }

    public static TheoryData<string> MalformedSuccessBodies => new()
    {
        """{"ok":true}""",
        """{"ok":true,"data":null}""",
        """{"ok":true,"data":"x"}""",
        """{"ok":true,"data":[1,2]}""",
        "not json",
    };
}
