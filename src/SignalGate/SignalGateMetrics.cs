using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;

namespace SignalGate;

/// <summary>
/// The client's counters. A counter exists only after its first increment. Reading is safe at any time
/// and from any thread.
/// </summary>
/// <remarks>
/// <para>The counters are:</para>
/// <list type="bullet">
/// <item><description><c>check_total</c>: a check was accepted and is about to be sent.</description></item>
/// <item><description><c>check_success_total</c>: a check returned a real verdict.</description></item>
/// <item><description><c>check_failed_open_total</c>: a check returned a synthesized <c>allow</c>.</description></item>
/// <item><description><c>check_error_total{type}</c>: a check hit a timeout, network error, non-2xx status or
/// unreadable success response, including when it then failed open. <c>type</c> is <c>TimeoutError</c>,
/// <c>NetworkError</c>, <c>ServerError</c> or <c>MalformedResponse</c>.</description></item>
/// <item><description><c>log_enqueued_total</c>: an event was accepted into the delivery queue.</description></item>
/// <item><description><c>log_sent_total</c>: a delivery attempt received a 2xx status.</description></item>
/// <item><description><c>log_http_error_total{status}</c>: a delivery attempt failed; <c>status</c> is the
/// HTTP status code or <c>network</c>. Attempts aborted by closing the client are not counted.</description></item>
/// <item><description><c>log_dropped_total{reason}</c>: an event was not delivered; <c>reason</c> is
/// <c>queue_full</c>, <c>closed</c> or <c>retry_exhausted</c>.</description></item>
/// </list>
/// </remarks>
public sealed class SignalGateMetrics
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    internal SignalGateMetrics()
    {
    }

    /// <summary>
    /// Returns the value of one counter.
    /// </summary>
    /// <param name="name">The counter name, for example <c>check_error_total</c>.</param>
    /// <param name="labels">The counter labels, matched as a set; <see langword="null"/> or empty for a counter without labels.</param>
    /// <returns>The counter value, or 0 when the counter does not exist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public long Get(string name, IReadOnlyDictionary<string, string>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        string key = FlatKey(name, labels);
        return _counters.TryGetValue(key, out Counter? counter) ? Interlocked.Read(ref counter.Value) : 0;
    }

    /// <summary>
    /// Returns every counter with its name, labels and value, sorted by flat key.
    /// </summary>
    /// <returns>The counters at the time of the call.</returns>
    public IReadOnlyList<MetricSample> Snapshot()
    {
        var keys = new List<string>(_counters.Keys);
        keys.Sort(StringComparer.Ordinal);

        var samples = new List<MetricSample>(keys.Count);
        foreach (string key in keys)
        {
            if (_counters.TryGetValue(key, out Counter? counter))
            {
                samples.Add(new MetricSample(counter.Name, counter.Labels, Interlocked.Read(ref counter.Value)));
            }
        }

        return samples.AsReadOnly();
    }

    /// <summary>
    /// Returns every counter keyed by its flat key: the name alone, or
    /// <c>name{key="value",...}</c> with labels sorted by key. Entries are sorted by flat key.
    /// </summary>
    /// <returns>The counters at the time of the call.</returns>
    public IReadOnlyDictionary<string, long> SnapshotFlat()
    {
        var flat = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Counter> entry in _counters)
        {
            flat[entry.Key] = Interlocked.Read(ref entry.Value.Value);
        }

        return new ReadOnlyDictionary<string, long>(flat);
    }

    internal void Increment(string name)
    {
        Counter counter = _counters.GetOrAdd(name, static n => new Counter(n, EmptyLabels()));
        Interlocked.Increment(ref counter.Value);
    }

    internal void Increment(string name, string labelKey, string labelValue)
    {
        string key = BuildKey(name, [new KeyValuePair<string, string>(labelKey, labelValue)]);
        if (!_counters.TryGetValue(key, out Counter? counter))
        {
            var labels = new Dictionary<string, string>(1, StringComparer.Ordinal) { [labelKey] = labelValue };
            counter = _counters.GetOrAdd(key, new Counter(name, new ReadOnlyDictionary<string, string>(labels)));
        }

        Interlocked.Increment(ref counter.Value);
    }

    private static ReadOnlyDictionary<string, string> EmptyLabels()
    {
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0, StringComparer.Ordinal));
    }

    // Returns name alone when there are no labels, otherwise name{key="value",...} with labels sorted by key
    // (ordinal) and values escaped.
    internal static string FlatKey(string name, IReadOnlyDictionary<string, string>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return name;
        }

        var sorted = new List<KeyValuePair<string, string>>(labels);
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return BuildKey(name, sorted);
    }

    private static string BuildKey(string name, List<KeyValuePair<string, string>> sortedLabels)
    {
        var builder = new StringBuilder(name);
        builder.Append('{');
        for (int i = 0; i < sortedLabels.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(sortedLabels[i].Key).Append("=\"");
            AppendEscaped(builder, sortedLabels[i].Value);
            builder.Append('"');
        }

        return builder.Append('}').ToString();
    }

    private static void AppendEscaped(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }

    private sealed class Counter
    {
        public long Value;

        public Counter(string name, IReadOnlyDictionary<string, string> labels)
        {
            Name = name;
            Labels = labels;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Labels { get; }
    }
}
