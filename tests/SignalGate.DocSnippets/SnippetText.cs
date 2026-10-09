using System;
using System.Collections.Generic;
using System.Linq;

namespace SignalGate.DocSnippets;

internal sealed record FencedBlock(string Info, string Code);

internal sealed record SnippetRegion(string File, string Name, string Code);

// Reads fenced code blocks from Markdown and "#region readme:<name>" blocks from C# files.
internal static class SnippetText
{
    private const string RegionPrefix = "#region readme:";
    private const string RegionEnd = "#endregion";

    internal static IReadOnlyList<FencedBlock> FencedBlocks(string markdown)
    {
        string[] lines = SplitLines(markdown);
        var blocks = new List<FencedBlock>();

        for (int i = 0; i < lines.Length; i++)
        {
            string opening = lines[i].Trim();
            int ticks = CountLeading(opening, '`');
            if (ticks < 3)
            {
                continue;
            }

            string info = opening[ticks..].Trim();
            var body = new List<string>();
            i++;
            while (i < lines.Length && !IsClosingFence(lines[i], ticks))
            {
                body.Add(lines[i]);
                i++;
            }

            if (i == lines.Length)
            {
                throw new InvalidOperationException($"A code block opened with \"{opening}\" is never closed.");
            }

            blocks.Add(new FencedBlock(info, string.Join('\n', body)));
        }

        return blocks;
    }

    internal static IReadOnlyList<SnippetRegion> Regions(string file, string source)
    {
        string[] lines = SplitLines(source);
        var regions = new List<SnippetRegion>();

        for (int i = 0; i < lines.Length; i++)
        {
            string opening = lines[i].Trim();
            if (!opening.StartsWith(RegionPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = opening[RegionPrefix.Length..].Trim();
            var body = new List<string>();
            i++;
            while (i < lines.Length && !lines[i].Trim().StartsWith(RegionEnd, StringComparison.Ordinal))
            {
                if (lines[i].Trim().StartsWith("#region", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Region {name} in {file} contains a nested region.");
                }

                body.Add(lines[i]);
                i++;
            }

            if (i == lines.Length)
            {
                throw new InvalidOperationException($"Region {name} in {file} is never closed.");
            }

            regions.Add(new SnippetRegion(file, name, string.Join('\n', body)));
        }

        return regions;
    }

    // Line endings become LF, trailing whitespace goes, the common indentation is removed and
    // blank lines at both ends are dropped.
    internal static string Normalize(string code)
    {
        List<string> lines = SplitLines(code).Select(line => line.TrimEnd()).ToList();

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        int indent = lines
            .Where(line => line.Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(line => line.Length == 0 ? line : line[indent..]));
    }

    private static string[] SplitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static bool IsClosingFence(string line, int openingTicks)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= openingTicks && CountLeading(trimmed, '`') == trimmed.Length;
    }

    private static int CountLeading(string text, char c)
    {
        int count = 0;
        while (count < text.Length && text[count] == c)
        {
            count++;
        }

        return count;
    }
}
