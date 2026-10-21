using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// BIBLE section 3: "a fixed tiebreak, never arrival order: a race is never a coin flip". Two of one enemy's cast-time
/// steps due on the same tick (the Hollow Warden's slam and lock, both on 300-tick multiples) resolve in one fixed
/// order on the frozen-TS replay path (<see cref="ScriptStep.TsCastReplace"/>, <see cref="CastDeferral"/>), the order
/// sim/encounter/scripts.ts and deferred-casts.ts record into the oracle traces and that
/// tests/sim/encounter/scripts.test.ts pins on the TS side: casts already held go first, oldest first; then the tick's
/// own steps in script order; the first cast-time cast claims the caster and every later one is held, to be issued
/// (its telegraph re-drawn) the first tick the caster is free (finding f-df833cf3).
/// </summary>
public sealed class SameTickCastTiebreakTests
{
    private const int Boss = 900;
    private const string Slam = "boss.slam";
    private const string Lock = "boss.lock";

    private static readonly IReadOnlyDictionary<string, ScriptAbility> Abilities = new Dictionary<string, ScriptAbility>(StringComparer.Ordinal)
    {
        [Slam] = new ScriptAbility(Slam, 0, "circle", false) { CastTicks = 10 },
        [Lock] = new ScriptAbility(Lock, 0, "self", false) { CastTicks = 20 },
    };

    private static ScriptStep Step(int every, string cast) => ScriptStep.Loop(every, cast) with
    {
        Telegraph = new TelegraphSpec("circle", 10) { Radius = 3000, TsUnchecked = true },
        TsCastReplace = true,
    };

    private static SimState StateAt(int tick) => EncounterTestKit.State(tick, EncounterTestKit.Actor(Boss, ActorKind.Enemy));

    private static string[] Casts(StepEnemyScriptsResult r) => r.Commands.OfType<CastCommand>().Select(c => c.AbilityId).ToArray();

    [Fact(DisplayName = "same-tick casts: the first in script order starts, the later one is held (warden order: slam, lock)")]
    public void The_first_step_in_script_order_starts_and_the_later_one_is_held()
    {
        var scripts = EncounterTestKit.Scripts(Boss, Step(300, Slam), Step(600, Lock));
        var r = Scripts.StepEnemyScripts(StateAt(600), scripts, abilities: Abilities);
        Assert.Equal(new[] { Slam }, Casts(r));
        Assert.Equal(new[] { Lock }, r.DeferredCasts.Select(d => d.Step.Cast).ToArray());
    }

    [Fact(DisplayName = "same-tick casts: the order is the script's, not the abilities' (reversed script, lock starts)")]
    public void Reversing_the_script_reverses_the_winner()
    {
        var scripts = EncounterTestKit.Scripts(Boss, Step(600, Lock), Step(300, Slam));
        var r = Scripts.StepEnemyScripts(StateAt(600), scripts, abilities: Abilities);
        Assert.Equal(new[] { Lock }, Casts(r));
        Assert.Equal(new[] { Slam }, r.DeferredCasts.Select(d => d.Step.Cast).ToArray());
    }

    [Fact(DisplayName = "same-tick casts: the held cast starts on the tick the winner lands, ahead of a step due that tick, and re-draws its telegraph")]
    public void The_held_cast_goes_first_on_the_free_tick()
    {
        var scripts = EncounterTestKit.Scripts(Boss, Step(300, Slam), Step(600, Lock), Step(610, Slam));
        var first = Scripts.StepEnemyScripts(StateAt(600), scripts, abilities: Abilities);
        Assert.Equal(new[] { Slam }, Casts(first));

        var busy = StateAt(605);
        busy.Actors[Boss].CastId = Slam;
        busy.Actors[Boss].CastEndsTick = 610;
        var mid = Scripts.StepEnemyScripts(busy, scripts, abilities: Abilities, deferredCasts: first.DeferredCasts);
        Assert.Empty(Casts(mid));
        Assert.Equal(new[] { Lock }, mid.DeferredCasts.Select(d => d.Step.Cast).ToArray());

        var landing = StateAt(610);
        landing.Actors[Boss].CastId = Slam;
        landing.Actors[Boss].CastEndsTick = 610;
        var free = Scripts.StepEnemyScripts(landing, scripts, abilities: Abilities, deferredCasts: mid.DeferredCasts);
        Assert.Equal(new[] { Lock }, Casts(free));
        Assert.Equal(new[] { Slam }, free.DeferredCasts.Select(d => d.Step.Cast).ToArray());
        var redraw = Assert.Single(free.TelegraphEvents, e => ((JsString)e.Get("cast")!).Value == Lock);
        Assert.Equal(610, redraw.Tick);
    }
}
