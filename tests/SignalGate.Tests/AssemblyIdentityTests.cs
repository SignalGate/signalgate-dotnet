using System;
using System.Reflection;
using SignalGate.Tests.Fakes;
using Xunit;

namespace SignalGate.Tests;

public sealed class AssemblyIdentityTests
{
    private static readonly Assembly Sdk = Assembly.Load(new AssemblyName("SignalGate"));

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Sdk_assembly_is_strong_named_with_the_repository_key()
    {
        byte[]? token = Sdk.GetName().GetPublicKeyToken();

        Assert.NotNull(token);
        Assert.Equal("f702817f7ddba0f7", Convert.ToHexString(token).ToLowerInvariant());
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Sdk_assembly_version_stays_fixed_before_1_0()
    {
        Assert.Equal(new Version(0, 0, 0, 0), Sdk.GetName().Version);
    }

    [Fact(Timeout = HangGuard.TimeoutMs)]
    public void Sdk_informational_version_carries_the_package_version()
    {
        string? informational = Sdk.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.NotNull(informational);
        Assert.Equal(SignalGateClient.SdkVersion, informational.Split('+')[0]);
    }
}
