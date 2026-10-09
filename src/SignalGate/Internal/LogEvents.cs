namespace SignalGate.Internal;

// The event names passed to ISignalGateLogger.
internal static class LogEvents
{
    internal const string HttpRequest = "signalgate.http.request";
    internal const string HttpResponse = "signalgate.http.response";
    internal const string HttpTimeout = "signalgate.http.timeout";
    internal const string HttpNetworkError = "signalgate.http.network_error";

    internal const string CheckFailedOpen = "signalgate.check.failed_open";
    internal const string CheckUnknownAction = "signalgate.check.unknown_action";

    internal const string LogQueueFull = "signalgate.log.queue_full";
    internal const string LogInvalidEvent = "signalgate.log.invalid_event";
    internal const string LogDropped4xx = "signalgate.log.dropped_4xx";
    internal const string LogDroppedNetwork = "signalgate.log.dropped_network";
    internal const string LogWorkerUnhandled = "signalgate.log.worker_unhandled";
}
