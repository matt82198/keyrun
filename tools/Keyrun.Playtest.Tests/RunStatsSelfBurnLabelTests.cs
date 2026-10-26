using System.Collections.Generic;
using System.Linq;
using ContentLoader = Keyrun.Content.ContentLoader;
using ContentRegistry = Keyrun.Content.ContentRegistry;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Playtest;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// A Cinderfield closer burn is the payoff actor burning itself: the damage event's source and target are both seat 1
/// (Combos.cs BurnCloser, one "damage" event, source and target the closer). RunStats must still label a seat's death
/// by that burn with the combo that caused it (cinderfield-field), never "unknown", and must count the burn in
/// DamageTakenByAbility. The events are built from the real event types and carry the real combo view.
/// </summary>
public sealed class RunStatsSelfBurnLabelTests
{
    private const int Tick = 1702;
    private const int Seat = 1;
    private const int Setup = 2;
    private const int Burn = 100;

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    [Fact(DisplayName = "A seat killed by its own Cinderfield closer burn is labelled by the combo, not unknown, and the burn is counted")]
    public void Self_burn_death_is_labelled_by_its_combo_and_counted()
    {
        var setup = HostRunSetup.Build(Registry, "hollow-vault", 2, HostRunSetup.Roster[0].ClassId, withBots: true);
        var run = RunRoot.CreateRun(1, setup.Party, setup.Dungeon, setup.Content);
        var stats = new RunStats(Registry, setup.Party);

        var combo = new SimEvent(
            Tick,
            "combo",
            ("comboId", JsValue.Str("cinderfield-field")),
            ("windowId", JsValue.Str("cinderfield")),
            ("setupActorId", JsValue.Int(Setup)),
            ("setupClass", JsValue.Str("cinder")),
            ("payoffActorId", JsValue.Int(Seat)),
            ("payoffClass", JsValue.Str("warden")));
        Assert.True(SimEventViews.TryCombo(combo, out _));

        var burn = new SimEvent(
            Tick,
            "damage",
            ("sourceId", JsValue.Int(Seat)),
            ("targetId", JsValue.Int(Seat)),
            ("healthDamage", JsValue.Int(Burn)));
        var death = new SimEvent(
            Tick,
            "death",
            ("actorId", JsValue.Int(Seat)),
            ("sourceId", JsValue.Int(Seat)));

        stats.ObserveStep(run, new List<SimEvent> { combo, burn, death }, "pair");

        var row = Assert.Single(stats.Deaths);
        Assert.Equal(Seat, row.ActorId);
        Assert.NotEqual("unknown", row.Cause);
        Assert.Contains("cinderfield-field", row.Cause);

        var total = stats.DamageTakenByAbility.Where(kv => kv.Key.Contains("cinderfield-field")).Sum(kv => kv.Value);
        Assert.Equal(Burn, total);
    }
}
