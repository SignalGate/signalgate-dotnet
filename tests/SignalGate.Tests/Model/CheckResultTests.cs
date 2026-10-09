using System;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Model;

public sealed class CheckResultTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Constructor_keeps_every_value()
    {
        var result = new CheckResult("block", 1.0, "req_1", "acme", "2026-04-01T13:08:50Z", 812, failedOpen: false);

        Assert.Equal("block", result.Action);
        Assert.Equal(1.0, result.Score);
        Assert.Equal("req_1", result.RequestId);
        Assert.Equal("acme", result.TenantId);
        Assert.Equal("2026-04-01T13:08:50Z", result.Timestamp);
        Assert.Equal(812, result.ProcessingTimeUs);
        Assert.False(result.FailedOpen);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Fail_open_shape_can_be_constructed()
    {
        var result = new CheckResult(CheckActions.Allow, 0.0, "", "", "", 0, failedOpen: true);

        Assert.Equal("allow", result.Action);
        Assert.Equal(0.0, result.Score);
        Assert.Equal("", result.RequestId);
        Assert.True(result.FailedOpen);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Null_strings_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new CheckResult(null!, 0, "", "", "", 0, false));
        Assert.Throws<ArgumentNullException>(() => new CheckResult("", 0, null!, "", "", 0, false));
        Assert.Throws<ArgumentNullException>(() => new CheckResult("", 0, "", null!, "", 0, false));
        Assert.Throws<ArgumentNullException>(() => new CheckResult("", 0, "", "", null!, 0, false));
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Known_actions_have_their_wire_values()
    {
        Assert.Equal("allow", CheckActions.Allow);
        Assert.Equal("block", CheckActions.Block);
        Assert.Equal("dry_run_block", CheckActions.DryRunBlock);
        Assert.Equal("admin_alert", CheckActions.AdminAlert);
    }
}
