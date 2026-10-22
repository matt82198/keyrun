using System;
using Keyrun.Content;
using Keyrun.Host;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-caa537fc (P0 wrong data): the in-run controls hint names the run's own dungeon, never a literal. A
/// <c>sunken-bellfoundry</c> run must not show "Hollow Vault" at the top of the screen; the Hollow Vault keeps its
/// exact historical line.
/// </summary>
public sealed class HelpLineTests
{
    [Fact(DisplayName = "The Bellfoundry run's hint line starts with the Bellfoundry's display name and never names the Hollow Vault")]
    public void Bellfoundry_hint_names_the_run_dungeon()
    {
        var reg = ContentLoader.Load();

        var line = HelpLine.For(reg, "sunken-bellfoundry");

        Assert.StartsWith("The Sunken Bellfoundry - WASD move,", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Hollow Vault", line, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "The Hollow Vault's hint line is unchanged: its display name then the controls")]
    public void Hollow_vault_hint_is_unchanged()
    {
        var reg = ContentLoader.Load();

        Assert.Equal(
            "Hollow Vault - WASD move, hold right mouse to look, 1-5 abilities, Q kick (hover a foe), E defensive, R utility, F charge, Tab target, G ping, V camera",
            HelpLine.For(reg, "hollow-vault"));
    }
}
