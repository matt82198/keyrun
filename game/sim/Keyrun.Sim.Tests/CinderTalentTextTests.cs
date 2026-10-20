using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Tests.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Cinder Overkill talent sentence the player reads (content/data/classes/cinder.json) says what the sim does with
/// its patch. The test reads cinder.detonate from content/data/abilities/cinder.json and the Overkill patch, casts the
/// base and the patched Detonate through the real cast pipeline (<see cref="Rules.Make"/>) on every tick, and measures
/// the hit size and the ticks between hits. The sentence is then checked against those observations: base Detonate
/// already recasts on a cooldown longer than its GCD, so Overkill lengthens a cooldown rather than adding one.
/// </summary>
public sealed class CinderTalentTextTests
{
    private static JsonElement Load(params string[] parts)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(new[] { EncounterTestKit.RepoRoot() }.Concat(parts).ToArray())));
        return doc.RootElement.Clone();
    }

    private static JsonElement BaseDetonate() =>
        Load("content", "data", "abilities", "cinder.json").EnumerateArray()
            .Single(a => a.GetProperty("id").GetString() == "cinder.detonate");

    private static JsonElement Overkill() =>
        Load("content", "data", "classes", "cinder.json").GetProperty("talents").EnumerateArray()
            .Single(f => f.GetProperty("abilityId").GetString() == "cinder.detonate")
            .GetProperty("options").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == "overkill");

    private static JsonElement Field(JsonElement b, JsonElement? patch, string name) =>
        patch is { } p && p.TryGetProperty(name, out var v) ? v : b.GetProperty(name);

    /// <summary>cinder.detonate as the sim sees it: the base fields, with any field the patch sets replacing it.</summary>
    private static AbilityDef Detonate(JsonElement? patch)
    {
        var b = BaseDetonate();
        var damage = Field(b, patch, "effects").EnumerateArray().Single(e => e.GetProperty("op").GetString() == "damage");
        return Def("cinder.detonate", new AbilityShape(ShapeKind.Target),
            new EffectOp[] { new DamageOp(damage.GetProperty("amount").GetInt64(), damage.GetProperty("school").GetString()!), new PayoffOp() },
            gcdTicks: Field(b, patch, "gcdTicks").GetInt32(),
            cooldownTicks: Field(b, patch, "cooldownTicks").GetInt32(),
            charges: Field(b, patch, "charges").GetInt64(),
            costResource: "none",
            range: Field(b, patch, "range").GetInt64(),
            tags: new[] { "payoff" });
    }

    /// <summary>The GCD Detonate starts, read from the base ability.</summary>
    private static int Gcd() => BaseDetonate().GetProperty("gcdTicks").GetInt32();

    /// <summary>
    /// Casts Detonate on every tick for 400 ticks at one dummy and returns the size of the first hit and the ticks
    /// between the first and second hit (the recast the player feels: the larger of the GCD and the cooldown).
    /// </summary>
    private static (long Hit, int Recast) Observe(AbilityDef def)
    {
        var cinder = Make(1, ActorKind.Player, "cinder", ActorRole.Damage, maxResource: 100);
        var dummy = Make(2, ActorKind.Enemy, "trash", x: 1000, z: 0, maxHealth: 1_000_000);
        var state = At(0, cinder, dummy);
        var rules = Rules.Make(Config(AbilityMap(def)));
        var hits = new System.Collections.Generic.List<(int Tick, long Amount)>();
        var health = dummy.Health;
        for (var t = 0; t < 400 && hits.Count < 2; t++)
        {
            var tick = state.Tick;
            Tick(state, rules, Cmds(Cast(1, "cinder.detonate", 2)), _ => { });
            if (dummy.Health < health)
            {
                hits.Add((tick, health - dummy.Health));
                health = dummy.Health;
            }
        }
        Assert.Equal(2, hits.Count);
        return (hits[0].Amount, hits[1].Tick - hits[0].Tick);
    }

    private static string Text() => Overkill().GetProperty("text").GetString()!;

    [Fact]
    public void Base_Detonate_already_recasts_on_a_cooldown_longer_than_its_gcd()
    {
        var (_, recast) = Observe(Detonate(null));
        Assert.True(recast > Gcd(), "base Detonate recast " + recast + " ticks, GCD " + Gcd());
    }

    [Fact]
    public void Overkill_text_does_not_claim_to_add_a_cooldown_base_Detonate_already_has()
    {
        var (_, baseRecast) = Observe(Detonate(null));
        if (baseRecast > Gcd())
        {
            Assert.DoesNotMatch(new Regex(@"adds a cooldown|gains a cooldown|now has a cooldown", RegexOptions.IgnoreCase), Text());
        }
    }

    [Fact]
    public void Overkill_text_names_the_longer_recast_the_sim_gives_it()
    {
        var (_, baseRecast) = Observe(Detonate(null));
        var (_, patchedRecast) = Observe(Detonate(Overkill().GetProperty("patch")));
        Assert.True(patchedRecast > baseRecast, "Overkill recast " + patchedRecast + " vs base " + baseRecast);
        Assert.Matches(new Regex(@"longer cooldown|cooldown (rises|grows)", RegexOptions.IgnoreCase), Text());
    }

    /// <summary>
    /// "far harder" / "much harder" is a claim about size; it needs the hit at least 1.5 times the base hit. A plain
    /// "hits harder" only needs the hit to grow.
    /// </summary>
    [Fact]
    public void Overkill_text_sizes_the_bigger_hit_the_sim_deals()
    {
        var (baseHit, _) = Observe(Detonate(null));
        var (patchedHit, _) = Observe(Detonate(Overkill().GetProperty("patch")));
        Assert.True(patchedHit > baseHit, "Overkill hit " + patchedHit + " vs base " + baseHit);
        Assert.Matches(new Regex(@"hits (\w+ )?harder", RegexOptions.IgnoreCase), Text());
        if (patchedHit * 2 < baseHit * 3)
        {
            Assert.DoesNotMatch(new Regex(@"(far|much) harder", RegexOptions.IgnoreCase), Text());
        }
    }
}
