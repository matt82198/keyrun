using System.Collections.Generic;
using System.Linq;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-DECLUTTER-1 (ruling on PR #528; BIBLE s3, "cast bars are the enemy's language"): a retarget mid-cast must leave the
/// "HUD unitframe cast" proof and the "HUD castbar" placement describing one moment. The frame draws the cast while the
/// human targets the caster, and the over-head bar draws it after the human retargets away. Retargeting to a patrol mid-cast
/// is correct WoW tank behaviour; the proof must follow it, not suppress it.
/// </summary>
public sealed class CastProofTests
{
    private const int Human = 1;
    private const int Caster = 107;
    private const int Other = 105;
    private const string Spread = "pair.spreadMark";

    private static bool NotInterruptible(string _) => false;

    /// <summary>The state after a step in which the human targets <paramref name="target"/> and actor 107 is mid-cast.</summary>
    private static SimState StateTargeting(int? target)
    {
        var s = SimState.Create(1);
        s.Actors[Human] = new ActorState { Id = Human, Kind = ActorKind.Player, ClassId = "warden", TargetId = target };
        s.Actors[Caster] = new ActorState
        {
            Id = Caster, Kind = ActorKind.Enemy, ClassId = "pair.melee", CastId = Spread, CastStartTick = 100, CastEndsTick = 140,
        };
        s.Actors[Other] = new ActorState { Id = Other, Kind = ActorKind.Enemy, ClassId = "pair.melee" };
        return s;
    }

    /// <summary>Every "HUD unitframe cast" line in a step names an actor whose castbar line in the same step is where=frame.</summary>
    private static void AssertFrameLinesMatchCastbars(IReadOnlyList<string> lines)
    {
        foreach (var frame in lines.Where(l => l.StartsWith("HUD unitframe cast ", System.StringComparison.Ordinal)))
        {
            var actor = frame.Split(' ').First(p => p.StartsWith("actor=", System.StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith($"HUD castbar {actor} kind=enemy where=frame ", System.StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "Cast proof (R-DECLUTTER-1): a target switch mid-cast moves the caster between the unit frame and over the head, and the two proof lines describe the same moment")]
    public void Target_switch_mid_cast_keeps_the_frame_and_castbar_proof_in_step()
    {
        var log = new CastProofLog();

        // Before the retarget: the human targets nobody, so the caster's bar is over its head and no frame line is printed.
        var before = log.Observe(StateTargeting(null), Human, NotInterruptible);
        Assert.Equal(new[] { $"HUD castbar actor={Caster} kind=enemy where=overhead ability={Spread}" }, before);

        // Mid-cast the human retargets to the caster: the frame draws the cast, and both proof lines are printed together.
        var targeted = log.Observe(StateTargeting(Caster), Human, NotInterruptible);
        Assert.Contains($"HUD castbar actor={Caster} kind=enemy where=frame ability={Spread}", targeted);
        Assert.Contains($"HUD unitframe cast kind=target actor={Caster} ability={Spread} interruptible=0", targeted);
        AssertFrameLinesMatchCastbars(targeted);

        // The same state again is no news: nothing is printed twice.
        Assert.Empty(log.Observe(StateTargeting(Caster), Human, NotInterruptible));

        // The human retargets away mid-cast (to a living enemy): the bar goes back over the caster's head, and no frame line
        // is printed for it. The frame line is never left describing a cast the frame no longer draws.
        var away = log.Observe(StateTargeting(Other), Human, NotInterruptible);
        Assert.Equal(new[] { $"HUD castbar actor={Caster} kind=enemy where=overhead ability={Spread}" }, away);
        Assert.DoesNotContain(away, l => l.StartsWith("HUD unitframe cast ", System.StringComparison.Ordinal));
    }
}
