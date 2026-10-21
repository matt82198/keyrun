using System.Linq;
using Keyrun.Hud;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The private Ascension readout (BIBLE s7; f-275d47c9) the between-runs screen's Character tab and the run-end panel
/// draw (game/src/Hud/AscensionReadout.cs, Godot-free). Every line is built from <see cref="Ascension.Of"/> over the
/// character's own <see cref="Progression"/>, the same read the run uses, never a second computation; and every line
/// says only the player sees it (BIBLE s12 [DECIDE] on visibility to others is open, so nothing is shown to another
/// seat).
/// </summary>
public sealed class AscensionReadoutTests
{
    private static Item At(string slot, int tier) =>
        new($"reward-{slot}-edge-t{tier}-0", slot, tier, new Facet($"{slot}-edge", "Strike also applies Sundered", "ability-1"), Rewards.StartingCharges);

    private static Progression WithTiers(params int[] tiers) =>
        Rewards.GearSlots.Take(tiers.Length).Select((s, i) => (s, i))
            .Aggregate(Progression.Start(7).BankRun(RunResult.Timed), (p, x) => p.Take(At(x.s, tiers[x.i])));

    private static Progression Ascended() => WithTiers(10, 10, 10, 10, 10);

    [Fact]
    public void Before_the_cap_the_character_tab_counts_the_slots_at_tier_10()
    {
        var line = AscensionReadout.CharacterLine(Ascension.Of(WithTiers(10, 10, 10, 4)));
        Assert.Equal("Ascension: 3/5 slots at Tier 10  (only you see this)", line);
        Assert.Equal("Ascension: 0/5 slots at Tier 10  (only you see this)", AscensionReadout.CharacterLine(Ascension.Of(Progression.Start(1))));
    }

    [Fact]
    public void After_the_cap_the_character_tab_shows_ascended_and_the_marks()
    {
        Assert.Equal("Ascended  -  0 Ascension marks (timed above +10)  (only you see this)", AscensionReadout.CharacterLine(Ascension.Of(Ascended())));
        var one = Ascended().BankRun(RunResult.Timed, 11);
        Assert.Equal("Ascended  -  1 Ascension mark (timed above +10)  (only you see this)", AscensionReadout.CharacterLine(Ascension.Of(one)));
        var three = one.BankRun(RunResult.Timed, 14).BankRun(RunResult.Depleted, 18).BankRun(RunResult.Timed, 20);
        Assert.Equal("Ascended  -  3 Ascension marks (timed above +10)  (only you see this)", AscensionReadout.CharacterLine(Ascension.Of(three)));
    }

    [Fact]
    public void The_run_end_line_names_the_mark_this_run_earned()
    {
        var p = Ascended().BankRun(RunResult.Timed, 11).BankRun(RunResult.Timed, 14);
        Assert.Equal("Ascension mark earned: timed +14  (2 marks, only you see this)", AscensionReadout.RunEndLine(Ascension.Of(p)));
    }

    [Fact]
    public void A_run_that_earned_no_mark_shows_the_standing_state()
    {
        var ascended = Ascended().BankRun(RunResult.Timed, 11).BankRun(RunResult.Timed, 10);
        Assert.Equal("Ascended  -  1 Ascension mark (timed above +10)  (only you see this)", AscensionReadout.RunEndLine(Ascension.Of(ascended)));
        var below = WithTiers(10, 10).BankRun(RunResult.Timed, 15);
        Assert.Equal("Ascension: 2/5 slots at Tier 10  (only you see this)", AscensionReadout.RunEndLine(Ascension.Of(below)));
    }

    [Fact]
    public void The_proof_line_carries_every_value_the_lines_draw()
    {
        var p = Ascended().BankRun(RunResult.Timed, 12);
        Assert.Equal(
            "SCREEN ascension ascended=1 capped=5 marks=1 last=timed key=12 mark=1 private=1",
            AscensionReadout.LogLine("SCREEN", Ascension.Of(p)));
        Assert.Equal(
            "HUD runend ascension ascended=0 capped=0 marks=0 last=none key=- mark=0 private=1",
            AscensionReadout.LogLine("HUD runend", Ascension.Of(Progression.Start(1))));
    }
}
