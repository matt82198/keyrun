using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PURPOSE: this artifact exists to float the word IMMUNE in combat text when a stun-immune enemy refuses a stun
/// (finding f-04dba988), red-first; status: real.
/// #429 (ControlStops.RefuseImmuneStuns) made the stun-immune boss refuse a stun with an <c>immune</c> event, and the HUD
/// had no readout for it (WoW floats "Immune" over the target). On the run the host builds (<see cref="HostRunSetup.Build"/>,
/// run.json stunImmuneEnemies = the Hollow Warden), a Cold Snap on the boss is pushed through the HUD's word path
/// (<see cref="CombatTextWords.WordFloat"/>, the call src/Hud/CombatText.cs makes per event): the human's refused stun
/// floats exactly one IMMUNE over the boss with its proof line; a bot's refused stun floats nothing (HUD-DECLUTTER: only
/// the played seat's combat text); a stun that lands on a stunnable boss floats nothing.
/// </summary>
public sealed class ImmuneCombatTextTests
{
    private const long Seed = 1;
    private const int Boss = HostRunSetup.BossActorId;
    private const string ColdSnap = "cinder.coldSnap"; // instant, target, applies stunned (content/data/abilities/cinder.json)

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private sealed record Word(int TargetId, string Text, string Proof);

    private sealed record Stunned(List<SimEvent> Events, Run After, int HumanId, int CasterId);

    // The host's run with the human in humanClass's seat; the cinder seat casts Cold Snap on the boss from 3 m away.
    private static Stunned ColdSnapTheBoss(string humanClass, bool bossStunImmune)
    {
        var setup = HostRunSetup.Build(Registry, "hollow-vault", 2, humanClass, withBots: true);
        var content = bossStunImmune ? setup.Content : setup.Content with { StunImmuneClassIds = null };
        Assert.Contains("hollowWarden.boss", setup.Content.StunImmuneClassIds ?? new List<string>());
        var run = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, content);
        Assert.True(run.State.Actors.ContainsKey(Boss), "the boss is not in the run at tick 0");
        var caster = setup.Party.Single(p => p.ClassId == "cinder").Id;
        var boss = run.State.Actors[Boss].Pos;
        run.State.Actors[caster].Pos = new Vec3(boss.X, boss.Y, boss.Z + 3000);
        var stepped = RunRoot.StepRun(run, new Command[] { new CastCommand(run.State.Tick, caster, ColdSnap, Boss) });
        return new Stunned(stepped.Events.ToList(), stepped.Run, setup.HumanActorId, caster);
    }

    private static List<Word> Words(IEnumerable<SimEvent> events, int human)
    {
        var words = new List<Word>();
        foreach (var e in events)
        {
            if (CombatTextWords.WordFloat(e, human, out var target, out var text, out var proof))
            {
                words.Add(new Word(target, text, proof));
            }
        }
        return words;
    }

    private static bool Is(SimEvent e, string key, int id) => e.Get(key) is JsInt { Value: var v } && v == id;

    [Fact(DisplayName = "The human's stun refused by the stun-immune boss floats one IMMUNE over the boss, with its proof line (f-04dba988)")]
    public void Human_refused_stun_floats_IMMUNE_over_the_boss()
    {
        var s = ColdSnapTheBoss("cinder", bossStunImmune: true);
        Assert.Equal(s.HumanId, s.CasterId);
        var immune = Assert.Single(s.Events, e => e.Type == "immune");
        Assert.True(Is(immune, "sourceId", s.HumanId) && Is(immune, "targetId", Boss), "the refused stun is not the human's on the boss");
        Assert.DoesNotContain(s.After.State.Actors[Boss].Effects, e => e.EffectId == "stunned");

        var word = Assert.Single(Words(s.Events, s.HumanId), w => w.Text == CombatTextRule.ImmuneText);
        Assert.Equal(Boss, word.TargetId);
        Assert.Equal("IMMUNE", word.Text);
        Assert.Equal($"HUD immune source={s.HumanId} target={Boss} effect=stunned text=IMMUNE", word.Proof);
    }

    [Fact(DisplayName = "A bot's stun refused by the stun-immune boss floats no IMMUNE (f-04dba988)")]
    public void Bot_refused_stun_floats_nothing()
    {
        var s = ColdSnapTheBoss("warden", bossStunImmune: true);
        Assert.NotEqual(s.HumanId, s.CasterId);
        var immune = Assert.Single(s.Events, e => e.Type == "immune");
        Assert.True(Is(immune, "sourceId", s.CasterId) && Is(immune, "targetId", Boss), "the refused stun is not the bot's on the boss");

        Assert.DoesNotContain(Words(s.Events, s.HumanId), w => w.Text == CombatTextRule.ImmuneText);
    }

    [Fact(DisplayName = "The human's stun that lands on a stunnable boss floats no IMMUNE (f-04dba988)")]
    public void Landed_stun_floats_nothing()
    {
        var s = ColdSnapTheBoss("cinder", bossStunImmune: false);
        Assert.Equal(s.HumanId, s.CasterId);
        Assert.Contains(s.After.State.Actors[Boss].Effects, e => e.EffectId == "stunned" && e.SourceId == s.HumanId);
        Assert.DoesNotContain(s.Events, e => e.Type == "immune");

        Assert.DoesNotContain(Words(s.Events, s.HumanId), w => w.Text == CombatTextRule.ImmuneText);
    }
}
