using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Builders for the lane 3F boss tests (Sundered Twins, the shared Break bar, the Lock). Abilities, effects and combos
/// are the hollow-vault content exactly as the oracle setup holds it (ORACLE.md section 4); the boss script is the
/// setup's Tempo-scaled <c>hollowWarden.boss</c> script (actor 900, its <c>hollowWarden.lock</c> loop included, so the
/// tests prove the armed rules suppress it). The Twins and their bar mirror content/data/enemies/hollow-warden.json.
/// Everything runs through <see cref="RunRoot.CreateRun"/> and <see cref="RunRoot.StepRun"/> only.
/// </summary>
internal static class BossTestKit
{
    public const long Seed = 31337;
    public const int Warden = 1;
    public const int Chanter = 2;
    public const int Cinder = 3;
    public const int Boss = 900;
    public const int TwinA = 901;
    public const int TwinB = 902;

    /// <summary>Party health high enough that nobody dies across three Locks (the rules under test, not the wipe).</summary>
    public const long PartyHealth = 100_000_000;

    /// <summary>The Twins' script (hollow-warden.json: <c>{at:"loop", every:60, cast:"twins.slam"}</c>).</summary>
    public static IReadOnlyList<ScriptStep> TwinScript() =>
        EncounterJson.ParseScript(JsValue.Parse("[{\"at\":\"loop\",\"every\":60,\"cast\":\"twins.slam\"}]"));

    /// <summary>The armed Hollow Warden: Twins flanking the boss, the twinsBreak bar, BIBLE s9 Lock defaults.</summary>
    public static BossConfig Config() => new(
        Boss,
        new[]
        {
            new BossTwinSpec(TwinA, "hollowWarden.twinA", 6000, new Vec3(2000, 0, 0), TwinScript()),
            new BossTwinSpec(TwinB, "hollowWarden.twinB", 6000, new Vec3(-2000, 0, 0), TwinScript()),
        },
        new BreakBarSource("twinsBreak", new[] { TwinA, TwinB }, 100, 34, 20, new[] { new BreakBarOnEmptyEffect("stunned", 100, 1) }));

    /// <summary>A boss-only run at the given boss health (max 60000), armed with <see cref="Config"/> unless told not to.</summary>
    public static Run BuildRun(long bossHealth = 60000, bool armed = true, long seed = Seed)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("boss", new[] { new PullSpawn(Boss, "hollowWarden.boss", new Vec3(0, 0, 0), 512, 60000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = setup.Content.EnemyScripts[Boss] },
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(Warden, "warden", ActorRole.Tank, 0, 1500, PartyHealth, 100),
            RunTestKit.Member(Chanter, "chanter", ActorRole.Healer, 500, 2000, PartyHealth, 1),
            RunTestKit.Member(Cinder, "cinder", ActorRole.Damage, -500, 2000, PartyHealth, 100),
        };
        var run = RunRoot.CreateRun(seed, party, dungeon, content);
        run.State.Actors[Boss].Health = bossHealth;
        return armed ? run with { Encounter = BossRules.Arm(run.Encounter, Config()) } : run;
    }

    /// <summary>Steps until <paramref name="untilTick"/>, feeding each tick its commands; returns every event.</summary>
    public static (Run Run, List<SimEvent> Events) StepUntil(Run run, int untilTick, IReadOnlyList<Command>? commands = null) =>
        RunTestKit.StepUntil(run, untilTick, commands);

    public static IEnumerable<SimEvent> OfType(this IEnumerable<SimEvent> events, string type) => events.Where(e => e.Type == type);

    public static long IntField(this SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    public static bool HasEffect(this ActorState actor, string effectId, int tick) =>
        actor.Effects.Any(e => e.EffectId == effectId && e.EndsTick > tick);
}
