using System;
using System.Collections.Generic;

namespace SignalGate.DocSnippets.Snippets;

internal static class MetricsSnippets
{
    internal static void Print(SignalGateClient client)
    {
        #region readme:metrics
        long failedOpen = client.Metrics.Get("check_failed_open_total");
        long timeouts = client.Metrics.Get("check_error_total",
            new Dictionary<string, string> { ["type"] = "TimeoutError" });
        Console.WriteLine($"failed open: {failedOpen}, timeouts: {timeouts}");

        // For example: check_error_total{type="TimeoutError"}=1
        foreach (KeyValuePair<string, long> counter in client.Metrics.SnapshotFlat())
        {
            Console.WriteLine($"{counter.Key}={counter.Value}");
        }
        #endregion
    }
}
