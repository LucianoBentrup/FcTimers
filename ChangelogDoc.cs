using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FcTimers;

public enum ChangeType
{
    Added,
    Changed,
    Fixed,
    Removed,
    Other,
}

// Title vazio: item comum da lista. Com título: vira um card de destaque na janela de novidades.
public sealed record ChangeEntry(string Title, string Text);

public sealed record ChangeSection(ChangeType Type, List<ChangeEntry> Entries);

// Version "Unreleased" é o que ainda não saiu. Date pode faltar nas versões antigas.
public sealed record Release(string Version, string Date, List<ChangeSection> Sections)
{
    public bool IsUnreleased => Version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ChangeEntry> Highlights => Sections.SelectMany(s => s.Entries).Where(e => e.Title.Length > 0);
}

// Lê o CHANGELOG.md (formato Keep a Changelog), que é a única fonte das novidades: o arquivo vai dentro do plugin.
public static partial class ChangelogDoc
{
    public static List<Release> Parse(string markdown)
    {
        var releases = new List<Release>();
        Release? release = null;
        ChangeSection? section = null;

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (VersionLine().Match(line) is { Success: true } version)
            {
                release = new Release(version.Groups["v"].Value, version.Groups["d"].Value, []);
                releases.Add(release);
                section = null;
            }
            else if (release != null && line.StartsWith("### ", StringComparison.Ordinal))
            {
                var type = Enum.TryParse<ChangeType>(line[4..].Trim(), true, out var known) ? known : ChangeType.Other;
                section = new ChangeSection(type, []);
                release.Sections.Add(section);
            }
            else if (section != null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                var text = line[2..].Trim();
                section.Entries.Add(BoldLead().Match(text) is { Success: true } lead
                    ? new ChangeEntry(lead.Groups["t"].Value.Trim(), lead.Groups["r"].Value.Trim())
                    : new ChangeEntry(string.Empty, text));
            }
            else if (section is { Entries.Count: > 0 } && line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0)
            {
                // Continuação do item anterior, quebrado em mais de uma linha.
                var last = section.Entries[^1];
                section.Entries[^1] = last with { Text = $"{last.Text} {line.Trim()}" };
            }
        }

        return releases;
    }

    // Tira a marcação que a janela não desenha: `código` e [texto](link).
    public static string Plain(string text) => Link().Replace(text.Replace("`", string.Empty), "$1");

    // Versões que a pessoa ainda não viu: as mais novas que a última vista, sem o que não saiu.
    public static List<Release> NewSince(IEnumerable<Release> releases, string? lastSeen)
    {
        var published = releases.Where(r => !r.IsUnreleased).ToList();
        if (string.IsNullOrEmpty(lastSeen) || !System.Version.TryParse(lastSeen, out var seen))
            return published.Take(1).ToList();

        var newer = published.Where(r => System.Version.TryParse(r.Version, out var v) && v > seen).ToList();
        return newer.Count > 0 ? newer : published.Take(1).ToList();
    }

    [GeneratedRegex(@"^## \[(?<v>[^\]]+)\](?:\s*-\s*(?<d>\d{4}-\d{2}-\d{2}))?")]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^\*\*(?<t>[^*]+?):?\*\*:?\s*(?<r>.*)$")]
    private static partial Regex BoldLead();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();
}
