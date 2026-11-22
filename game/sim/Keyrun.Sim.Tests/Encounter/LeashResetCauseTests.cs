using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// f-ef77ad17 (P0): the <c>pack_reset</c> cause is the label of the reset as it stands, not the cause the group evaded
/// with. A group that evades with no living seat must not reset labelled <c>no-living-seat</c> once a seat has respawned
/// (Leash.cs carried the evade cause onto the reset). Drives <see cref="Leash.Step"/> directly over hand-built states so
/// the seat's death and respawn land on exact ticks.
/// </summary>
public sealed class LeashResetCauseTests
{
    private const int Seat = 1;
    private const int Enemy = 101;
    private const string Group = "g";
    private const long LeashMm = 1000;

    private static AggroGroup Group1() => new(Group, 0, new[] { Enemy }, new Vec3(0, 0, 0), 6000, System.Array.Empty<string>());

    private static EncounterState Engaged() => EncounterTestKit.BaseEncounter() with { Engaged = new[] { Group } };

    private static ActorState Seat1(bool alive, Vec3 pos) =>
        EncounterTestKit.Actor(Seat, ActorKind.Player, pos, alive, health: 100);

    private static ActorState Boss(Vec3 pos)
    {
        var boss = EncounterTestKit.Actor(Enemy, ActorKind.Enemy, pos, alive: true, health: 1000);
        boss.Idle = false;
        boss.Home = new Vec3(0, 0, 0);
        boss.LeashMm = LeashMm;
        return boss;
    }

    private static string? ResetCause(List<SimEvent> events) =>
        events.Find(e => e.Type == "pack_reset") is { } reset && reset.Get("cause") is JsString { Value: var v } ? v : null;

    /// <summary>
    /// Tick 1: the boss is dragged 5000 mm off home with no living seat, so the group evades with no-living-seat. Tick 2:
    /// a seat is alive again and the boss has walked home, so the group resets. The reset is labelled by the seats alive at
    /// reset time, which is the leash the seat-less evade fell back to: it must not carry the stale no-living-seat label.
    /// </summary>
    [Fact(DisplayName = "f-ef77ad17: a group that evades with no living seat does not reset no-living-seat once a seat respawns")]
    public void Reset_after_seat_respawn_is_not_labelled_no_living_seat()
    {
        var emitted = new List<SimEvent>();
        var evadeState = EncounterTestKit.State(1, Seat1(alive: false, new Vec3(36000, 0, 0)), Boss(new Vec3(5000, 0, 0)));
        var encounter = Leash.Step(evadeState, Engaged(), new[] { Group1() }, 200, EncounterTestKit.Sink(emitted));
        var evade = emitted.Find(e => e.Type == "pack_evade");
        Assert.True(evade is not null && evade.Get("cause") is JsString { Value: Leash.NoLivingSeatCause }, "f-ef77ad17 setup: the evade must start with no-living-seat");

        emitted.Clear();
        var resetState = EncounterTestKit.State(2, Seat1(alive: true, new Vec3(36000, 0, 0)), Boss(new Vec3(0, 0, 0)));
        resetState.Actors[Enemy].Evading = true;
        resetState.Actors[Enemy].EvadeCause = Leash.NoLivingSeatCause;
        Leash.Step(resetState, encounter, new[] { Group1() }, 200, EncounterTestKit.Sink(emitted));

        var cause = ResetCause(emitted);
        Assert.True(cause is not null, "f-ef77ad17: no pack_reset fired when the boss reached home");
        Assert.NotEqual(Leash.NoLivingSeatCause, cause);
    }

    [Fact(DisplayName = "f-ef77ad17 control: a group that evades with no living seat still resets no-living-seat while no seat is alive")]
    public void Reset_with_no_living_seat_stays_no_living_seat()
    {
        var emitted = new List<SimEvent>();
        var resetState = EncounterTestKit.State(2, Seat1(alive: false, new Vec3(36000, 0, 0)), Boss(new Vec3(0, 0, 0)));
        resetState.Actors[Enemy].Evading = true;
        resetState.Actors[Enemy].EvadeCause = Leash.NoLivingSeatCause;
        Leash.Step(resetState, Engaged(), new[] { Group1() }, 200, EncounterTestKit.Sink(emitted));

        Assert.Equal(Leash.NoLivingSeatCause, ResetCause(emitted));
    }
}
