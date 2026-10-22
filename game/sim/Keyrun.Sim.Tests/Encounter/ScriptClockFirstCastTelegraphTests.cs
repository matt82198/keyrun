using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// R-BOSS-5 (c) and BIBLE s3 "every dangerous enemy action is a cast with a visible bar and a floor telegraph drawn
/// ahead of resolution" (finding f-0431d7f5). A scripted cast whose telegraph is NOT a spansCast shape draws its
/// telegraph LeadTicks before the fire tick. When the boss exists only from a pull that starts inside that window
/// (the draw tick falls before the pull), the draw is skipped and the cast on the fire tick still issues, so it lands
/// with no telegraph of its own. A boss's first scripted cast must always telegraph before it resolves.
/// </summary>
public sealed class ScriptClockFirstCastTelegraphTests
{
    private const int Boss = 900;
    private const string Slam = "boss.slam";
    private const int Period = 294;
    private const int Lead = 10;

    private static readonly IReadOnlyDictionary<string, ScriptAbility> Abilities = new Dictionary<string, ScriptAbility>(StringComparer.Ordinal)
    {
        [Slam] = new ScriptAbility(Slam, 0, "circle", false) { CastTicks = 10 },
    };

    /// <summary>
    /// A loop slam with a plain (not spansCast) floor telegraph LeadTicks ahead of its fire tick, on the frozen-TS cast
    /// path a boss's steps take (TsCastReplace, RunWiring.cs), as in the hollow-vault k2 boss.
    /// </summary>
    private static readonly ScriptStep SlamStep = ScriptStep.Loop(Period, Slam) with
    {
        TsCastReplace = true,
        Telegraph = new TelegraphSpec("circle", Lead) { Radius = 3000 },
    };

    /// <summary>
    /// Runs the boss script over ticks 1150..1500 with the boss present only from <paramref name="spawnTick"/>, and
    /// returns the casts issued and the telegraphs drawn (tick, cast).
    /// </summary>
    private static (List<(int Tick, string Cast)> Casts, List<(int Tick, string Cast)> Telegraphs) Drive(int spawnTick)
    {
        var scripts = EncounterTestKit.Scripts(Boss, SlamStep);
        var casts = new List<(int, string)>();
        var telegraphs = new List<(int, string)>();
        IReadOnlyDictionary<int, int> starts = new SortedDictionary<int, int>();
        for (var t = 1150; t <= 1500; t++)
        {
            var state = t >= spawnTick
                ? EncounterTestKit.State(t, EncounterTestKit.Actor(Boss, ActorKind.Enemy))
                : EncounterTestKit.State(t);
            var r = Scripts.StepEnemyScripts(state, scripts, abilities: Abilities, scriptStarts: starts);
            starts = r.ScriptStarts;
            casts.AddRange(r.Commands.OfType<CastCommand>().Select(c => (t, c.AbilityId)));
            telegraphs.AddRange(r.TelegraphEvents.Select(e => (e.Tick, ((JsString)e.Get("cast")!).Value)));
        }
        return (casts, telegraphs);
    }

    [Theory(DisplayName = "scripted clock: a non-spans slam is never cast before its own telegraph is drawn, whatever tick the boss spawns on")]
    [InlineData(1170)]
    [InlineData(1173)]
    [InlineData(1251)]
    public void No_plain_telegraph_cast_starts_without_an_earlier_telegraph_of_its_own(int spawnTick)
    {
        var (casts, telegraphs) = Drive(spawnTick);
        Assert.NotEmpty(casts);
        var unannounced = casts
            .Select(c => (Land: c.Tick + Abilities[c.Cast].CastTicks, c.Cast, c.Tick))
            .Where(c => !telegraphs.Any(g => g.Item2 == c.Cast && g.Item1 < c.Land))
            .Select(c => $"{c.Cast} started at tick {c.Tick} and lands at {c.Land} (boss spawned at {spawnTick}) with no earlier telegraph")
            .ToList();
        Assert.True(unannounced.Count == 0, string.Join("; ", unannounced));
    }
}
