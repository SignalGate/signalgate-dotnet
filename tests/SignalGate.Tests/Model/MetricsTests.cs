using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class MetricsTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_new_instance_has_no_counters()
    {
        var metrics = new SignalGateMetrics();

        Assert.Empty(metrics.Snapshot());
        Assert.Empty(metrics.SnapshotFlat());
        Assert.Equal(0, metrics.Get(MetricNames.CheckTotal));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void A_counter_appears_only_after_its_first_increment()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.CheckTotal);

        Assert.Equal(["check_total"], metrics.SnapshotFlat().Keys);
        Assert.Equal(1, metrics.Get(MetricNames.CheckTotal));
        Assert.Equal(0, metrics.Get(MetricNames.CheckSuccessTotal));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Labelled_counters_use_the_flat_key_format()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.CheckErrorTotal, MetricNames.TypeLabel, MetricNames.TimeoutError);
        metrics.Increment(MetricNames.LogHttpErrorTotal, MetricNames.StatusLabel, "503");
        metrics.Increment(MetricNames.LogHttpErrorTotal, MetricNames.StatusLabel, "503");
        metrics.Increment(MetricNames.LogDroppedTotal, MetricNames.ReasonLabel, MetricNames.QueueFull);
        metrics.Increment(MetricNames.LogEnqueuedTotal);

        var expected = new Dictionary<string, long>
        {
            ["check_error_total{type=\"TimeoutError\"}"] = 1,
            ["log_dropped_total{reason=\"queue_full\"}"] = 1,
            ["log_enqueued_total"] = 1,
            ["log_http_error_total{status=\"503\"}"] = 2,
        };
        Assert.Equal(expected, metrics.SnapshotFlat());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Get_matches_labels_and_returns_zero_for_other_labels()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.LogHttpErrorTotal, MetricNames.StatusLabel, "503");

        Assert.Equal(1, metrics.Get("log_http_error_total", new Dictionary<string, string> { ["status"] = "503" }));
        Assert.Equal(0, metrics.Get("log_http_error_total", new Dictionary<string, string> { ["status"] = "500" }));
        Assert.Equal(0, metrics.Get("log_http_error_total"));
        Assert.Equal(0, metrics.Get("log_http_error_total", new Dictionary<string, string>()));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Get_without_labels_equals_get_with_empty_labels()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.LogSentTotal);

        Assert.Equal(1, metrics.Get("log_sent_total"));
        Assert.Equal(1, metrics.Get("log_sent_total", new Dictionary<string, string>()));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Get_matches_labels_as_a_set_whatever_the_dictionary_order()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.CheckErrorTotal, MetricNames.TypeLabel, MetricNames.ServerError);

        var plain = new Dictionary<string, string> { ["type"] = "ServerError" };
        var reversed = new SortedDictionary<string, string>(new ReverseOrdinal()) { ["type"] = "ServerError" };
        var extra = new Dictionary<string, string> { ["type"] = "ServerError", ["a"] = "x" };

        Assert.Equal(1, metrics.Get("check_error_total", plain));
        Assert.Equal(1, metrics.Get("check_error_total", reversed));
        Assert.Equal(0, metrics.Get("check_error_total", extra));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Several_labels_produce_the_same_key_in_any_order()
    {
        var forward = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["alpha"] = "1", ["beta"] = "2" };
        var backward = new SortedDictionary<string, string>(new ReverseOrdinal()) { ["alpha"] = "1", ["beta"] = "2" };

        Assert.Equal(SignalGateMetrics.FlatKey("m", forward), SignalGateMetrics.FlatKey("m", backward));
        Assert.Equal("m{alpha=\"1\",beta=\"2\"}", SignalGateMetrics.FlatKey("m", backward));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Label_values_are_escaped()
    {
        var labels = new Dictionary<string, string> { ["k"] = "a\\b\"c\nd" };

        Assert.Equal("m{k=\"a\\\\b\\\"c\\nd\"}", SignalGateMetrics.FlatKey("m", labels));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Snapshot_lists_names_labels_and_values_sorted_by_flat_key()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.LogSentTotal);
        metrics.Increment(MetricNames.CheckTotal);
        metrics.Increment(MetricNames.CheckErrorTotal, MetricNames.TypeLabel, MetricNames.NetworkError);

        IReadOnlyList<MetricSample> samples = metrics.Snapshot();

        Assert.Equal(["check_error_total", "check_total", "log_sent_total"], samples.Select(sample => sample.Name));
        Assert.Equal("NetworkError", samples[0].Labels["type"]);
        Assert.Empty(samples[1].Labels);
        Assert.All(samples, sample => Assert.Equal(1, sample.Value));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Flat_snapshot_is_sorted_by_key()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.LogSentTotal);
        metrics.Increment(MetricNames.CheckTotal);
        metrics.Increment(MetricNames.LogDroppedTotal, MetricNames.ReasonLabel, MetricNames.RetryExhausted);
        metrics.Increment(MetricNames.LogDroppedTotal, MetricNames.ReasonLabel, MetricNames.Closed);

        Assert.Equal(
            ["check_total", "log_dropped_total{reason=\"closed\"}", "log_dropped_total{reason=\"retry_exhausted\"}", "log_sent_total"],
            metrics.SnapshotFlat().Keys);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Snapshots_do_not_change_after_later_increments()
    {
        var metrics = new SignalGateMetrics();
        metrics.Increment(MetricNames.CheckTotal);

        IReadOnlyDictionary<string, long> flat = metrics.SnapshotFlat();
        IReadOnlyList<MetricSample> samples = metrics.Snapshot();
        metrics.Increment(MetricNames.CheckTotal);

        Assert.Equal(1, flat["check_total"]);
        Assert.Equal(1, samples[0].Value);
        Assert.Equal(2, metrics.Get(MetricNames.CheckTotal));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Concurrent_increments_are_all_counted()
    {
        var metrics = new SignalGateMetrics();

        Parallel.For(0, 10_000, i =>
        {
            metrics.Increment(MetricNames.LogEnqueuedTotal);
            metrics.Increment(MetricNames.LogHttpErrorTotal, MetricNames.StatusLabel, (i % 2 == 0) ? "500" : "503");
        });

        Assert.Equal(10_000, metrics.Get(MetricNames.LogEnqueuedTotal));
        Assert.Equal(5_000, metrics.Get(MetricNames.LogHttpErrorTotal, new Dictionary<string, string> { ["status"] = "500" }));
        Assert.Equal(5_000, metrics.Get(MetricNames.LogHttpErrorTotal, new Dictionary<string, string> { ["status"] = "503" }));
    }

    private sealed class ReverseOrdinal : IComparer<string>
    {
        public int Compare(string? x, string? y) => string.CompareOrdinal(y, x);
    }
}
