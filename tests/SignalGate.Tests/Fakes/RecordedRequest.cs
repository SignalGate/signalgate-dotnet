using System;
using System.Collections.Generic;
using System.Net.Http;

namespace SignalGate.Tests.Fakes;

// A request as FakeHttpHandler received it. Header values are the raw strings that were added; header names
// are case-insensitive keys.
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Uri,
    Version Version,
    HttpVersionPolicy VersionPolicy,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> ContentHeaders,
    byte[] Body);
