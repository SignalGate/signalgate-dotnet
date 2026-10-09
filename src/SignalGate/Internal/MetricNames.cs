namespace SignalGate.Internal;

// Counter names, label keys and label values.
internal static class MetricNames
{
    internal const string CheckTotal = "check_total";
    internal const string CheckSuccessTotal = "check_success_total";
    internal const string CheckFailedOpenTotal = "check_failed_open_total";
    internal const string CheckErrorTotal = "check_error_total";
    internal const string LogEnqueuedTotal = "log_enqueued_total";
    internal const string LogSentTotal = "log_sent_total";
    internal const string LogHttpErrorTotal = "log_http_error_total";
    internal const string LogDroppedTotal = "log_dropped_total";

    internal const string TypeLabel = "type";
    internal const string StatusLabel = "status";
    internal const string ReasonLabel = "reason";

    internal const string TimeoutError = "TimeoutError";
    internal const string NetworkError = "NetworkError";
    internal const string ServerError = "ServerError";
    internal const string MalformedResponse = "MalformedResponse";

    internal const string QueueFull = "queue_full";
    internal const string Closed = "closed";
    internal const string RetryExhausted = "retry_exhausted";
    internal const string Network = "network";
}
