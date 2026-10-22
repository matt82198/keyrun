using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The headless half of HudTooltip-SceneTest.ps1 (Matt 2026-10-22, ability tooltips): the HUD builds a slot's tooltip
/// from the run's own sim ability (<see cref="RunAbilityFacts.FromSim"/>), not the content row. In the default run (key
/// 2, Warden seat 1, bots on, no talents) every one of the human's nine slots must read the same tooltip through the
/// sim path as through the content path, slot 1 is the Warden's Strike under its themed name, and the tooltip's
/// seconds use the sim's own tick rate.
/// </summary>
public sealed class AbilityTooltipRunTests
{
    [Fact(DisplayName = "default run: each human slot's tooltip from the run's sim ability equals the content tooltip; slot 1 is Sundering Verdict")]
    public void Default_run_slots_read_the_same_tooltip_through_the_sim()
    {
        var registry = ContentLoader.Load();
        var table = AbilityTextLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        Assert.Equal(9, setup.Slots.Count);
        foreach (var slot in setup.Slots)
        {
            var row = table.For(slot.AbilityId);
            Assert.True(row is not null, $"slot {slot.Key} ({slot.AbilityId}) has no ability-text row");
            var fromSim = AbilityTooltip.Build(RunAbilityFacts.FromSim(setup.Content.Abilities[slot.AbilityId]), row!, registry);
            var fromContent = AbilityTooltip.Build(AbilityTooltipFacts.From(registry.Abilities[slot.AbilityId]), row!, registry);
            Assert.Equal(fromContent.Lines, fromSim.Lines);
        }
        var first = setup.Slots.First();
        Assert.Equal("1", first.Key);
        Assert.Equal("warden.strike", first.AbilityId);
        Assert.Equal("Sundering Verdict", table.For(first.AbilityId)!.DisplayName);
    }

    [Fact(DisplayName = "the tooltip's seconds use the sim tick rate")]
    public void Tooltip_tick_rate_is_the_sim_tick_rate() => Assert.Equal(Tick.TickHz, AbilityTooltip.TickHz);
}
