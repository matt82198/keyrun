using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Keyrun.Content;

/// <summary>One toolkit gap: a class button or an encounter threat the contract (design/TOOLKIT-CONTRACT.md) wants and content lacks.</summary>
/// <param name="Subject">"class" or "encounter".</param>
/// <param name="Id">The class id, or the encounter id (a pull id or a boss enemy id).</param>
/// <param name="Code">The missing toolkit part.</param>
/// <param name="Detail">One line saying what content has instead.</param>
public sealed record ToolkitGap(string Subject, string Id, string Code, string Detail);

/// <summary>
/// The toolkit rule of design/TOOLKIT-CONTRACT.md s2 and s3 (R-TOOLKIT-1, R-TOOLKIT-2), section 5 lane L1. It REPORTS
/// gaps; it never throws and is deliberately not in <see cref="ContentRules.All"/>, because the shipped content has
/// known gaps (committed as design/toolkit-gaps/*.json and pinned by ToolkitRuleTests). The contract says gate once the
/// gaps are closed: move it into the list then.
/// <para>
/// Nothing here reads an ability or enemy NAME. A button counts by what its effects do (effect modifiers, op kinds,
/// shapes, tags); a threat counts by its telegraph, damage and interruptibility. Class: the movement button must move
/// the caster (apply an effect with movementBp of at least <see cref="MobilityMinMovementBp"/>) and clear a slow
/// (remove a slowing effect); the defensive must apply damageTakenBp of at most <see cref="DefensiveMaxDamageTakenBp"/>,
/// and a healer's must be able to target an ally; the utility button must carry the "utility" tag. Encounter: a threat
/// is a telegraphed scripted cast lethal to a full-health seat (<see cref="SeatHealth"/>); a MOBILITY threat has a circle
/// telegraph walking cannot leave in its warning window; a DEFENSIVE threat is one the best defensive in the content
/// survives and a kick cannot stop. Key scaling (Tempo, affixes) is ignored: the contract measures key 2, no affixes.
/// </para>
/// </summary>
public static class ToolkitRule
{
    /// <summary>A full-health Cinder or Chanter seat (HostRunSetup.cs:89; the smallest seat, so the lethal bar). Not in content.</summary>
    public const int SeatHealth = 250;

    /// <summary>Contract s4.1: a speed effect counts as mobility from +5000 bp.</summary>
    public const int MobilityMinMovementBp = 5000;

    /// <summary>Contract s4.1 and s2: a defensive counts from -3500 bp damage taken.</summary>
    public const int DefensiveMaxDamageTakenBp = -3500;

    /// <summary>Class gaps over <paramref name="reg"/>, in class-id then code order.</summary>
    public static IReadOnlyList<ToolkitGap> ClassGaps(ContentRegistry reg)
    {
        var gaps = new List<ToolkitGap>();
        foreach (ClassDef cls in reg.Classes.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            AbilityDef? move = Find(reg, cls.Abilities.Movement);
            AbilityDef? def = Find(reg, cls.Abilities.Defensive);
            AbilityDef? util = Find(reg, cls.Abilities.Utility);

            IEnumerable<EffectOp> moveOps = move is null ? Array.Empty<EffectOp>() : AllOps(move);
            bool moves = moveOps.OfType<ApplyEffectOp>().Any(a => Modifiers(reg, a.EffectId)?.MovementBp >= MobilityMinMovementBp);
            if (!moves)
                gaps.Add(Class(cls, "mobility-moves-caster", "movement button " + cls.Abilities.Movement + " applies no speed effect of +" + MobilityMinMovementBp + " bp and no displacement (the sim has no displace op yet)"));

            bool frees = moveOps.OfType<RemoveEffectOp>().Any(r => r.EffectId == "*" || Modifiers(reg, r.EffectId)?.MovementBp < 0);
            if (!frees)
                gaps.Add(Class(cls, "mobility-frees-binds", "movement button " + cls.Abilities.Movement + " removes no slow or root"));

            IEnumerable<EffectOp> defOps = def is null ? Array.Empty<EffectOp>() : AllOps(def);
            bool mitigates = defOps.OfType<ApplyEffectOp>().Any(a => Modifiers(reg, a.EffectId)?.DamageTakenBp <= DefensiveMaxDamageTakenBp);
            if (!mitigates)
                gaps.Add(Class(cls, "defensive-missing", "defensive button " + cls.Abilities.Defensive + " applies no damageTakenBp of " + DefensiveMaxDamageTakenBp + " or less"));
            else if (IsHealer(reg, cls) && def!.Shape.Kind == ShapeKind.Self)
                gaps.Add(Class(cls, "defensive-external", "healer defensive " + cls.Abilities.Defensive + " is shape self; a healer's defensive must be able to target an ally"));

            if (util is null || !util.Tags.Contains("utility"))
                gaps.Add(Class(cls, "utility-missing", "utility button " + cls.Abilities.Utility + " is not tagged utility"));
        }
        return gaps.OrderBy(g => g.Id, StringComparer.Ordinal).ThenBy(g => g.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The encounter ids over <paramref name="reg"/>: every dungeon's pull ids, then its segment-boss and final-boss enemy
    /// ids, distinct, in dungeon-id order.
    /// </summary>
    public static IReadOnlyList<string> EncounterIds(ContentRegistry reg)
    {
        var ids = new List<string>();
        foreach (DungeonDef d in reg.Dungeons.Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            ids.AddRange(d.Pulls.Select(p => p.Id));
            ids.AddRange(d.Segments.Select(s => s.Boss.EnemyId));
            ids.Add(d.Boss.EnemyId);
        }
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The enemy ids whose scripts make up encounter <paramref name="encounterId"/>: a pull's enemies, or the boss itself.</summary>
    public static IReadOnlyList<string> ScriptOwners(ContentRegistry reg, string encounterId)
    {
        foreach (DungeonDef d in reg.Dungeons.Values)
            foreach (PullDef p in d.Pulls)
                if (p.Id == encounterId) return p.Enemies.Select(e => e.EnemyId).Distinct(StringComparer.Ordinal).ToList();
        return new[] { encounterId };
    }

    /// <summary>Encounter gaps over <paramref name="reg"/>, in encounter then code order.</summary>
    public static IReadOnlyList<ToolkitGap> EncounterGaps(ContentRegistry reg)
    {
        int bestTakenBp = BestDefensiveBp(reg);
        int walk = reg.Run.Movement.SpeedMmPerTick;
        var gaps = new List<ToolkitGap>();
        foreach (string id in EncounterIds(reg))
        {
            var mobility = new List<string>();
            var defensive = new List<string>();
            int casts = 0, lethal = 0;
            foreach (string enemyId in ScriptOwners(reg, id))
            {
                if (!reg.Enemies.TryGetValue(enemyId, out EnemyDef? enemy)) continue;
                foreach (ScriptStep step in enemy.Script.Where(s => s.Telegraph is not null).DistinctBy(s => s.Cast))
                {
                    if (!reg.Abilities.TryGetValue(step.Cast, out AbilityDef? ability)) continue;
                    casts++;
                    int damage = ability.Effects.OfType<DamageOp>().Sum(d => d.Amount);
                    if (damage < SeatHealth) continue;
                    lethal++;
                    TelegraphSpec t = step.Telegraph!;
                    int window = t.LeadTicks + (t.SpansCast == true ? ability.CastTicks : 0);
                    if (t.Shape == TelegraphShape.Circle && (t.Radius ?? 0) > walk * window)
                        mobility.Add(step.Cast);
                    else if (!ability.Interruptible && damage * (10000 + bestTakenBp) / 10000 < SeatHealth)
                        defensive.Add(step.Cast);
                }
            }
            string seen = casts + " telegraphed casts, " + lethal + " lethal to a " + SeatHealth + " seat";
            if (mobility.Count == 0)
                gaps.Add(new ToolkitGap("encounter", id, "threat-mobility", "no telegraphed circle lethal to a seat that walking cannot leave in its warning window (" + seen + ")"));
            if (defensive.Count == 0)
                gaps.Add(new ToolkitGap("encounter", id, "threat-defensive", "no uninterruptible telegraphed cast lethal to a seat that the best defensive survives (" + seen + ")"));
        }
        return gaps.OrderBy(g => g.Id, StringComparer.Ordinal).ThenBy(g => g.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Renders <paramref name="gaps"/> as the committed gap-file text (LF newlines, trailing newline): a count then one
    /// object per gap with the file that must change, resolved by <paramref name="fileOf"/> from the gap's id.
    /// </summary>
    public static string Render(IReadOnlyList<ToolkitGap> gaps, Func<string, string> fileOf)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"count\": ").Append(gaps.Count).Append(",\n  \"gaps\": [");
        for (int i = 0; i < gaps.Count; i++)
        {
            ToolkitGap g = gaps[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    { \"subject\": ").Append(Q(g.Subject)).Append(", \"id\": ").Append(Q(g.Id))
              .Append(", \"code\": ").Append(Q(g.Code)).Append(", \"file\": ").Append(Q(fileOf(g.Id)))
              .Append(", \"detail\": ").Append(Q(g.Detail)).Append(" }");
        }
        sb.Append(gaps.Count == 0 ? "]\n}\n" : "\n  ]\n}\n");
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Q(string s) => JsonSerializer.Serialize(s, Relaxed);

    private static ToolkitGap Class(ClassDef cls, string code, string detail) => new("class", cls.Id, code, detail);

    private static AbilityDef? Find(ContentRegistry reg, string id) => reg.Abilities.TryGetValue(id, out AbilityDef? a) ? a : null;

    private static EffectModifiers? Modifiers(ContentRegistry reg, string effectId) =>
        reg.Effects.TryGetValue(effectId, out EffectDef? e) ? e.Modifiers : null;

    private static IEnumerable<EffectOp> AllOps(AbilityDef a) =>
        a.Effects.Concat(a.Variants is null ? Enumerable.Empty<EffectOp>() : a.Variants.Values.SelectMany(v => v));

    private static IEnumerable<AbilityDef> KitOf(ContentRegistry reg, ClassDef c)
    {
        ClassAbilitySlots s = c.Abilities;
        foreach (string id in new[] { s.Rot1, s.Rot2, s.Rot3, s.Rot4, s.Rot5, s.Interrupt, s.Defensive, s.Utility, s.Movement })
            if (Find(reg, id) is { } a) yield return a;
    }

    /// <summary>A healer is a class with a heal op anywhere in its kit (content, not a name).</summary>
    private static bool IsHealer(ContentRegistry reg, ClassDef c) => KitOf(reg, c).Any(a => AllOps(a).OfType<HealOp>().Any());

    /// <summary>The strongest damageTakenBp (most negative) any class defensive applies; 0 when none does.</summary>
    private static int BestDefensiveBp(ContentRegistry reg)
    {
        int best = 0;
        foreach (ClassDef c in reg.Classes.Values)
            if (Find(reg, c.Abilities.Defensive) is { } d)
                foreach (ApplyEffectOp a in AllOps(d).OfType<ApplyEffectOp>())
                    best = Math.Min(best, Modifiers(reg, a.EffectId)?.DamageTakenBp ?? 0);
        return best;
    }
}
