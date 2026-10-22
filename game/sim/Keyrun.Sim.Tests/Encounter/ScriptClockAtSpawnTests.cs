using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// R-BOSS-5 / BIBLE section 3 "every dangerous enemy action is a cast with a visible bar and a floor telegraph drawn
/// ahead of resolution" (finding f-0431d7f5). A flat run's scripted boss is absent from the state until its pull spawns,
/// and its <c>every</c> clock is absolute. When the spawn lands inside a slam's lead window (the fire tick minus the
/// lead, up to the fire tick), the draw for that slam falls on a tick before the boss exists and is skipped, yet the
/// cast on the fire tick still starts: a slam completes with no telegraph of its own. Every cast issued on a tick must
/// have a telegraph of the same actor and cast drawn on an earlier tick.
/// </summary>
public sealed class ScriptClockAtSpawnTests
{
    private const int Boss = 900;
    private const string Slam = "boss.slam";
    private const int Period = 294;
    private const int Lead = 10;

    private static readonly IReadOnlyDictionary<string, ScriptAbility> Abilities = new Dictionary<string, ScriptAbility>(StringComparer.Ordinal)
    {
        [Slam] = new ScriptAbility(Slam, 0, "circle", false) { CastTicks = 10 },
    };

    private static readonly ScriptStep SlamStep = ScriptStep.Loop(Period, Slam) with
    {
        Telegraph = new TelegraphSpec("circle", Lead) { Radius = 3000, SpansCast = true },
    };

    /// <summary>
    /// Runs the boss script over ticks 1150..1300 with the boss present only from <paramref name="spawnTick"/>, and
    /// returns the casts issued and the telegraphs drawn (tick, actor, cast).
    /// </summary>
    private static (List<(int Tick, string Cast)> Casts, List<(int Tick, string Cast)> Telegraphs) Drive(int spawnTick)
    {
        var scripts = EncounterTestKit.Scripts(Boss, SlamStep);
        var casts = new List<(int, string)>();
        var telegraphs = new List<(int, string)>();
        for (var t = 1150; t <= 1500; t++)
        {
            var state = t >= spawnTick
                ? EncounterTestKit.State(t, EncounterTestKit.Actor(Boss, ActorKind.Enemy))
                : EncounterTestKit.State(t);
            var r = Scripts.StepEnemyScripts(state, scripts, abilities: Abilities);
            casts.AddRange(r.Commands.OfType<CastCommand>().Select(c => (t, c.AbilityId)));
            telegraphs.AddRange(r.TelegraphEvents.Select(e => (e.Tick, ((JsString)e.Get("cast")!).Value)));
        }
        return (casts, telegraphs);
    }

    [Theory(DisplayName = "scripted clock: no slam is cast without an earlier telegraph of its own, whatever tick the boss spawns on")]
    [InlineData(1170)]
    [InlineData(1173)]
    [InlineData(1251)]
    public void No_cast_starts_without_an_earlier_telegraph_of_its_own(int spawnTick)
    {
        var (casts, telegraphs) = Drive(spawnTick);
        Assert.NotEmpty(casts);
        // The property is on the resolution: a cast_complete lands at its start plus castTicks, and needs a telegraph drawn
        // strictly before that landing tick.
        var unannounced = casts
            .Select(c => (Land: c.Tick + Abilities[c.Cast].CastTicks, c.Cast, c.Tick))
            .Where(c => !telegraphs.Any(g => g.Item2 == c.Cast && g.Item1 < c.Land))
            .Select(c => $"{c.Cast} started at tick {c.Tick} and lands at {c.Land} (boss spawned at {spawnTick}) with no earlier telegraph")
            .ToList();
        Assert.True(unannounced.Count == 0, string.Join("; ", unannounced));
    }
}
