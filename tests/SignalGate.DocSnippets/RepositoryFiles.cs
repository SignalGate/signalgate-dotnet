using System;
using System.IO;

namespace SignalGate.DocSnippets;

// Paths of repository files, found by walking up from the test output folder to the solution file.
internal static class RepositoryFiles
{
    private const string SolutionFileName = "SignalGate.slnx";

    internal static string Root { get; } = FindRoot();

    internal static string Readme => Path.Combine(Root, "README.md");

    internal static string Changelog => Path.Combine(Root, "CHANGELOG.md");

    internal static string SnippetsFolder => Path.Combine(Root, "tests", "SignalGate.DocSnippets", "Snippets");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No folder above {AppContext.BaseDirectory} contains {SolutionFileName}.");
    }
}
