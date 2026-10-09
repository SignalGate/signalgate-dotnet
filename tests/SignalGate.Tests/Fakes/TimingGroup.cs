using Xunit;

namespace SignalGate.Tests.Fakes;

// Tests that depend on real elapsed time run in this collection, one at a time.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingGroup
{
    public const string Name = "Timing-sensitive";
}
