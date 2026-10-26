using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// WoW M+ tank behaviour (S1 spec, finding f-892781c7, PURPOSE: pin that a bot tank which already holds its own mob
/// with a solid lead still goes to a loose mob that is hitting a squishy). Board is the chanter tutorial (key 2, seed 1)
/// trace at tick 712, taken from C:/Users/Mattt/AppData/Local/Temp/claude/C--Users-Mattt-aesop/0198324a-b702-41b5-882f-d2628aac95cc/scratchpad/lanes/s5-chanter-tank-ticks.txt:
/// the Warden (seat 2) holds mob 101 (Threat 2600 vs the Cinder's 320) and is retargeting it, while mob 102 sits on the
/// Cinder (seat 3, holder and hitting) inside the 3000 mm R-TAUNT-3 row 1 reach (HurtsItsSeat), with the Warden about
/// 1270 mm away and Threat 740 vs 1300. Mob 101 is placed OUTSIDE that reach (about 3335 mm from the Cinder), so it is
/// not hurting its seat and row 1 sorts 102 first. The taunt is on cooldown until 775. The chanter (seat 1) is
/// dead-or-far and is omitted from the board; the Cinder and both mobs are kept.
/// The main case steps the real TankDecide for 80 ticks and requires the Warden to target 102 or become its holder. The
/// control removes mob 102 entirely and requires the Warden to keep 101, so the stepping loop cannot pass vacuously.
/// </summary>
public sealed class BotTankTakesLooseMobTests
{
    private const int StartTick = 712;
    private const int WindowTicks = 80;

    // Per-tick movement step in mm for the Warden. Approximate: the trace moves the Warden ~160 mm per tick.
    private const long StepMm = 160;

    private const int WardenId = 2;
    private const int CinderId = 3;
    private const int LooseMobId = 101;
    private const int VictimMobId = 102;

    private static ActorState Warden(int targetId) =>
        Actor(
            id: WardenId,
            classId: "warden",
            role: ActorRole.Tank,
            x: 17887,
            z: -570,
            health: 310,
            maxHealth: 400,
            maxResource: 100,
            targetId: targetId,
            cooldowns: new[]
            {
                ("warden.taunt", 775),
                ("warden.brace", 639),
                ("warden.vigil", 471),
                ("warden.bulwark", 611),
                ("warden.shieldBash", 1189),
            },
            threat: Array.Empty<(int, long)>());

    // The Cinder: hitting mob 102 from 2600 mm, inside DefaultReachMm 3000 (trace pos 15753,616, hp 110/250).
    private static ActorState Cinder() =>
        Actor(
            id: CinderId,
            classId: "cinder",
            role: ActorRole.Damage,
            x: 15753,
            z: 616,
            health: 110,
            maxHealth: 250,
            maxResource: 100,
            targetId: VictimMobId);

    // Mob 101: held by the Cinder (HolderId 3), the Warden's own mob with threat 2600 vs the Cinder's 320. Placed at
    // (15407,-2700): about 3335 mm from the Cinder, OUTSIDE DefaultReachMm 3000, so it is not hurting its seat (R-TAUNT-3 row 1).
    private static ActorState LooseMob()
    {
        var mob = Actor(
            id: LooseMobId,
            kind: ActorKind.Enemy,
            classId: "choir.melee",
            role: ActorRole.None,
            x: 15407,
            z: -2700,
            health: 3620,
            maxHealth: 6180,
            maxResource: 100,
            targetId: CinderId,
            threat: new[] { (WardenId, 2600L), (CinderId, 320L) });
        mob.HolderId = CinderId;
        return mob;
    }

    // Mob 102: on the Cinder (HolderId 3), hitting it from (18353,616): 2600 mm from the Cinder, INSIDE DefaultReachMm 3000,
    // so it is the loose mob hurting its seat (R-TAUNT-3 row 1). The Warden is ~1270 mm away with threat 740 vs the Cinder's 1300.
    private static ActorState VictimMob()
    {
        var mob = Actor(
            id: VictimMobId,
            kind: ActorKind.Enemy,
            classId: "choir.melee",
            role: ActorRole.None,
            x: 18353,
            z: 616,
            health: 4090,
            maxHealth: 6180,
            maxResource: 100,
            targetId: CinderId,
            threat: new[] { (WardenId, 740L), (CinderId, 1300L) });
        mob.HolderId = CinderId;
        return mob;
    }

    /// <summary>
    /// Steps the real <see cref="TankRole.TankDecide"/> for <see cref="WindowTicks"/> ticks from <see cref="StartTick"/>.
    /// Each tick's TargetCommand sets the Warden's target and each MoveCommand advances its position by <see cref="StepMm"/>;
    /// everything else on the board (mob holders, threat, Cinder) is held fixed. Returns the Warden at the end, the tick
    /// of the first tick it targeted or held mob 102 (or null), and the ids of every target it chose.
    /// </summary>
    private static (ActorState Warden, int? FirstVictimTick, List<int> Targets) StepWarden(bool includeVictimMob, int startTarget)
    {
        var warden = Warden(startTarget);
        var cinder = Cinder();
        var loose = LooseMob();
        var victim = VictimMob();
        var actors = includeVictimMob ? new[] { warden, cinder, loose, victim } : new[] { warden, cinder, loose };
        int? firstVictimTick = null;
        var targets = new List<int>();

        for (var tick = StartTick; tick < StartTick + WindowTicks; tick++)
        {
            var view = ViewOf(tick, RealKits, null, actors);
            var cmds = TankRole.TankDecide(view, warden).ToList();

            foreach (var target in cmds.OfType<TargetCommand>())
            {
                warden.TargetId = target.TargetId;
                targets.Add(target.TargetId ?? -1);
            }
            var move = BotTestKit.FirstMove(cmds);
            BotTestKit.SetPos(warden, BotTestKit.ApplyMove(PointMm.Of(warden.Pos), move, StepMm));

            var victimHeld = includeVictimMob && victim.HolderId == WardenId;
            if (firstVictimTick is null && (warden.TargetId == VictimMobId || victimHeld))
            {
                firstVictimTick = tick;
            }
        }
        return (warden, firstVictimTick, targets);
    }

    [Fact(DisplayName = "Bot tank takes loose mob > holding a led mob with taunt on cooldown: the Warden targets or holds mob 102 within 80 ticks")]
    public void WardenTakesLooseMobWithinWindow()
    {
        // At tick 712 the Warden is still targeting 102 on the trace and retargets 101 on that tick (TargetCommand 101).
        var (warden, firstVictimTick, targets) = StepWarden(includeVictimMob: true, startTarget: VictimMobId);

        Assert.True(
            firstVictimTick is not null,
            $"expected the Warden to target or hold mob {VictimMobId} within {WindowTicks} ticks from {StartTick}; "
            + $"final target {warden.TargetId}, targets chosen [{string.Join(", ", targets)}]");
    }

    [Fact(DisplayName = "Bot tank takes loose mob > control: with mob 102 removed, the Warden keeps mob 101")]
    public void ControlWithoutVictimMobKeepsLooseMob()
    {
        var (warden, firstVictimTick, targets) = StepWarden(includeVictimMob: false, startTarget: LooseMobId);

        Assert.Null(firstVictimTick);
        Assert.Equal(LooseMobId, warden.TargetId);
        Assert.DoesNotContain(VictimMobId, targets);
    }
}
