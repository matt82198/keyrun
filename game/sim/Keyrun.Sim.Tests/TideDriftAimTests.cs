using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE.md:156 "*Tide* (floor patches drift)" and content/data/clauses.json tide: under Tide, every pending telegraph's
/// origin moves +x by driftMmPerTick each tick (Clauses.DriftTelegraphOrigin, applied by EncounterStep.cs:110-111). The
/// drawn shape therefore sits under the drifted origin when it resolves. A cone's damage is resolved against the origin
/// the player saw drawn (BIBLE s3 "never a lie about where"), so a player under the drifted cone is hit and a player only
/// under the original, undrifted origin is not. The caster stands still (no sight blocker, so Chase never walks it) and
/// faces -x (yaw 512), so the cone points away from the drift direction.
/// </summary>
public sealed class TideDriftAimTests
{
    private const int Caster = 101;
    private const int Ahead = 1;
    private const int Behind = 2;
    private const string Cast = "boss.tideCone";
    private const int Lead = 40;
    private const int CastTicks = 10;
    private const int Period = 400;
    private const long ConeRadius = 3000;
    private const int ConeAngle1024 = 256;
    private const int DriftMmPerTick = 100;
    private const int CasterYaw = 512; // facing -x
    private const int MaxTicks = 800;

    // An open hall with no obstacle: the caster has sight of every player, so Chase never walks it during the lead.
    private static NavLayout OpenHall() => new(
        new[] { new NavBox("hall", new Bounds(-20000, 40000, -6000, 6000), true, null) },
        new[] { new NavTrigger("p1", new Bounds(39500, 40000, 5500, 6000)) },
        new[] { new Vec3(39000, 0, 0) });

    private static Run Create(long aheadX, long behindX)
    {
        var ability = RunTestKit.Ability(
            Cast,
            new EffectOp[] { new DamageOp(5, "physical") },
            shape: new AbilityShape(ShapeKind.Cone, ConeRadius, ConeAngle1024)) with { CastTicks = CastTicks };
        var step = ScriptStep.Loop(Period, Cast) with
        {
            TsCastReplace = true,
            Telegraph = new TelegraphSpec("cone", Lead) { Radius = ConeRadius, Angle1024 = ConeAngle1024 },
        };
        // Speed 0: the caster is rooted, so the only thing that moves the drawn origin is the Tide drift under test.
        var spawn = RunTestKit.Spawn(Caster, "dummy", 0, 0, 1000) with { Yaw = CasterYaw, AggroMm = 20000, SpeedMmPerTick = 0 };
        var dungeon = RunTestKit.OnePull("tide-aim", "p1", spawn);
        var layout = OpenHall();
        var content = RunTestKit.Content(
            new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { [Cast] = ability }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Caster] = new[] { step } },
        };
        var members = new[]
        {
            RunTestKit.Member(Ahead, "warden", ActorRole.Tank, aheadX, 0, 1000, 100),
            RunTestKit.Member(Behind, "warden", ActorRole.Damage, behindX, 0, 1000, 100),
        };
        var run = RunRoot.CreateRun(1, members, dungeon, content);
        var rules = run.Encounter.Rules with { ActiveClauseKind = ClauseKind.Tide, DriftMmPerTick = DriftMmPerTick };
        return run with { Encounter = run.Encounter with { Rules = rules } };
    }

    /// <summary>
    /// The cone's origin as the registry holds it on the tick before it resolves (the origin the view draws, after every
    /// Tide drift so far), read from the run's own telegraph registry on a probe run with the players out of the way.
    /// </summary>
    private static Vec3 DisplayedOriginBeforeResolve()
    {
        var run = Create(aheadX: -10000, behindX: -10000);
        for (var i = 0; i < MaxTicks; i++)
        {
            var pending = run.Encounter.Telegraphs.FirstOrDefault(t => t.AbilityId == Cast);
            if (pending is not null && pending.ResolvesTick - 1 == run.State.Tick)
            {
                return pending.Origin;
            }
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
        }
        throw new InvalidOperationException("the drifted cone telegraph never reached its resolve tick");
    }

    private static (long Ahead, long Behind) Play(long aheadX, long behindX)
    {
        var run = Create(aheadX, behindX);
        var completed = false;
        for (var i = 0; i < MaxTicks && !completed; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            // The only cast in this run is the caster's cone; its completion is the only cast_complete.
            completed = stepped.Events.Any(e => e.Type == "cast_complete");
        }
        Assert.True(completed, "the scripted cone cast never completed; the case proves nothing");
        return (run.State.Actors[Ahead].Health, run.State.Actors[Behind].Health);
    }

    [Fact(DisplayName = "Tide aim: the player under the drifted (displayed) cone is hit, the player only under the original origin is not")]
    public void The_drifted_display_is_where_the_damage_lands()
    {
        var displayed = DisplayedOriginBeforeResolve();
        var original = new Vec3(0, 0, 0);
        Assert.True(displayed.X > original.X, $"precondition: Tide must have drifted the drawn cone +x (displayed origin {displayed})");

        // Ahead: 1500 mm in front of the drifted origin along the cone's -x axis, and beyond the original cone's +x side.
        var aheadX = displayed.X - 1500;
        // Behind: 2000 mm behind the original origin, where the undrifted cone reaches and the drifted cone does not.
        var behindX = -2000;
        Assert.True(Telegraphs.PointInCone(displayed, CasterYaw, ConeAngle1024, ConeRadius, new Vec3(aheadX, 0, 0)),
            "precondition: the ahead player must be inside the drifted cone");
        Assert.False(Telegraphs.PointInCone(original, CasterYaw, ConeAngle1024, ConeRadius, new Vec3(aheadX, 0, 0)),
            "precondition: the ahead player must be outside the original cone");
        Assert.True(Telegraphs.PointInCone(original, CasterYaw, ConeAngle1024, ConeRadius, new Vec3(behindX, 0, 0)),
            "precondition: the behind player must be inside the original cone");
        Assert.False(Telegraphs.PointInCone(displayed, CasterYaw, ConeAngle1024, ConeRadius, new Vec3(behindX, 0, 0)),
            "precondition: the behind player must be outside the drifted cone");

        var (aheadHealth, behindHealth) = Play(aheadX, behindX);

        var failures = new List<string>();
        if (aheadHealth >= 1000)
        {
            failures.Add($"Expected the player under the drifted cone to be hit (health < 1000), Actual health {aheadHealth}: the cone resolved on the undrifted origin");
        }
        if (behindHealth < 1000)
        {
            failures.Add($"Expected the player only under the original origin NOT to be hit (health 1000), Actual health {behindHealth}");
        }
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }
}
