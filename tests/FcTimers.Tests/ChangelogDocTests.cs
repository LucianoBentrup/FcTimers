using System;
using System.IO;
using System.Linq;
using Xunit;

namespace FcTimers.Tests;

public class ChangelogDocTests
{
    private const string Sample = """
        # Changelog

        ## [Unreleased]

        ### Added
        - Coming soon.

        ## [0.10.0] - 2026-11-23

        ### Added
        - **Airships:** return time and loot,
          read from the workshop.
        - Discord: `/lucro`.

        ### Fixed
        - A bug.

        ## [0.9.0] - 2026-11-22

        ### Changed
        - Renamed.

        [0.10.0]: https://example.com
        """;

    [Fact]
    public void Le_versoes_tipos_e_destaques_com_linha_quebrada()
    {
        var releases = ChangelogDoc.Parse(Sample);

        Assert.Equal(["Unreleased", "0.10.0", "0.9.0"], releases.Select(r => r.Version));
        var latest = releases[1];
        Assert.Equal("2026-11-23", latest.Date);
        Assert.Equal([ChangeType.Added, ChangeType.Fixed], latest.Sections.Select(s => s.Type));
        var card = latest.Highlights.Single();
        Assert.Equal("Airships", card.Title);
        Assert.Equal("return time and loot, read from the workshop.", card.Text);
        Assert.Equal("Discord: /lucro.", ChangelogDoc.Plain(latest.Sections[0].Entries[1].Text));
    }

    [Fact]
    public void Mostra_as_versoes_novas_desde_a_ultima_vista_sem_o_que_nao_saiu()
    {
        var releases = ChangelogDoc.Parse(Sample);

        Assert.Equal(["0.10.0"], ChangelogDoc.NewSince(releases, "0.9.0").Select(r => r.Version));
        Assert.Equal(["0.10.0", "0.9.0"], ChangelogDoc.NewSince(releases, "0.8.6").Select(r => r.Version));
        Assert.Equal(["0.10.0"], ChangelogDoc.NewSince(releases, null).Select(r => r.Version));
        Assert.Equal(["0.10.0"], ChangelogDoc.NewSince(releases, "0.10.0").Select(r => r.Version));
    }

    [Fact]
    public void O_CHANGELOG_do_repositorio_tem_todas_as_versoes_com_tipo_conhecido()
    {
        var releases = ChangelogDoc.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md")));

        Assert.Contains(releases, r => r.Version == "0.10.0" && r.Date == "2026-11-23");
        Assert.Contains(releases, r => r.Version == "0.6.1");
        Assert.All(releases, r => Assert.NotEmpty(r.Sections));
        Assert.All(releases.SelectMany(r => r.Sections), s => Assert.NotEqual(ChangeType.Other, s.Type));
        Assert.All(releases.SelectMany(r => r.Sections).SelectMany(s => s.Entries), e => Assert.False(string.IsNullOrWhiteSpace(e.Text)));
    }
}
