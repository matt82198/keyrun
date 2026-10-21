using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-FORK-RETUNE-1 (finding f-f1ab879b; BIBLE section 6 "Every option changes function", section 7 "Facets change
/// function"): two fork options measured dominated beyond the seed noise (<see cref="ForkParityTests"/>) are fixed by
/// changing what they DO, not by moving numbers. Each case drives the talented ability exactly as the talent screen
/// patches it (<see cref="C.TalentPicks.ApplyPatch"/>) through <see cref="S.Rules.Make"/> with the run content the game
/// hands the sim (<see cref="HostRunSetup.Build"/>), so the data, the patch and the sim rule are proven together.
/// <list type="bullet">
/// <item>Reckoning "Overwhelm" (vs "Execute", cheaper and lighter): a frontal cone at the full 100 Momentum that still
/// closes a Window, then leaves Sundered on its target for an ally to close.</item>
/// </list>
/// Chorus "Encore" is not changed here: its one function change measured as dominating Discipline beyond noise, and
/// the ruling says to stop and report rather than iterate (see the PR body).
/// </summary>
public sealed class ForkFunctionTests
{
    private const int WardenId = 1;
    private const int CinderId = 2;
    private const int EnemyA = 100;
    private const int EnemyB = 101;
    private const int CastTick = 10;

    private static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    private static S.AbilityDef Talented(string classId, string abilityId, string optionId)
    {
        var reg = Registry.Value;
        var option = reg.Classes[classId].Talents.First(t => t.AbilityId == abilityId).Options.First(o => o.Id == optionId);
        return ForkParityTests.ToSim(C.TalentPicks.ApplyPatch(reg.Abilities[abilityId], option.Patch));
    }

    private static RunContent Content() => HostRunSetup.Build(Registry.Value, "hollow-vault", 2, "warden").Content;

    /// <summary>
    /// A Warden at the origin facing +X with 100 Momentum, two enemies in front of it (A carrying a Cinder's Kindled), casts
    /// <paramref name="reckoning"/> on A at <see cref="CastTick"/>; returns every event of that step.
    /// </summary>
    private static List<SimEvent> CastReckoning(S.AbilityDef reckoning)
    {
        var content = Content();
        var abilities = new Dictionary<string, S.AbilityDef>(content.Abilities, StringComparer.Ordinal) { [reckoning.Id] = reckoning };
        var rules = S.Rules.Make(RunWiring.RulesConfigOf(content) with { Abilities = abilities });
        var warden = S.Actors.Create(WardenId, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(0, 0, 0), 1000, 100);
        warden.Resource = 100;
        var cinder = S.Actors.Create(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(-5000, 0, 0), 1000, 100);
        var a = S.Actors.Create(EnemyA, ActorKind.Enemy, "x", ActorRole.None, new Vec3(1500, 0, 0), 5000, 0);
        var b = S.Actors.Create(EnemyB, ActorKind.Enemy, "x", ActorRole.None, new Vec3(2000, 0, 300), 5000, 0);
        a.Effects.Add(new EffectInstance
        {
            EffectId = S.WindowState.WindowEffectId("kindled", "cinder", false),
            SourceId = CinderId,
            Stacks = 1,
            EndsTick = 200,
            NextPeriodicTick = null,
        });
        var state = RunWiring.MergeActors(SimState.Create(1), new[] { warden, cinder, a, b });
        state.Phase = "combat";
        state.Tick = CastTick;
        var events = new List<SimEvent>();
        rules(state, new Command[] { new CastCommand(CastTick, WardenId, reckoning.Id, EnemyA) }, events.Add);
        return events;
    }

    private static bool SunderedOn(List<SimEvent> events, int target) => events.Any(e => e.Type == "window_opened" &&
        e.Get("windowId") is JsString { Value: "sundered" } && e.Get("targetId") is JsInt t && t.Value == target);

    // An on-target window op opens on the cast's primary target only (Windows.ApplyWindowsAndCombosTo; only Warcry's
    // multi-target tag spreads one), so the cone hits both enemies and the Sundered lands on A, the target.
    [Fact(DisplayName = "fork function > Overwhelm's cone Reckoning hits every enemy in front, closes a Window, then leaves Sundered on its target (R-FORK-RETUNE-1)")]
    public void Overwhelm_hits_the_cone_pays_off_and_leaves_Sundered_on_its_target()
    {
        var events = CastReckoning(Talented("warden", "warden.reckoning", "overwhelm"));
        Assert.Contains(events, e => e.Type == "damage" && e.Get("targetId") is JsInt t && t.Value == EnemyA);
        Assert.Contains(events, e => e.Type == "damage" && e.Get("targetId") is JsInt t && t.Value == EnemyB);
        Assert.Contains(events, e => e.Type == "combo" && e.Get("comboId") is JsString { Value: "forge" });
        Assert.True(SunderedOn(events, EnemyA), "no Sundered on A");
        Assert.False(SunderedOn(events, EnemyB), "Sundered on B");
    }

    [Fact(DisplayName = "fork function > Execute's Reckoning closes a Window but opens none (R-FORK-RETUNE-1 control)")]
    public void Execute_pays_off_and_opens_no_Window()
    {
        var events = CastReckoning(Talented("warden", "warden.reckoning", "execute"));
        Assert.Contains(events, e => e.Type == "combo" && e.Get("comboId") is JsString { Value: "forge" });
        Assert.DoesNotContain(events, e => e.Type == "window_opened");
    }
}
