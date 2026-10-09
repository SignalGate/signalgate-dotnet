using System.Collections.Generic;
using System.Text.RegularExpressions;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Wire;

public sealed partial class UuidTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Ids_are_lowercase_version_4_uuids()
    {
        for (int i = 0; i < 1_000; i++)
        {
            Assert.Matches(UuidPattern(), Uuid.NewV4());
        }
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_thousand_ids_are_unique()
    {
        var ids = new HashSet<string>();

        for (int i = 0; i < 1_000; i++)
        {
            Assert.True(ids.Add(Uuid.NewV4()));
        }

        Assert.Equal(1_000, ids.Count);
    }

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex UuidPattern();
}
