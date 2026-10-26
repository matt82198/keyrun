using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// GUIDED-GLIDE (G2.1: you die if you ignore the toolkit; bots must use it). A guided playtest seat whose plan turns the
/// telegraph dodge on, with a danger circle centred on the seat that resolves in 20 ticks and its mobility button off
/// cooldown and the GCD free, must either press its mobility button or move out of the telegraph within 20 Decide ticks.
/// Chanter seat 2 uses chanter.glide (a self buff); Warden seat 1 uses warden.charge (a displacement onto a living enemy
/// target beyond melee reach). "Moves out of the telegraph" is counted as a dodge move the seat issued (PolicyBot.DodgeMoves
/// rises), which is the only walk-out PolicyBot emits; a bare MoveCommand is not counted because it does not say where it leads.
/// </summary>
public sealed class GuidedGlideTests
{
    private const string GuidedPlan = "{\"mode\": \"guided\", \"dodge\": {\"telegraphs\": true}, \"mitigate\": [\"chanter\"]}";
    private const string WardenPlan = "{\"mode\": \"guided\", \"dodge\": {\"telegraphs\": true}, \"mitigate\": [\"warden\"]}";
    private const int Chanter = 2;
    private const int Warden = 1;
    private const int Tick = 60;
    private const int Window = 20;
    private const int Resolves = Tick + Window;
    private const int DangerTelegraphId = 9001;
    private const int ChoirA = 900;
    private const int ChoirB = 901;
    private const int WardenEnemy = 910;
    private const long DangerRadiusMm = 1500;
    private const long ChargeGapMm = 7500;
    private const string Glide = "chanter.glide";
    private const string Charge = "warden.charge";

    [Fact(DisplayName = "Guided chanter inside a danger telegraph that resolves in 20 ticks presses glide or dodges out within 20 ticks")]
    public void Guided_chanter_in_danger_telegraph_presses_glide_or_leaves_it()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(GuidedPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = Tick }, content).Run;
        Assert.Equal(Tick, run.State.Tick);
        var kits = KitRegistry.From(registry);
        var seat = new PolicyBot(policy, new PolicyContext(), registry, 1, Chanter, PlaytestRunner.TankId);
        var memory = BotMemory.Default(PlaytestRunner.ScriptedSkillDial);
        var prng = Skill.SeedPrng(1);
        var casts = new List<string>();
        var dodgesBefore = seat.DodgeMoves;

        for (var t = Tick; t < Resolves; t++)
        {
            var state = run.State.Clone();
            state.Tick = t;

            // The scene: the chanter at full health, glide ready, GCD free, nothing in flight, and a danger circle on its seat.
            var self = state.Actors[Chanter];
            self.Health = self.MaxHealth;
            self.GcdReadyTick = 0;
            self.Cooldowns[Glide] = 0;
            self.CastId = null;
            var telegraph = new BotTelegraph(DangerTelegraphId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Keyrun.Bots.TelegraphShape.Circle, PointMm.Of(self.Pos), Resolves, true) { Radius = DangerRadiusMm };
            var view = BotViews.BuildBotView(state, new[] { telegraph }, ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var decision = seat.Decide(view, memory, Chanter, prng);
            memory = decision.Memory;
            prng = decision.Prng;
            casts.AddRange(decision.Commands.OfType<CastCommand>()
                .Where(c => c.AbilityId is Glide)
                .Select(c => $"{t}:{c.AbilityId}"));
        }

        var dodged = seat.DodgeMoves - dodgesBefore;
        Assert.True(casts.Count > 0,
            $"the guided chanter inside a danger telegraph resolving at tick {Resolves} did not press {Glide} before it resolved (window {Window} ticks; dodge moves: {dodged}; cast commands: {string.Join(", ", casts)})");
    }

    [Fact(DisplayName = "Guided warden inside a danger telegraph that resolves in 20 ticks presses charge or dodges out within 20 ticks")]
    public void Guided_warden_in_danger_telegraph_presses_charge_or_leaves_it()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(WardenPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = Tick }, content).Run;
        Assert.Equal(Tick, run.State.Tick);
        var kits = KitRegistry.From(registry);
        var seat = new PolicyBot(policy, new PolicyContext(), registry, 1, Warden, PlaytestRunner.TankId);
        var memory = BotMemory.Default(PlaytestRunner.ScriptedSkillDial);
        var prng = Skill.SeedPrng(1);
        var casts = new List<string>();
        var dodgesBefore = seat.DodgeMoves;

        for (var t = Tick; t < Resolves; t++)
        {
            var state = run.State.Clone();
            state.Tick = t;

            // The scene: the warden at full health, charge ready, GCD free, nothing in flight, holding one living enemy
            // beyond melee reach but inside charge range, and a danger circle on its seat.
            var self = state.Actors[Warden];
            self.Health = self.MaxHealth;
            self.GcdReadyTick = 0;
            self.Cooldowns[Charge] = 0;
            self.CastId = null;
            self.TargetId = WardenEnemy;
            state.Actors[WardenEnemy] = ChoirMelee(WardenEnemy, self, +ChargeGapMm);
            var telegraph = new BotTelegraph(DangerTelegraphId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Keyrun.Bots.TelegraphShape.Circle, PointMm.Of(self.Pos), Resolves, true) { Radius = DangerRadiusMm };
            var view = BotViews.BuildBotView(state, new[] { telegraph }, ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var decision = seat.Decide(view, memory, Warden, prng);
            memory = decision.Memory;
            prng = decision.Prng;
            casts.AddRange(decision.Commands.OfType<CastCommand>()
                .Where(c => c.AbilityId is Charge)
                .Select(c => $"{t}:{c.AbilityId}"));
        }

        var dodged = seat.DodgeMoves - dodgesBefore;
        Assert.True(casts.Count > 0,
            $"the guided warden inside a danger telegraph resolving at tick {Resolves} did not press {Charge} before it resolved (window {Window} ticks; dodge moves: {dodged}; cast commands: {string.Join(", ", casts)})");
    }

    private static ActorState ChoirMelee(int id, ActorState self, long dxMm) => new()
    {
        Id = id,
        Kind = ActorKind.Enemy,
        ClassId = "choir.melee",
        Role = ActorRole.None,
        Pos = new Vec3(self.Pos.X + dxMm, self.Pos.Y, self.Pos.Z),
        Yaw = 512,
        Health = 6720,
        MaxHealth = 6720,
        Alive = true,
        TargetId = self.Id,
        Threat = new SortedDictionary<int, long> { [self.Id] = 100 },
    };
}
