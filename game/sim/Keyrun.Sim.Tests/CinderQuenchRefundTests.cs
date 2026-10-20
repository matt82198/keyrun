using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// design/BIBLE.md section 5 (Cinder): Q <c>Quench</c> "refunds 20 Heat on a successful kick". Drives the REAL
/// cinder.quench entry of content/data/abilities/cinder.json through <see cref="Casting.StartCast"/> and
/// <see cref="Casting.CompleteCast"/> and records the caster's Heat before and after: a kick that lands on an
/// interruptible cast refunds 20 Heat, and a kick at an enemy that is not casting refunds nothing.
/// </summary>
public class CinderQuenchRefundTests
{
    private const int CinderId = 1;
    private const int EnemyId = 2;

    private static readonly AbilityDef EnemyBolt = Def("enemy.bolt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "fire") },
        castTicks: 60, costResource: "none", range: 40000, interruptible: true);

    [Fact]
    public void Quench_refunds_20_heat_when_it_interrupts_a_casting_enemy()
    {
        var (before, after, interrupted) = KickAt(enemyCasting: true);
        Assert.True(interrupted);
        Assert.Equal(before + 20, after);
    }

    [Fact]
    public void Quench_refunds_no_heat_when_the_enemy_is_not_casting()
    {
        var (before, after, interrupted) = KickAt(enemyCasting: false);
        Assert.False(interrupted);
        Assert.Equal(before, after);
    }

    private static (long Before, long After, bool Interrupted) KickAt(bool enemyCasting)
    {
        var quench = LoadQuench();
        var cinder = Make(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, maxResource: 100);
        cinder.Resource = 50;
        var enemy = Make(EnemyId, ActorKind.Enemy, x: 1000);
        var state = At(10, cinder, enemy);
        var ctx = new CastContext(AbilityMap(quench, EnemyBolt), NoDefs, 60);
        if (enemyCasting)
        {
            Assert.Null(Casting.StartCast(state, enemy, EnemyBolt, CinderId, null, 10));
        }
        var before = cinder.Resource;
        Assert.Null(Casting.StartCast(state, cinder, quench, EnemyId, null, 10));
        var (events, emit) = Collector();
        Casting.CompleteCast(state, cinder, ctx, emit);
        return (before, cinder.Resource, events.Any(e => e.Type == "interrupt"));
    }

    // The cinder.quench entry read the way game/src/Host/HostRunSetup.cs ToSim reads it (the ops Quench uses).
    private static AbilityDef LoadQuench()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CinderJsonPath()));
        var a = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "cinder.quench");
        Assert.Equal("target", a.GetProperty("shape").GetProperty("kind").GetString());
        var ops = a.GetProperty("effects").EnumerateArray().Select(o => (EffectOp)(o.GetProperty("op").GetString() switch
        {
            "interrupt" => new InterruptOp(),
            "resource" => new ResourceOp(o.GetProperty("delta").GetInt64()),
            var op => throw new InvalidOperationException($"cinder.quench: op {op} not mapped by this test"),
        })).ToList();
        return Def(
            "cinder.quench",
            new AbilityShape(ShapeKind.Target),
            ops,
            castTicks: a.GetProperty("castTicks").GetInt32(),
            gcdTicks: a.GetProperty("gcdTicks").GetInt32(),
            cooldownTicks: a.GetProperty("cooldownTicks").GetInt32(),
            charges: a.GetProperty("charges").GetInt64(),
            costResource: "none",
            range: a.GetProperty("range").GetInt64(),
            tags: a.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToList());
    }

    private static string CinderJsonPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content", "data", "abilities", "cinder.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("content/data/abilities/cinder.json not found above " + AppContext.BaseDirectory);
    }
}
