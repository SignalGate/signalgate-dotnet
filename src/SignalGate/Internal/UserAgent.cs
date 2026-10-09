using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SignalGate.Internal;

// The SDK name, version and User-Agent header value.
internal static class UserAgent
{
    internal const string SdkName = "signalgate-backend-sdk";

    internal const string SdkVersion = "0.1.0";

    // signalgate-backend-sdk/<version> (dotnet/<runtime version>; <os>), computed once.
    internal static string Value { get; } = Build();

    private static string Build()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SdkName}/{SdkVersion} (dotnet/{Environment.Version}; {OperatingSystemName()})");
    }

    private static string OperatingSystemName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "darwin";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            return "freebsd";
        }

        return "unknown";
    }
}
