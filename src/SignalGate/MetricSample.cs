using System.Collections.Generic;

namespace SignalGate;

/// <summary>
/// One counter value in a <see cref="SignalGateMetrics.Snapshot"/>.
/// </summary>
/// <param name="Name">The counter name, for example <c>check_total</c>.</param>
/// <param name="Labels">The counter labels; empty for a counter without labels.</param>
/// <param name="Value">The counter value at the time of the snapshot.</param>
public sealed record MetricSample(string Name, IReadOnlyDictionary<string, string> Labels, long Value);
