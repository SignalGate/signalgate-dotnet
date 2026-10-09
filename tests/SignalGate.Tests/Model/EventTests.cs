using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using SignalGate.Tests.Fakes;
using SignalGate.Tests.Fixtures;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class EventTests
{
    public static TheoryData<string> NullCases => new()
    {
        "user id",
        "ip",
        "method",
        "payload",
        "payload encrypted",
        "payload nonce",
        "custom key",
    };

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void String_timestamp_constructor_keeps_every_value_verbatim()
    {
        EncryptedPayload payload = StandardEvent.Payload();

        var evt = new SignalGateEvent(
            StandardEvent.UserId,
            StandardEvent.Ip,
            StandardEvent.Method,
            StandardEvent.Timestamp,
            payload,
            StandardEvent.Custom());

        Assert.Equal(StandardEvent.UserId, evt.UserId);
        Assert.Equal(StandardEvent.Ip, evt.Ip);
        Assert.Equal(StandardEvent.Method, evt.Method);
        Assert.Equal(StandardEvent.Timestamp, evt.Timestamp);
        Assert.Same(payload, evt.Payload);
        Assert.NotNull(evt.Custom);
        Assert.Equal("pro", Assert.Single(evt.Custom).Value);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Empty_strings_are_accepted_as_given()
    {
        var evt = new SignalGateEvent("", "", "", "", new EncryptedPayload("", 0, ""));

        Assert.Equal("", evt.UserId);
        Assert.Equal("", evt.Ip);
        Assert.Equal("", evt.Method);
        Assert.Equal("", evt.Timestamp);
        Assert.Null(evt.Custom);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Date_time_offset_in_utc_is_formatted_with_milliseconds_and_a_zero_offset()
    {
        var timestamp = new DateTimeOffset(2026, 4, 1, 13, 8, 50, TimeSpan.Zero);

        var evt = new SignalGateEvent(StandardEvent.UserId, StandardEvent.Ip, StandardEvent.Method, timestamp, StandardEvent.Payload());

        Assert.Equal("2026-04-01T13:08:50.000+00:00", evt.Timestamp);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Date_time_offset_keeps_its_offset_and_milliseconds()
    {
        var timestamp = new DateTimeOffset(2026, 4, 1, 16, 8, 50, 123, TimeSpan.FromHours(3));

        var evt = new SignalGateEvent(StandardEvent.UserId, StandardEvent.Ip, StandardEvent.Method, timestamp, StandardEvent.Payload());

        Assert.Equal("2026-04-01T16:08:50.123+03:00", evt.Timestamp);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void TryCreate_formats_the_timestamp_like_the_constructor()
    {
        var timestamp = new DateTimeOffset(2026, 4, 1, 13, 8, 50, TimeSpan.Zero);

        bool created = SignalGateEvent.TryCreate(
            StandardEvent.UserId,
            StandardEvent.Ip,
            StandardEvent.Method,
            timestamp,
            StandardEvent.Payload(),
            out SignalGateEvent? evt,
            StandardEvent.Custom());

        Assert.True(created);
        Assert.NotNull(evt);
        Assert.Equal("2026-04-01T13:08:50.000+00:00", evt.Timestamp);
        Assert.Equal(StandardEvent.UserId, evt.UserId);
        Assert.NotNull(evt.Custom);
        Assert.Equal("pro", evt.Custom["plan"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void String_timestamp_constructor_rejects_a_null_timestamp()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SignalGateEvent(StandardEvent.UserId, StandardEvent.Ip, StandardEvent.Method, (string)null!, StandardEvent.Payload()));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(NullCases))]
    public void String_timestamp_constructor_rejects_null_values(string nullCase)
    {
        Inputs inputs = Inputs.WithNull(nullCase);

        Assert.Throws<ArgumentNullException>(
            () => new SignalGateEvent(inputs.UserId!, inputs.Ip!, inputs.Method!, StandardEvent.Timestamp, inputs.Payload!, inputs.Custom));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(NullCases))]
    public void Date_time_offset_constructor_rejects_null_values(string nullCase)
    {
        Inputs inputs = Inputs.WithNull(nullCase);

        Assert.Throws<ArgumentNullException>(
            () => new SignalGateEvent(inputs.UserId!, inputs.Ip!, inputs.Method!, DateTimeOffset.UtcNow, inputs.Payload!, inputs.Custom));
    }

    [Theory(Timeout = HangGuard.TimeoutMs)]
    [MemberData(nameof(NullCases))]
    public void TryCreate_returns_false_without_an_event_for_null_values(string nullCase)
    {
        Inputs inputs = Inputs.WithNull(nullCase);

        bool created = SignalGateEvent.TryCreate(
            inputs.UserId,
            inputs.Ip,
            inputs.Method,
            DateTimeOffset.UtcNow,
            inputs.Payload,
            out SignalGateEvent? evt,
            inputs.Custom);

        Assert.False(created);
        Assert.Null(evt);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void TryCreate_accepts_empty_strings_and_no_custom_values()
    {
        bool created = SignalGateEvent.TryCreate("", "", "", DateTimeOffset.UtcNow, new EncryptedPayload("", 0, ""), out SignalGateEvent? evt);

        Assert.True(created);
        Assert.NotNull(evt);
        Assert.Null(evt.Custom);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Custom_values_are_copied_when_the_event_is_created()
    {
        var source = new Dictionary<string, object?> { ["plan"] = "pro", ["seats"] = 3 };

        SignalGateEvent evt = StandardEvent.WithCustom(source);
        source["plan"] = "free";
        source["added"] = true;
        source.Remove("seats");

        Assert.NotNull(evt.Custom);
        Assert.Equal(2, evt.Custom.Count);
        Assert.Equal("pro", evt.Custom["plan"]);
        Assert.Equal(3, evt.Custom["seats"]);
        Assert.False(evt.Custom.ContainsKey("added"));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void TryCreate_copies_custom_values_too()
    {
        var source = new Dictionary<string, object?> { ["plan"] = "pro" };

        Assert.True(SignalGateEvent.TryCreate("u", "ip", "m", DateTimeOffset.UtcNow, StandardEvent.Payload(), out SignalGateEvent? evt, source));
        source["plan"] = "free";

        Assert.Equal("pro", evt.Custom!["plan"]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Custom_values_keep_the_source_order_and_cannot_be_modified()
    {
        var source = new Dictionary<string, object?> { ["z"] = 1, ["a"] = 2, ["m"] = 3 };

        SignalGateEvent evt = StandardEvent.WithCustom(source);

        Assert.Equal(["z", "a", "m"], evt.Custom!.Select(pair => pair.Key));
        var asDictionary = Assert.IsAssignableFrom<IDictionary<string, object?>>(evt.Custom);
        Assert.True(asDictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asDictionary["z"] = 9);
    }

    private sealed record Inputs(string? UserId, string? Ip, string? Method, EncryptedPayload? Payload, IReadOnlyDictionary<string, object?>? Custom)
    {
        public static Inputs WithNull(string nullCase)
        {
            var valid = new Inputs(StandardEvent.UserId, StandardEvent.Ip, StandardEvent.Method, StandardEvent.Payload(), StandardEvent.Custom());
            return nullCase switch
            {
                "user id" => valid with { UserId = null },
                "ip" => valid with { Ip = null },
                "method" => valid with { Method = null },
                "payload" => valid with { Payload = null },
                "payload encrypted" => valid with { Payload = new EncryptedPayload(null, 1, "n", 2) },
                "payload nonce" => valid with { Payload = new EncryptedPayload("e", 1, null, 2) },
                "custom key" => valid with { Custom = new NullKeyDictionary() },
                _ => throw new ArgumentOutOfRangeException(nameof(nullCase), nullCase, "unknown case"),
            };
        }
    }

    private sealed class NullKeyDictionary : IReadOnlyDictionary<string, object?>
    {
        private readonly KeyValuePair<string, object?>[] _entries = [new("ok", 1), new(null!, 2)];

        public int Count => _entries.Length;

        public IEnumerable<string> Keys => _entries.Select(entry => entry.Key);

        public IEnumerable<object?> Values => _entries.Select(entry => entry.Value);

        public object? this[string key] => throw new KeyNotFoundException();

        public bool ContainsKey(string key) => false;

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
        {
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_entries).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
