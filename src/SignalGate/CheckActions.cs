namespace SignalGate;

/// <summary>
/// The known values of <see cref="CheckResult.Action"/>. Gate on <see cref="Block"/> only and let every
/// other value through.
/// </summary>
public static class CheckActions
{
    /// <summary>
    /// The <c>allow</c> verdict; let the action through. Public score 0.0.
    /// </summary>
    public const string Allow = "allow";

    /// <summary>
    /// The <c>block</c> verdict; block the action. Public score 1.0.
    /// </summary>
    public const string Block = "block";

    /// <summary>
    /// The <c>dry_run_block</c> verdict; let the action through. Public score 0.5.
    /// </summary>
    public const string DryRunBlock = "dry_run_block";

    /// <summary>
    /// The <c>admin_alert</c> verdict; let the action through. Public score 0.25.
    /// </summary>
    public const string AdminAlert = "admin_alert";
}
