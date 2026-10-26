using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-AGGRO-1 (ag): a bot Warden at or above <see cref="TankRole.DefensiveThresholdPct"/> hp with a loose pack (enemies held by
/// another seat, each still carrying the sim's default TargetId of the tank, so <see cref="TankPressure.UnderPackPressure"/>
/// reads three attackers) in melee reach and the taunt on cooldown must issue no defensive cast (warden.brace, warden.vigil,
/// warden.bulwark). The take-the-pack block finds the taunt down and the approach move null (already in range) and falls
/// through to the pressure chain. Fixture shape copied from <see cref="TankTakesHeldPackBeforeBloomTests"/>.
/// </summary>
public sealed class TankHoldsDefensiveWhileTakingPackTests
{
    private static readonly Bounds Hall = new(-5000, 25000, -6000, 6000);
    private static readonly Vec3 Warden = new(4000, 0, 0);
    private static readonly Vec3 Cinder = new(2000, 0, 1500);
    private const int WardenId = 1;
    private const int HolderId = 2;
    private static readonly int[] PackIds = { 150, 151, 152 };
    private static readonly string[] Defensives = { "warden.brace", "warden.vigil", "warden.bulwark" };

    private static Run Create()
    {
        var layout = new NavLayout(
            new[] { new NavBox("hall", Hall, true, null) },
            new[] { new NavTrigger("p1", new Bounds(20000, 24000, -5000, 5000)) },
            new[] { new Vec3(10000, 0, 0) });
        var dungeon = new DungeonDef("nav", new[]
        {
            new PullDef("p1", new[] { new PullSpawn(101, "dummy", new Vec3(22000, 0, 0), 0, 10) }),
        });
        var content = new RunContent
        {
            Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal),
            Effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal),
            LockoutTicks = 40,
            BaseSpeedMmPerTick = 180,
            ActorRadiusMm = 400,
            Bounds = layout.Extent,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
            Layout = layout,
        };
        var party = new[]
        {
            new PartyMemberSpec(WardenId, "warden", ActorRole.Tank, Warden, 400, 100),
            new PartyMemberSpec(HolderId, "cinder", ActorRole.Damage, Cinder, 250, 100),
        };
        return RunRoot.CreateRun(1, party, dungeon, content);
    }

    [Fact(DisplayName = "R-AGGRO-1 (ag): a healthy tank taking a loose pack in reach with the taunt on cooldown issues no defensive cast")]
    public void Healthy_tank_with_taunt_down_burns_no_defensive_on_a_loose_pack()
    {
        var state = Create().State;
        for (var i = 0; i < PackIds.Length; i++)
        {
            // In melee reach of the Warden (well inside reach less margin), held by seat 2, TargetId parked on the tank.
            var at = new Vec3(Warden.X + 600, 0, -600 + 600 * i);
            var enemy = Actors.Create(PackIds[i], ActorKind.Enemy, "test.brute", ActorRole.None, at, 1000, 0);
            enemy.Idle = false;
            enemy.HolderId = HolderId;
            enemy.TargetId = WardenId;
            state.Actors[PackIds[i]] = enemy;
        }
        var kits = KitRegistry.From(Keyrun.Content.ContentLoader.Load());
        var view = BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits);
        var self = view.State.Actors[WardenId];
        var kit = RoleShared.KitOf(view, self.ClassId);

        Assert.True(!string.IsNullOrEmpty(kit.TauntAbilityId), "precondition: the Warden kit has no taunt ability, so the fixture is wrong, not the rule");
        self.Cooldowns[kit.TauntAbilityId!] = view.State.Tick + 1000;

        Assert.True((double)self.Health / self.MaxHealth >= TankRole.DefensiveThresholdPct, "precondition: the Warden is under the defensive threshold");
        Assert.True(TankEngage.PickLooseEnemy(view.State.Actors, self) is not null, "precondition: no loose enemy, so the fixture is wrong, not the rule");
        Assert.True(!RoleShared.AbilityReady(self, kit.TauntAbilityId!, view.State.Tick, kit), "precondition: the taunt is still ready, so the fixture is wrong, not the rule");
        Assert.True(TankEngage.ApproachMove(view, self, kit) is null, "precondition: the approach move is not null (pack out of reach), so the fixture is wrong, not the rule");
        Assert.True(TankPressure.UnderPackPressure(view, self), "precondition: the pack is not read as pressure, so the fixture cannot reach the pressure chain");

        var casts = TankRole.TankDecide(view, self).OfType<CastCommand>().Where(c => Defensives.Contains(c.AbilityId)).Select(c => c.AbilityId).ToList();

        Assert.True(
            casts.Count == 0,
            $"a healthy Warden taking a loose pack with the taunt on cooldown must hold its defensives, but TankDecide issued [{string.Join(", ", casts)}]");
    }
}
