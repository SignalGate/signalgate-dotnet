using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SignalGate.DocSnippets;

// Every C# example in README.md must be compiled: each one is copied verbatim into a
// "#region readme:<name>" block under Snippets/, and the two sets must stay identical.
public sealed class ReadmeSnippetTests
{
    private const string CSharpFence = "csharp";

    [Fact]
    public void Readme_contains_csharp_examples()
    {
        Assert.NotEmpty(ReadmeExamples());
    }

    [Fact]
    public void Readme_labels_csharp_examples_only_with_the_csharp_fence()
    {
        string[] otherLabels = ["cs", "c#"];

        List<string> wrong = SnippetText.FencedBlocks(File.ReadAllText(RepositoryFiles.Readme))
            .Select(block => block.Info.Split(' ', 2)[0])
            .Where(label => otherLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Snippet_files_contain_regions_with_unique_names()
    {
        IReadOnlyList<SnippetRegion> regions = SnippetRegions();

        Assert.NotEmpty(regions);
        List<string> duplicates = regions
            .GroupBy(region => region.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_readme_example_matches_exactly_one_compiled_snippet()
    {
        IReadOnlyList<SnippetRegion> regions = SnippetRegions();
        var failures = new List<string>();

        foreach (string example in ReadmeExamples())
        {
            List<SnippetRegion> matches = regions
                .Where(region => string.Equals(SnippetText.Normalize(region.Code), example, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
            {
                failures.Add($"README example matches {matches.Count} snippet regions (expected 1):\n{example}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    [Fact]
    public void Every_compiled_snippet_appears_exactly_once_in_the_readme()
    {
        IReadOnlyList<string> examples = ReadmeExamples();
        var failures = new List<string>();

        foreach (SnippetRegion region in SnippetRegions())
        {
            string code = SnippetText.Normalize(region.Code);
            int count = examples.Count(example => string.Equals(example, code, StringComparison.Ordinal));
            if (count != 1)
            {
                failures.Add($"Region {region.Name} in {region.File} appears {count} times in README.md (expected 1):\n{code}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    private static List<string> ReadmeExamples()
    {
        return SnippetText.FencedBlocks(File.ReadAllText(RepositoryFiles.Readme))
            .Where(block => string.Equals(block.Info, CSharpFence, StringComparison.Ordinal))
            .Select(block => SnippetText.Normalize(block.Code))
            .ToList();
    }

    private static List<SnippetRegion> SnippetRegions()
    {
        return Directory.GetFiles(RepositoryFiles.SnippetsFolder, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .SelectMany(path => SnippetText.Regions(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
    }
}
