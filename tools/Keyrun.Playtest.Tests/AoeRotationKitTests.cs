using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-AOE-1 (findings f-c8ed3a62, f-47b1f102; Matt 2026-10-22: "players love aoe on trash, not one at a time clearing" and
/// "the tank just spams two abilities ... spamming means a CD is required"). The bot party at Hollow Vault key 2 seed 1:
/// the first trash pack dies together because every class has a shaped AoE rotational the bots press at two or more
/// enemies in its shape, and over the first 1200 ticks of combat each bot's cast log cycles at least four buttons with no
/// button over half of its casts.
/// </summary>
public sealed class AoeRotationKitTests
{
    private const int PartyIds = 3;
    private const int TogetherTicks = 60;
    private const int WindowTicks = 1200;

    private static string ClassOf(int actor) => actor switch { 1 => "warden", 2 => "chanter", _ => "cinder" };

    private readonly ITestOutputHelper _out;

    public AoeRotationKitTests(ITestOutputHelper output) => _out = output;

    private sealed record Trace(IReadOnlyList<JsonElement> Events, ContentRegistry Registry);

    private static readonly Lazy<Trace> Run = new(() =>
    {
        var reg = ContentLoader.Load();
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null) { MaxTicks = 4000 }, reg);
        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();
        return new Trace(events, reg);
    });

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();

    private static bool HasDamage(IEnumerable<EffectOp> ops) => ops.OfType<DamageOp>().Any();

    /// <summary>A shaped AoE: a circle, cone or line ability that damages (in its base effects or any verse variant).</summary>
    private static bool IsShapedAoeDamage(AbilityDef def) =>
        def.Shape.Kind is ShapeKind.Circle or ShapeKind.Cone or ShapeKind.Line
        && (HasDamage(def.Effects) || (def.Variants?.Values.Any(HasDamage) ?? false));

    private static IEnumerable<JsonElement> PartyCasts(Trace t) =>
        t.Events.Where(e => Str(e, "type") == "cast_complete" && Int(e, "actorId") <= PartyIds);

    [Fact(DisplayName = "AoE kit (k2 seed 1): the first trash pack dies together and every bot class casts a shaped AoE on it")]
    public void First_pack_dies_together_and_each_class_casts_its_aoe()
    {
        var t = Run.Value;
        var pull = t.Events.First(e => Str(e, "type") == "pull_start");
        var packIds = pull.GetProperty("actorIds").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        var start = Int(pull, "tick");
        var deaths = t.Events
            .Where(e => Str(e, "type") == "death" && packIds.Contains(Int(e, "actorId")))
            .Select(e => Int(e, "tick")).OrderBy(x => x).ToList();
        _out.WriteLine("first pack " + string.Join(",", packIds) + " pulled @" + start + ", deaths @" + string.Join(",", deaths));
        var together = deaths.Zip(deaths.Skip(1), (a, b) => b - a).Any(gap => gap <= TogetherTicks);

        var end = deaths.Count == packIds.Count ? deaths[^1] : int.MaxValue;
        var aoeClasses = PartyCasts(t)
            .Where(e => Int(e, "tick") >= start && Int(e, "tick") <= end)
            .Select(e => Str(e, "abilityId")!)
            .Where(id => t.Registry.Abilities.TryGetValue(id, out var def) && IsShapedAoeDamage(def))
            .Select(id => id.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);
        _out.WriteLine("classes with an AoE cast on the first pack: " + string.Join(",", aoeClasses.OrderBy(x => x, StringComparer.Ordinal)));

        Assert.True(together, "no two first-pack enemies died within " + TogetherTicks + " ticks of each other: deaths @" + string.Join(",", deaths));
        Assert.Equal(new[] { "chanter", "cinder", "warden" }, aoeClasses.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact(DisplayName = "AoE kit (k2 seed 1): over the first 1200 combat ticks each bot casts four or more distinct abilities and none is over half its casts")]
    public void Each_bot_cycles_its_buttons()
    {
        var t = Run.Value;
        var start = Int(t.Events.First(e => Str(e, "type") == "pull_start"), "tick");
        var casts = PartyCasts(t).Where(e => Int(e, "tick") >= start && Int(e, "tick") < start + WindowTicks).ToList();
        var failures = new List<string>();
        for (var actor = 1; actor <= PartyIds; actor++)
        {
            var counts = casts.Where(e => Int(e, "actorId") == actor)
                .GroupBy(e => Str(e, "abilityId")!).ToDictionary(g => g.Key, g => g.Count());
            var total = counts.Values.Sum();
            var summary = "actor " + actor + ": " + string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value));
            _out.WriteLine(summary);
            // R-AOE-1 (always be casting, cooldowns visit every button): each of the class's five rotational abilities is pressed.
            var rot = t.Registry.Classes[ClassOf(actor)].Abilities;
            var missing = new[] { rot.Rot1, rot.Rot2, rot.Rot3, rot.Rot4, rot.Rot5 }.Where(id => !counts.ContainsKey(id)).ToList();
            if (counts.Count < 4 || counts.Values.Max() * 2 > total || missing.Count > 0)
            {
                failures.Add(summary + (missing.Count > 0 ? " (never pressed: " + string.Join(",", missing) + ")" : string.Empty));
            }
        }

        Assert.True(failures.Count == 0, "a bot spams or skips buttons: " + string.Join(" | ", failures));
    }

    [Fact(DisplayName = "AoE kit: every playable class has a shaped AoE rotational, and no AoE builder has a cooldown (R-AOE-2, R-AOE-3)")]
    public void Every_class_has_an_aoe_rotational_and_no_aoe_builder_has_a_cooldown()
    {
        var reg = Run.Value.Registry;
        foreach (var cls in new[] { "warden", "chanter", "cinder" })
        {
            var rot = reg.Classes[cls].Abilities;
            var ids = new[] { rot.Rot1, rot.Rot2, rot.Rot3, rot.Rot4, rot.Rot5 };
            var defs = ids.Select(id => reg.Abilities[id]).ToList();
            Assert.True(defs.Any(IsShapedAoeDamage), cls + " has no shaped AoE damage rotational");
            // R-AOE-2: an AoE builder (a shaped AoE rotational that costs nothing and generates resource) is a spammable
            // filler bound by cast cadence, so it never has a cooldown; AoE arrives by shape and spread margin.
            var cooledBuilders = defs
                .Where(d => IsShapedAoeDamage(d) && d.Cost.Amount == 0 && d.Effects.OfType<ResourceOp>().Any(r => r.Delta > 0) && d.CooldownTicks > 0)
                .Select(d => d.Id + "=" + d.CooldownTicks).ToList();
            Assert.True(cooledBuilders.Count == 0, cls + " has AoE builders with a cooldown: " + string.Join(",", cooledBuilders));
        }
    }
}
