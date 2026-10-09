using System.Text;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Wire;

public sealed class ResponseParserTests
{
    private const string SentRequestId = "11111111-2222-4333-8444-555555555555";

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Allow_verdict_is_read_field_by_field()
    {
        Assert.True(ResponseParser.TryParseCheckResult(Bytes(ResponseBodies.AllowVerdict), out CheckResult result));

        AssertResult(result, "allow", 0.0, "req_1", "acme", "2026-04-01T13:08:50Z", 812);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Verdict_inside_the_envelope_with_an_integer_score_is_read()
    {
        Assert.True(ResponseParser.TryParseCheckResult(Bytes(ResponseBodies.BlockVerdictInEnvelope), out CheckResult result));

        AssertResult(result, "block", 1.0, "req_happy_1", "t_fake", "2026-04-01T13:08:50Z", 812);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Every_verdict_field_is_read()
    {
        Assert.True(ResponseParser.TryParseCheckResult(Bytes(ResponseBodies.EveryVerdictField), out CheckResult result));

        AssertResult(result, "admin_alert", 0.25, "req_all_fields", "tenant_all_fields", "2026-07-06T12:00:00Z", 1234);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Empty_data_object_gives_every_default()
    {
        Assert.True(ResponseParser.TryParseCheckResult(Bytes(ResponseBodies.EmptyData), out CheckResult result));

        AssertResult(result, "", 0.0, "", "", "", 0);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Unknown_action_is_returned_verbatim()
    {
        Assert.True(ResponseParser.TryParseCheckResult(Bytes(ResponseBodies.UnknownAction), out CheckResult result));

        Assert.Equal("not_a_known_action", result.Action);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ResponseBodies.MalformedSuccessBodies), MemberType = typeof(ResponseBodies))]
    public void Body_without_a_data_object_is_malformed(string body)
    {
        Assert.False(ResponseParser.TryParseCheckResult(Bytes(body), out _));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"data\"")]
    [InlineData("null")]
    [InlineData("{\"data\":{}")]
    public void Unreadable_bodies_are_malformed(string body)
    {
        Assert.False(ResponseParser.TryParseCheckResult(Bytes(body), out _));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Log_acknowledgement_is_not_a_verdict()
    {
        Assert.False(ResponseParser.TryParseCheckResult(Bytes(ResponseBodies.LogAcknowledged), out _));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Invalid_utf8_and_deep_nesting_are_malformed_without_throwing()
    {
        byte[] invalidUtf8 = [0x7B, 0x22, 0xFF, 0xFE, 0x22, 0x3A, 0x31, 0x7D];
        string deep = new string('[', 200) + new string(']', 200);

        Assert.False(ResponseParser.TryParseCheckResult(invalidUtf8, out _));
        Assert.False(ResponseParser.TryParseCheckResult(Bytes(deep), out _));
        Assert.False(ResponseParser.TryParseCheckResult(null!, out _));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Body_with_a_byte_order_mark_is_read()
    {
        byte[] body = [0xEF, 0xBB, 0xBF, .. Bytes(ResponseBodies.AllowVerdict)];

        Assert.True(ResponseParser.TryParseCheckResult(body, out CheckResult result));
        Assert.Equal("allow", result.Action);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("""{"data":{"action":5,"score":"high","request_id":null,"tenant_id":[],"timestamp":{},"processing_time_us":"9"}}""", "", 0.0, 0)]
    [InlineData("""{"data":{"action":"allow","score":1e400,"processing_time_us":1e400}}""", "allow", 0.0, 0)]
    [InlineData("""{"data":{"action":"allow","score":-0.5,"processing_time_us":812.9}}""", "allow", -0.5, 812)]
    [InlineData("""{"data":{"action":"allow","score":2,"processing_time_us":-3.7}}""", "allow", 2.0, -3)]
    [InlineData("""{"data":{"action":"allow","processing_time_us":1e30}}""", "allow", 0.0, 0)]
    [InlineData("""{"data":{"action":"allow","processing_time_us":9223372036854775807}}""", "allow", 0.0, 9223372036854775807)]
    [InlineData("""{"data":{"action":"allow","processing_time_us":1e3}}""", "allow", 0.0, 1000)]
    public void Fields_of_the_wrong_type_fall_back_to_defaults(string body, string action, double score, long processingTimeUs)
    {
        Assert.True(ResponseParser.TryParseCheckResult(Bytes(body), out CheckResult result));

        Assert.Equal(action, result.Action);
        Assert.Equal(score, result.Score);
        Assert.Equal(processingTimeUs, result.ProcessingTimeUs);
        Assert.Equal("", result.RequestId);
        Assert.Equal("", result.TenantId);
        Assert.Equal("", result.Timestamp);
        Assert.False(result.FailedOpen);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void String_with_an_invalid_escape_falls_back_to_empty()
    {
        const string body = """{"data":{"action":"allow","request_id":"\uD800"}}""";

        Assert.True(ResponseParser.TryParseCheckResult(Bytes(body), out CheckResult result));

        Assert.Equal("allow", result.Action);
        Assert.Equal("", result.RequestId);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(ResponseBodies.ClientErrors), MemberType = typeof(ResponseBodies))]
    public void Error_envelope_fields_are_read(int status, string body, string code, string message, string requestId)
    {
        SignalGateServerException error = ResponseParser.ToServerException(status, Bytes(body), "req_header", SentRequestId);

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.ServerMessage);
        Assert.Equal(requestId, error.RequestId);
        Assert.Null(error.Details);
        Assert.Equal($"[{status}] {code}: {message}", error.Message);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Unauthorized_response_has_the_documented_message()
    {
        SignalGateServerException error = ResponseParser.ToServerException(401, Bytes(ResponseBodies.Unauthorized), null, SentRequestId);

        Assert.Equal("[401] UNAUTHORIZED: Invalid API key", error.Message);
        Assert.Equal("req_401", error.RequestId);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Non_json_error_body_uses_the_response_request_id()
    {
        SignalGateServerException error = ResponseParser.ToServerException(
            502,
            Bytes(ResponseBodies.HtmlBadGateway),
            ResponseBodies.HtmlBadGatewayRequestId,
            SentRequestId);

        Assert.Equal(502, error.StatusCode);
        Assert.Equal("", error.Code);
        Assert.Equal("", error.ServerMessage);
        Assert.Equal("req_hdr", error.RequestId);
        Assert.Null(error.Details);
        Assert.Equal("[502] : ", error.Message);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("""{"error":{"request_id":"req_body"}}""", "req_header", "req_body")]
    [InlineData("""{"error":{"request_id":""}}""", "req_header", "req_header")]
    [InlineData("""{"error":{"request_id":7}}""", "req_header", "req_header")]
    [InlineData("""{"error":{}}""", "", SentRequestId)]
    [InlineData("""{"error":{}}""", null, SentRequestId)]
    [InlineData("not json", null, SentRequestId)]
    public void Request_id_falls_back_from_body_to_header_to_sent_id(string body, string? headerRequestId, string expected)
    {
        SignalGateServerException error = ResponseParser.ToServerException(500, Bytes(body), headerRequestId, SentRequestId);

        Assert.Equal(expected, error.RequestId);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Details_keep_strings_and_store_other_values_as_json_text()
    {
        const string body =
            """{"error":{"code":"INVALID_PAYLOAD","message":"m","details":{"field":"user_id","count":42,"nested":{"a":[1, 2]},"flag":true,"none":null}}}""";

        SignalGateServerException error = ResponseParser.ToServerException(400, Bytes(body), null, SentRequestId);

        Assert.NotNull(error.Details);
        Assert.Equal(5, error.Details.Count);
        Assert.Equal("user_id", error.Details["field"]);
        Assert.Equal("42", error.Details["count"]);
        Assert.Equal("""{"a":[1, 2]}""", error.Details["nested"]);
        Assert.Equal("true", error.Details["flag"]);
        Assert.Equal("null", error.Details["none"]);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("""{"error":{"details":null}}""")]
    [InlineData("""{"error":{"details":"text"}}""")]
    [InlineData("""{"error":{"details":[1]}}""")]
    [InlineData("""{"error":"text"}""")]
    [InlineData("""{"error":{"code":1,"message":false}}""")]
    public void Details_are_absent_unless_an_object_and_non_string_fields_are_empty(string body)
    {
        SignalGateServerException error = ResponseParser.ToServerException(400, Bytes(body), null, SentRequestId);

        Assert.Null(error.Details);
        Assert.Equal("", error.Code);
        Assert.Equal("", error.ServerMessage);
        Assert.Equal("[400] : ", error.Message);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Empty_and_invalid_error_bodies_never_throw()
    {
        byte[] invalidUtf8 = [0x7B, 0xFF, 0x7D];

        Assert.Equal("[503] : ", ResponseParser.ToServerException(503, [], null, SentRequestId).Message);
        Assert.Equal("[503] : ", ResponseParser.ToServerException(503, invalidUtf8, null, SentRequestId).Message);
        Assert.Equal("[503] : ", ResponseParser.ToServerException(503, null!, null, SentRequestId).Message);
    }

    private static byte[] Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static void AssertResult(
        CheckResult result,
        string action,
        double score,
        string requestId,
        string tenantId,
        string timestamp,
        long processingTimeUs)
    {
        Assert.Equal(action, result.Action);
        Assert.Equal(score, result.Score);
        Assert.Equal(requestId, result.RequestId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(processingTimeUs, result.ProcessingTimeUs);
        Assert.False(result.FailedOpen);
    }
}
