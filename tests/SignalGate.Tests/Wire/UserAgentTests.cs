using System;
using System.Reflection;
using System.Text.RegularExpressions;
using SignalGate.Internal;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests.Wire;

public sealed partial class UserAgentTests
{
    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void User_agent_has_the_documented_shape()
    {
        Assert.Matches(UserAgentPattern(), UserAgent.Value);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void User_agent_names_the_sdk_and_runtime_versions()
    {
        Assert.StartsWith("signalgate-backend-sdk/" + UserAgent.SdkVersion + " (dotnet/", UserAgent.Value, StringComparison.Ordinal);
        Assert.Contains("dotnet/" + Environment.Version + "; ", UserAgent.Value, StringComparison.Ordinal);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void User_agent_names_the_current_operating_system()
    {
        string expected = OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "darwin"
            : OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsFreeBSD() ? "freebsd"
            : "unknown";

        Assert.EndsWith("; " + expected + ")", UserAgent.Value, StringComparison.Ordinal);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Sdk_name_and_version_have_the_expected_values()
    {
        string? informational = typeof(UserAgent).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.NotNull(informational);
        Assert.Equal("signalgate-backend-sdk", UserAgent.SdkName);
        Assert.Equal(UserAgent.SdkVersion, informational.Split('+')[0]);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void User_agent_is_computed_once()
    {
        Assert.Same(UserAgent.Value, UserAgent.Value);
    }

    [GeneratedRegex(@"^signalgate-backend-sdk/\d+\.\d+\.\d+ \(dotnet/[^;)]+; (linux|darwin|windows|freebsd|unknown)\)$")]
    private static partial Regex UserAgentPattern();
}
