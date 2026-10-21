using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// POLICY-HEALER-KEEPS-TARGET (R-326-B): <see cref="PolicyBot"/> turned every non-tank seat to the damage dealers'
/// assist target, the healer included. At seed 1 key 2 (kick plan) the healer pressed Cadence on the tank at 235, the
/// next reaction (242) pressed a TargetCommand to Choir member 100 while the cast was under way, and the cast resolved
/// on the enemy (window_opened attuned targetId 100 at 255): every Cadence the healer aimed at the tank in the Choir
/// pull (235, 266, 346, 381, 512, 542, 608) was turned onto an enemy, its Mend never reached the tank, and the tank
/// died at 661. The assist rule is the damage dealers' (the class summary: "damage dealers attack their target"); the
/// healer's targets are its heal pick's.
/// </summary>
public sealed class PolicyHealerKeepsTargetTests
{
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";
    private const int Chanter = 2;
    private const int Tick = 235;
    private const int CastTicks = 20;

    [Fact(DisplayName = "A policy healer casting a heal on the tank is never turned to an enemy target mid-cast")]
    public void Healer_heal_on_the_tank_keeps_its_target()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = Tick }, content).Run;
        Assert.Equal(Tick, run.State.Tick);
        var kits = KitRegistry.From(registry);
        var seat = new PolicyBot(policy, new PolicyContext(), registry, 1, Chanter, PlaytestRunner.TankId);

        var turned = new System.Collections.Generic.List<string>();
        for (var t = Tick + 1; t < Tick + CastTicks; t++)
        {
            var state = run.State.Clone();
            state.Tick = t;
            var healer = state.Actors[Chanter];
            healer.CastId = "chanter.cadence";
            healer.CastStartTick = Tick;
            healer.CastEndsTick = Tick + CastTicks;
            healer.TargetId = PlaytestRunner.TankId;
            var view = BotViews.BuildBotView(state, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };

            // The scene: the damage dealers' assist target is a living enemy other than the healer's target.
            Assert.Contains(state.Actors.Values, a => a.Kind == ActorKind.Enemy && a.Alive && a.Id == state.Actors[PlaytestRunner.TankId].TargetId);

            var decision = seat.Decide(view, BotMemory.Default(PlaytestRunner.ScriptedSkillDial), Chanter, Skill.SeedPrng(1));
            turned.AddRange(decision.Commands.OfType<TargetCommand>()
                .Where(c => c.TargetId is { } id && state.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Enemy)
                .Select(c => $"{t}->{c.TargetId}"));
        }

        Assert.True(turned.Count == 0, "the healer turned its Cadence to an enemy mid-cast: " + string.Join(", ", turned));
    }
}
