using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Wire;

public sealed class WireEncoderTests
{
    private static readonly string[] TopLevelKeys = ["user_id", "ip", "method", "timestamp", "payload"];
    private static readonly string[] TopLevelKeysWithCustom = ["user_id", "ip", "method", "timestamp", "payload", "custom"];
    private static readonly string[] PayloadKeys = ["encrypted", "timestamp", "nonce"];
    private static readonly string[] PayloadKeysWithV = ["encrypted", "timestamp", "nonce", "v"];

    public static TheoryData<string> UnsupportedValues => new()
    {
        "guid",
        "enum",
        "date time offset",
        "date time",
        "char",
        "byte array",
        "double nan",
        "double positive infinity",
        "double negative infinity",
        "float nan",
        "undefined json element",
        "dictionary with integer keys",
        "hashtable with integer keys",
        "list of string pairs",
        "object",
        "self-containing dictionary",
        "self-containing list",
        "nesting deeper than the limit",
    };

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Standard_event_encodes_to_the_expected_bytes()
    {
        byte[] body = WireEncoder.Encode(StandardEvent.Create());

        Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.Full), body);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Event_without_v_or_custom_encodes_to_the_expected_bytes()
    {
        byte[] body = WireEncoder.Encode(StandardEvent.WithoutVOrCustom());

        Assert.Equal(Encoding.UTF8.GetBytes(RequestBodies.WithoutVOrCustom), body);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Output_has_no_byte_order_mark_and_no_trailing_newline()
    {
        byte[] body = WireEncoder.Encode(StandardEvent.Create());

        Assert.Equal((byte)'{', body[0]);
        Assert.Equal((byte)'}', body[^1]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Empty_custom_is_written_as_an_empty_object()
    {
        byte[] body = WireEncoder.Encode(StandardEvent.WithCustom(new Dictionary<string, object?>()));

        Assert.EndsWith(""","custom":{}}""", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Large_64_bit_integers_are_written_exactly()
    {
        byte[] body = WireEncoder.Encode(StandardEvent.WithCustom(new Dictionary<string, object?> { ["big"] = 9007199254740993L }));

        Assert.Contains("\"big\":9007199254740993", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Body_keys_are_exactly_the_documented_keys(bool withCustom, bool withV)
    {
        SignalGateEvent evt = StandardEvent.WithCustom(withCustom ? StandardEvent.Custom() : null, withV ? 2 : null);

        using JsonDocument document = JsonDocument.Parse(WireEncoder.Encode(evt));
        JsonElement root = document.RootElement;

        Assert.Equal(withCustom ? TopLevelKeysWithCustom : TopLevelKeys, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(withV ? PayloadKeysWithV : PayloadKeys, root.GetProperty("payload").EnumerateObject().Select(p => p.Name));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Plus_slash_and_cyrillic_letters_are_not_escaped()
    {
        SignalGateEvent evt = new(
            "userж",
            StandardEvent.Ip,
            StandardEvent.Method,
            StandardEvent.Timestamp,
            new EncryptedPayload("ab+cd/ef==", 1, "n+/"),
            new Dictionary<string, object?> { ["note"] = "a+b/c ж" });

        string text = Encoding.UTF8.GetString(WireEncoder.Encode(evt));

        Assert.Contains("\"user_id\":\"userж\"", text, StringComparison.Ordinal);
        Assert.Contains("\"encrypted\":\"ab+cd/ef==\"", text, StringComparison.Ordinal);
        Assert.Contains("\"nonce\":\"n+/\"", text, StringComparison.Ordinal);
        Assert.Contains("\"note\":\"a+b/c ж\"", text, StringComparison.Ordinal);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Emoji_round_trips_through_a_json_parser()
    {
        const string emoji = "smile \U0001F600";
        byte[] body = WireEncoder.Encode(StandardEvent.WithCustom(new Dictionary<string, object?> { ["text"] = emoji }));

        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal(emoji, document.RootElement.GetProperty("custom").GetProperty("text").GetString());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Unpaired_surrogates_become_replacement_characters()
    {
        var evt = new SignalGateEvent(
            "u\uD800",
            StandardEvent.Ip,
            StandardEvent.Method,
            StandardEvent.Timestamp,
            StandardEvent.Payload(),
            new Dictionary<string, object?> { ["k\uDC00"] = "v\uD83Dx", ["pair"] = "\U0001F600" });

        using JsonDocument document = JsonDocument.Parse(WireEncoder.Encode(evt));
        JsonElement root = document.RootElement;
        JsonElement custom = root.GetProperty("custom");

        Assert.Equal("u\uFFFD", root.GetProperty("user_id").GetString());
        Assert.Equal("v\uFFFDx", custom.GetProperty("k\uFFFD").GetString());
        Assert.Equal("\U0001F600", custom.GetProperty("pair").GetString());
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData("no surrogates")]
    [InlineData("lone high surrogate")]
    [InlineData("low before high")]
    [InlineData("valid pair")]
    [InlineData("pair then lone high")]
    [InlineData("lone low then pair")]
    public void Sanitize_replaces_only_unpaired_surrogates(string sanitizeCase)
    {
        (string input, string expected) = sanitizeCase switch
        {
            "no surrogates" => ("plain", "plain"),
            "lone high surrogate" => ("\uD800", "\uFFFD"),
            "low before high" => ("\uDC00\uD800", "\uFFFD\uFFFD"),
            "valid pair" => ("a\uD83D\uDE00b", "a\uD83D\uDE00b"),
            "pair then lone high" => ("\uD83D\uDE00\uD83D", "\uD83D\uDE00\uFFFD"),
            "lone low then pair" => ("\uDE00\uD83D\uDE00", "\uFFFD\uD83D\uDE00"),
            _ => throw new ArgumentOutOfRangeException(nameof(sanitizeCase), sanitizeCase, "unknown case"),
        };

        Assert.Equal(expected, WireEncoder.Sanitize(input));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Nested_dictionaries_of_any_value_type_are_written_as_objects()
    {
        var custom = new Dictionary<string, object?>
        {
            ["strings"] = new Dictionary<string, string> { ["a"] = "b" },
            ["numbers"] = new Dictionary<string, int> { ["n"] = 1 },
            ["sorted"] = new SortedDictionary<string, bool> { ["t"] = true },
            ["immutable"] = ImmutableDictionary<string, long>.Empty.Add("l", 5),
            ["readonly"] = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["x"] = null },
            ["pairs"] = new List<KeyValuePair<string, object?>> { new("p", "q") },
        };

        string text = Encode(custom);

        Assert.Equal(
            """{"strings":{"a":"b"},"numbers":{"n":1},"sorted":{"t":true},"immutable":{"l":5},"readonly":{"x":null},"pairs":{"p":"q"}}""",
            CustomJson(text));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Sequences_are_written_as_arrays()
    {
        var custom = new Dictionary<string, object?>
        {
            ["ints"] = new List<int> { 1, 2, 3 },
            ["mixed"] = new object?[] { "a", 1, null, true },
            ["set"] = new SortedSet<string>(StringComparer.Ordinal) { "x", "y" },
            ["nested"] = new List<object> { new List<int> { 1 }, new Dictionary<string, object?> { ["k"] = "v" } },
            ["empty"] = Array.Empty<int>(),
        };

        string text = Encode(custom);

        Assert.Equal(
            """{"ints":[1,2,3],"mixed":["a",1,null,true],"set":["x","y"],"nested":[[1],{"k":"v"}],"empty":[]}""",
            CustomJson(text));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Json_elements_and_nodes_are_written_verbatim()
    {
        using JsonDocument document = JsonDocument.Parse("""{"a":[1,true,null,"s"],"b":{"c":1.5}}""");
        var custom = new Dictionary<string, object?>
        {
            ["element"] = document.RootElement,
            ["node"] = JsonNode.Parse("""{"x":[1,2],"y":"z"}"""),
            ["value"] = JsonValue.Create(7),
        };

        string text = Encode(custom);

        Assert.Equal(
            """{"element":{"a":[1,true,null,"s"],"b":{"c":1.5}},"node":{"x":[1,2],"y":"z"},"value":7}""",
            CustomJson(text));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Scalars_of_every_supported_type_are_written_exactly()
    {
        var custom = new Dictionary<string, object?>
        {
            ["null"] = null,
            ["string"] = "s",
            ["true"] = true,
            ["false"] = false,
            ["sbyte"] = sbyte.MinValue,
            ["byte"] = byte.MaxValue,
            ["short"] = short.MinValue,
            ["ushort"] = ushort.MaxValue,
            ["int"] = int.MinValue,
            ["uint"] = uint.MaxValue,
            ["long"] = long.MinValue,
            ["ulong"] = ulong.MaxValue,
            ["float"] = 1.5f,
            ["double"] = 0.1,
            ["decimal"] = 79228162514264337593543950335m,
            ["decimal scale"] = 1.10m,
        };

        string text = Encode(custom);

        Assert.Equal(
            """{"null":null,"string":"s","true":true,"false":false,"sbyte":-128,"byte":255,"short":-32768,"ushort":65535,"int":-2147483648,"uint":4294967295,"long":-9223372036854775808,"ulong":18446744073709551615,"float":1.5,"double":0.1,"decimal":79228162514264337593543950335,"decimal scale":1.10}""",
            CustomJson(text));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(UnsupportedValues))]
    public void Unsupported_custom_values_are_rejected(string valueCase)
    {
        object? value = UnsupportedValue(valueCase);

        WireEncodingException error = Assert.Throws<WireEncodingException>(
            () => WireEncoder.Encode(StandardEvent.WithCustom(new Dictionary<string, object?> { ["value"] = value })));

        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Unsupported_values_nested_in_sequences_are_rejected()
    {
        var custom = new Dictionary<string, object?> { ["list"] = new List<object?> { 1, Guid.NewGuid() } };

        Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(StandardEvent.WithCustom(custom)));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task An_endless_sequence_is_rejected_without_running_forever()
    {
        var produced = new StrongBox<long>();
        var custom = new Dictionary<string, object?> { ["endless"] = Endless(produced) };

        WireEncodingException error = await Task.Run(
            () => Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(StandardEvent.WithCustom(custom))),
            TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(error.Message));
        // Every item adds at least one byte, so the size limit stops the sequence within that many items.
        Assert.InRange(produced.Value, 1, WireEncoder.MaxBodyBytes);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public async Task An_endless_sequence_of_pairs_is_rejected_without_running_forever()
    {
        var produced = new StrongBox<long>();
        var custom = new Dictionary<string, object?> { ["endless"] = EndlessPairs(produced) };

        await Task.Run(
            () => Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(StandardEvent.WithCustom(custom))),
            TestContext.Current.CancellationToken);

        Assert.InRange(produced.Value, 1, WireEncoder.MaxBodyBytes);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_body_at_the_size_limit_is_accepted()
    {
        SignalGateEvent evt = StandardEvent.WithBodyLength(WireEncoder.MaxBodyBytes);

        byte[] body = WireEncoder.Encode(evt);

        Assert.Equal(1_048_576, body.Length);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_body_one_byte_over_the_size_limit_is_rejected()
    {
        SignalGateEvent evt = StandardEvent.WithBodyLength(WireEncoder.MaxBodyBytes + 1);

        WireEncodingException error = Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(evt));

        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_large_required_field_is_rejected()
    {
        var evt = new SignalGateEvent(new string('u', WireEncoder.MaxBodyBytes), StandardEvent.Ip, StandardEvent.Method, StandardEvent.Timestamp, StandardEvent.Payload());

        Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(evt));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Moderate_nesting_is_accepted()
    {
        object nested = "leaf";
        for (int i = 0; i < 40; i++)
        {
            nested = new Dictionary<string, object?> { ["n"] = nested };
        }

        byte[] body = WireEncoder.Encode(StandardEvent.WithCustom(new Dictionary<string, object?> { ["deep"] = nested }));

        using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 128 });
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("custom").GetProperty("deep").ValueKind);
    }

    // Counting custom itself as the first level.
    [Theory(Timeout = HangGuard.TimeoutMs)]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void Custom_may_be_nested_at_most_63_levels_deep(int levels, bool accepted)
    {
        object? value = "leaf";
        for (int i = 1; i < levels; i++)
        {
            value = new Dictionary<string, object?> { ["n"] = value };
        }

        SignalGateEvent evt = StandardEvent.WithCustom(new Dictionary<string, object?> { ["n"] = value });

        if (accepted)
        {
            Assert.NotEmpty(WireEncoder.Encode(evt));
        }
        else
        {
            Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(evt));
        }
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_null_event_is_rejected_with_the_encoding_exception()
    {
        WireEncodingException error = Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(null!));

        Assert.Equal("event is null", error.Message);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Failures_inside_a_custom_sequence_are_wrapped_with_their_cause()
    {
        var custom = new Dictionary<string, object?> { ["broken"] = Broken() };

        WireEncodingException error = Assert.Throws<WireEncodingException>(() => WireEncoder.Encode(StandardEvent.WithCustom(custom)));

        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Encoding_twice_gives_identical_bytes()
    {
        SignalGateEvent evt = StandardEvent.Create();

        Assert.Equal(WireEncoder.Encode(evt), WireEncoder.Encode(evt));
    }

    private static string Encode(Dictionary<string, object?> custom)
    {
        return Encoding.UTF8.GetString(WireEncoder.Encode(StandardEvent.WithCustom(custom)));
    }

    private static string CustomJson(string body)
    {
        const string marker = "\"custom\":";
        int start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return body[start..^1];
    }

    private static object? UnsupportedValue(string valueCase)
    {
        switch (valueCase)
        {
            case "guid":
                return Guid.NewGuid();
            case "enum":
                return DayOfWeek.Monday;
            case "date time offset":
                return DateTimeOffset.UnixEpoch;
            case "date time":
                return DateTime.UnixEpoch;
            case "char":
                return 'c';
            case "byte array":
                return new byte[] { 1, 2, 3 };
            case "double nan":
                return double.NaN;
            case "double positive infinity":
                return double.PositiveInfinity;
            case "double negative infinity":
                return double.NegativeInfinity;
            case "float nan":
                return float.NaN;
            case "undefined json element":
                return default(JsonElement);
            case "dictionary with integer keys":
                return new Dictionary<int, string> { [1] = "one" };
            case "hashtable with integer keys":
                return new Hashtable { [1] = "one" };
            case "list of string pairs":
                return new List<KeyValuePair<string, string>> { new("k", "v") };
            case "object":
                return new object();
            case "self-containing dictionary":
                var dictionary = new Dictionary<string, object?>();
                dictionary["self"] = dictionary;
                return dictionary;
            case "self-containing list":
                var list = new List<object?>();
                list.Add(list);
                return list;
            case "nesting deeper than the limit":
                object nested = "leaf";
                for (int i = 0; i < 70; i++)
                {
                    nested = new Dictionary<string, object?> { ["n"] = nested };
                }

                return nested;
            default:
                throw new ArgumentOutOfRangeException(nameof(valueCase), valueCase, "unknown case");
        }
    }

    private static IEnumerable<int> Endless(StrongBox<long> produced)
    {
        while (true)
        {
            produced.Value++;
            yield return 1;
        }
    }

    private static IEnumerable<KeyValuePair<string, object?>> EndlessPairs(StrongBox<long> produced)
    {
        int i = 0;
        while (true)
        {
            produced.Value++;
            yield return new KeyValuePair<string, object?>("k", i++);
        }
    }

    private static IEnumerable<int> Broken()
    {
        yield return 1;
        throw new InvalidOperationException("broken sequence");
    }
}
