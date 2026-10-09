namespace SignalGate.Internal;

// One queued log event: the serialized body and the Idempotency-Key reused on every attempt.
internal sealed class LogJob
{
    internal LogJob(byte[] body, string idempotencyKey)
    {
        Body = body;
        IdempotencyKey = idempotencyKey;
    }

    internal byte[] Body { get; }

    internal string IdempotencyKey { get; }

    // Set once the job has been counted as sent, retry_exhausted or closed. Only the worker reads or writes it.
    internal bool Accounted { get; set; }
}
