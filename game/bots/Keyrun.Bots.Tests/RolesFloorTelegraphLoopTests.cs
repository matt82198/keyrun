using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Port of tests/bots/floor-telegraph-loop.test.ts (1 case): the real producer (FloorTelegraphs) over real sim state
/// that a real <see cref="Rules.Make"/> step produced, and the real bot decide loop with the real roles.
/// </summary>
public sealed class RolesFloorTelegraphLoopTests
{
    private const int BossId = 9;
    private const int DamageBotId = 1;
    private const int MaxEscapeTicks = 15;
    private const long StepMm = 2000;
    private const long PatchRadius = 2000;

    /// <summary>Mirrors content's seed.deathBloom: instant circle, one window op, onTarget false, setupClass "boss".</summary>
    private static readonly AbilityDef BossFloorPatchAbility = new()
    {
        Id = "boss.deathbloom",
        CastTicks = 0,
        GcdTicks = 0,
        CooldownTicks = 0,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 0,
        Shape = new AbilityShape(ShapeKind.Circle, PatchRadius),
        Interruptible = false,
        Tags = new[] { "setup:seedbloom" },
        Effects = new EffectOp[] { new WindowOp("seedbloom", 300, false, "boss", RadiusMm: PatchRadius) },
    };

    private static List<BotTelegraph> Derive(SimState state) =>
        FloorTelegraphs.DeriveFloorTelegraphs(state).Select(BotViews.FromFloorTelegraph).ToList();

    private static bool InAny(Vec3 pos, IEnumerable<BotTelegraph> telegraphs) =>
        Positioning.IsInAnyDanger(PointMm.Of(pos), telegraphs.Select(t => t with { Danger = true }));

    [Fact(DisplayName = "hostile floor patch, real producer + real StepRules + real bot decide loop > moves a damage bot standing inside a boss-opened floor Window clear of it")]
    public void BossPatchEscapeLoop()
    {
        var boss = Actors.Create(BossId, ActorKind.Enemy, "seed", ActorRole.None, new Vec3(0, 0, 0), 5000, 0);
        var damageBot = Actors.Create(DamageBotId, ActorKind.Player, "warrior", ActorRole.Damage, new Vec3(0, 0, 0), 1000, 100);
        var rules = Rules.Make(new RulesConfig
        {
            Abilities = new Dictionary<string, AbilityDef> { [BossFloorPatchAbility.Id] = BossFloorPatchAbility },
            Effects = new Dictionary<string, EffectDef>(),
            LockoutTicks = 80,
            BaseSpeedMmPerTick = 300,
            ActorRadiusMm = 1,
            Bounds = new Bounds(-5000, 5000, -5000, 5000),
        });
        var state = new SimState
        {
            Tick = 0,
            Seed = 1,
            Prng = new PrngState(0, 0, 0, 0),
            Actors = BotTestKit.Table(boss, damageBot),
            RunTimerTicks = 0,
            Phase = "combat",
        };
        var events = new List<SimEvent>();
        rules(state, new Command[] { new CastCommand(state.Tick, BossId, "boss.deathbloom") }, events.Add);
        state.Tick += 1;
        var json = events.Select(e => e.ToCanonicalJson()).ToList();
        Assert.Contains(json, j => j.Contains("\"type\":\"window_opened\"", StringComparison.Ordinal)
            && j.Contains("\"windowId\":\"seedbloom\"", StringComparison.Ordinal) && j.Contains("\"onTarget\":false", StringComparison.Ordinal));
        Assert.Contains(json, j => j.Contains("\"type\":\"window_opened\"", StringComparison.Ordinal)
            && j.Contains("\"id\":\"" + Floor.FloorPatchPrefix + "seedbloom\"", StringComparison.Ordinal));

        var telegraphs = Derive(state);
        var only = Assert.Single(telegraphs);
        Assert.Equal(Floor.FloorPatchPrefix + "seedbloom", only.Id);
        Assert.False(only.Danger);

        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var ticks = 0;
        for (; ticks < MaxEscapeTicks; ticks++)
        {
            telegraphs = Derive(state);
            var self = state.Actors[DamageBotId];
            if (!InAny(self.Pos, telegraphs))
            {
                break;
            }
            var view = new BotView(state, telegraphs, ClassKitView.Empty);
            var result = bot.Decide(view, memory, DamageBotId, prng);
            memory = result.Memory;
            prng = result.Prng;
            var move = BotTestKit.FirstMove(result.Commands);
            Assert.NotNull(move);
            BotTestKit.SetPos(self, BotTestKit.ApplyMove(PointMm.Of(self.Pos), move, StepMm));
            state.Tick += 1;
        }

        Assert.True(ticks < MaxEscapeTicks);
        Assert.False(InAny(state.Actors[DamageBotId].Pos, Derive(state)));
    }
}
