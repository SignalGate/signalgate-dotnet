using System;

namespace SignalGate.Tests.Fakes;

// Time limits that turn a deadlock or an endless loop into a named test failure instead of a hung test run.
// Every test has a time limit no longer than RepeatedTimeoutMs (checked by TimeLimitTests), and every close of a
// client waits through BoundedClose. Each limit is far longer than the work it bounds, even on a loaded machine.
public static class HangGuard
{
    public const int TimeoutMs = 30_000;

    // For tests that repeat a scenario many times.
    public const int RepeatedTimeoutMs = 120_000;

    // How long closing a client may take, beyond a deadline that the close is allowed to wait out. A close with
    // nothing left to deliver takes milliseconds, so this is far longer than needed; it is kept short because a
    // regression that stops closes from finishing makes every test that closes a client wait this long.
    public static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(5);
}
