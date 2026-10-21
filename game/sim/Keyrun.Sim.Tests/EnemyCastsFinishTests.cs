using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// CAST-FINISH (BIBLE s3 "Cast bars are the enemy's language"; Matt: "lean into m+ design heavily", "it should feel
/// almost exact to wow"): as in WoW Mythic+, an enemy finishes the cast it starts. A scripted step that comes due while
/// its enemy is still casting waits and fires once that cast lands; only a player's interrupt or stun ends a cast early.
/// For every scripted enemy in content/data/enemies/*.json at keys 2, 10 and 20 (Tempo-scaled as the host scales them),
/// the enemy is stepped through the real <see cref="RunRoot.StepRun"/> path with an idle party (no kicks, no stuns), and
/// (1) every cast it starts lands: a <c>cast_complete</c> on its castEndsTick, never replaced mid-bar or on its start
/// tick; (2) every scheduled occurrence of every step still fires (a cast command for it, on its due tick or later).
/// Before the fix, the Pair's melee swing replaced spreadMark, the Warden's Lock and slam replaced its cleave, and the
/// Seed's slam replaced its lash at key 10.
/// </summary>
public sealed class EnemyCastsFinishTests
{
    private const int ActorId = 500;
    private const int Ticks = 3000;

    /// <summary>Every scripted enemy, at keys 2, 10 and 20.</summary>
    /// <returns>(file, enemy id, key) rows.</returns>
    public static IEnumerable<object[]> Cases()
    {
        foreach (var file in new[] { "seed.json", "pair.json", "choir.json", "hollow-warden.json" })
        {
            foreach (var enemy in Enemies(file).Where(e => e.Get("script") is JsArray))
            {
                foreach (var key in new[] { 2, 10, 20 })
                {
                    yield return new object[] { file, ((JsString)enemy.Get("id")!).Value, key };
                }
            }
        }
    }

    private static List<JsObject> Enemies(string file) =>
        ((JsArray)JsValue.Parse(File.ReadAllText(Path.Combine(
            ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", file)))).Items
            .Cast<JsObject>().ToList();

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    [Theory(DisplayName = "cast finish: every enemy cast that starts lands, and every scripted step still fires, every key")]
    [MemberData(nameof(Cases))]
    public void Every_started_cast_lands_and_every_step_fires(string file, string enemyId, int key)
    {
        var enemyDef = Enemies(file).Single(e => ((JsString)e.Get("id")!).Value == enemyId);
        var script = Tempo.ScaleScript(EncounterJson.ParseScript(enemyDef.Get("script")!), key);
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("solo", new[] { new PullSpawn(ActorId, enemyId, new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [ActorId] = script },
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(BossTestKit.Warden, "warden", ActorRole.Tank, 0, 1000, BossTestKit.PartyHealth, 100),
            RunTestKit.Member(BossTestKit.Chanter, "chanter", ActorRole.Healer, 500, 1000, BossTestKit.PartyHealth, 1),
            RunTestKit.Member(BossTestKit.Cinder, "cinder", ActorRole.Damage, -500, 1000, BossTestKit.PartyHealth, 100),
        };
        var run = RunRoot.CreateRun(BossTestKit.Seed, party, dungeon, content);
        var views = RunWiring.ScriptAbilitiesOf(run.Content.Abilities);

        // Every cast command the enemy's scripts hand combat (the same pure call StepRun makes), whether combat would
        // start it on that tick's pre-combat state, and every cast that completed.
        var commands = new List<(int Tick, string Cast, bool Starts)>();
        var completes = new HashSet<(string Cast, int Tick)>();
        var interrupted = new HashSet<int>();
        while (run.State.Tick < Ticks)
        {
            var pre = run.State;
            var scripted = Scripts.StepEnemyScripts(pre, run.ActiveScripts ?? run.Content.EnemyScripts, run.Encounter.FiredOnDeath, views, run.Encounter.PendingSteps);
            foreach (var c in scripted.Commands.OfType<CastCommand>().Where(c => c.ActorId == ActorId))
            {
                var def = run.Content.Abilities[c.AbilityId];
                commands.Add((pre.Tick, c.AbilityId, Casting.StartError(pre, pre.Actors[ActorId], def, c.TargetId, pre.Tick) is null));
            }
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            foreach (var e in stepped.Events.Where(e => Int(e, "actorId") == ActorId))
            {
                if (e.Type == "cast_complete")
                {
                    completes.Add((Str(e, "abilityId")!, e.Tick));
                }
                else if (e.Type is "interrupt" or "cast_stopped")
                {
                    interrupted.Add(e.Tick);
                }
            }
        }
        Assert.Empty(interrupted); // the party is idle: nothing but the enemy's own scripts can end its casts

        // (1) No started cast is cut short: every bar the enemy starts completes on its castEndsTick.
        var cut = new List<string>();
        foreach (var (tick, cast, starts) in commands.Where(c => c.Starts))
        {
            var castTicks = run.Content.Abilities[cast].CastTicks;
            if (castTicks > 0 && tick + castTicks < Ticks && !completes.Contains((cast, tick + castTicks)))
            {
                var by = commands.Where(c => c.Tick >= tick && c.Tick < tick + castTicks && c.Starts && !(c.Tick == tick && c.Cast == cast))
                    .Select(c => $"{c.Cast}@{c.Tick}");
                cut.Add($"{cast} started {tick}, due to land {tick + castTicks}, never landed (other casts: {string.Join(", ", by)})");
            }
        }

        // (2) No step is dropped: every scheduled occurrence is matched, in order, by a cast command on its due tick or
        // later, delayed at most by every other cast of the script running back to back.
        var bound = script.Sum(s => run.Content.Abilities.TryGetValue(s.Cast, out var d) ? d.CastTicks : 0) + 1;
        var dropped = new List<string>();
        var dueCount = 0;
        foreach (var step in script.Where(s => s.Trigger != StepTrigger.OnDeath))
        {
            var fired = commands.Where(c => c.Cast == step.Cast).Select(c => c.Tick).ToList();
            var next = 0;
            for (var d = 1; d < Ticks - bound; d++)
            {
                if (!Scripts.FiresAt(step, d))
                {
                    continue;
                }
                dueCount++;
                while (next < fired.Count && fired[next] < d)
                {
                    next++;
                }
                if (next < fired.Count && fired[next] - d <= bound)
                {
                    next++;
                }
                else
                {
                    dropped.Add($"{step.Cast} due {d} never fired (next fire {(next < fired.Count ? fired[next] : -1)})");
                }
            }
        }

        Assert.True(dueCount > 0, $"{enemyId} at key {key}: no step came due in {Ticks} ticks");
        Assert.True(cut.Count == 0, $"{enemyId} at key {key}: {cut.Count} of {commands.Count(c => c.Starts)} started casts cut short:\n" + string.Join("\n", cut.Take(10)));
        Assert.True(dropped.Count == 0, $"{enemyId} at key {key}: {dropped.Count} of {dueCount} scheduled steps dropped:\n" + string.Join("\n", dropped.Take(10)));
    }
}
