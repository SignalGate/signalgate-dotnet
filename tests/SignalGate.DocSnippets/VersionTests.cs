using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SignalGate.DocSnippets;

public sealed partial class VersionTests
{
    [Fact]
    public void Sdk_version_matches_the_assembly_and_the_top_changelog_entry()
    {
        string sdkVersion = SignalGateClient.SdkVersion;

        string? informational = typeof(SignalGateClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Assert.NotNull(informational);
        string assemblyVersion = informational.Split('+', 2)[0];

        string? heading = File.ReadLines(RepositoryFiles.Changelog)
            .FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.NotNull(heading);
        Match match = ChangelogHeading().Match(heading);
        Assert.True(match.Success, $"The first CHANGELOG.md entry heading has no version: {heading}");
        string changelogVersion = match.Groups[1].Value;

        Assert.Matches(@"^\d+\.\d+\.\d+$", sdkVersion);
        Assert.Equal(sdkVersion, assemblyVersion);
        Assert.Equal(sdkVersion, changelogVersion);
    }

    [GeneratedRegex(@"^## \[(\d+\.\d+\.\d+)\]")]
    private static partial Regex ChangelogHeading();
}
