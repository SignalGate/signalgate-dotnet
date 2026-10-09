using System;

namespace SignalGate;

/// <summary>
/// The verdict returned by <c>CheckAsync</c>. Gate on <see cref="Action"/> equal to
/// <see cref="CheckActions.Block"/>, let every other value through, and never gate on <see cref="Score"/>.
/// </summary>
public sealed class CheckResult
{
    /// <summary>
    /// Creates a verdict. The constructor is public so that you can unit-test code that gates on a verdict.
    /// </summary>
    /// <param name="action">The verdict action, for example <c>allow</c> or <c>block</c>.</param>
    /// <param name="score">The verdict score.</param>
    /// <param name="requestId">The request id assigned by SignalGate.</param>
    /// <param name="tenantId">The tenant id reported by SignalGate.</param>
    /// <param name="timestamp">When SignalGate produced the verdict.</param>
    /// <param name="processingTimeUs">The server processing time in microseconds.</param>
    /// <param name="failedOpen"><see langword="true"/> when the verdict was synthesized because the check failed open.</param>
    /// <exception cref="ArgumentNullException">A string argument is null.</exception>
    public CheckResult(
        string action,
        double score,
        string requestId,
        string tenantId,
        string timestamp,
        long processingTimeUs,
        bool failedOpen)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(timestamp);

        Action = action;
        Score = score;
        RequestId = requestId;
        TenantId = tenantId;
        Timestamp = timestamp;
        ProcessingTimeUs = processingTimeUs;
        FailedOpen = failedOpen;
    }

    /// <summary>
    /// The verdict action: one of the <see cref="CheckActions"/> values, or another value returned verbatim.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// The verdict score: allow 0.0, admin_alert 0.25, dry_run_block 0.5, block 1.0. Never gate on it.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// The request id assigned by SignalGate, or an empty string when the check failed open.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// The tenant id reported by SignalGate, or an empty string when the check failed open.
    /// </summary>
    public string TenantId { get; }

    /// <summary>
    /// When SignalGate produced the verdict, or an empty string when the check failed open.
    /// </summary>
    public string Timestamp { get; }

    /// <summary>
    /// The server processing time in microseconds, or 0 when the check failed open.
    /// </summary>
    public long ProcessingTimeUs { get; }

    /// <summary>
    /// <see langword="true"/> when no verdict was received and this <c>allow</c> result was synthesized
    /// because the check failed open.
    /// </summary>
    public bool FailedOpen { get; }
}
