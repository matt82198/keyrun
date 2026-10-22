using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// The numbers and ops an ability tooltip is generated from, in content terms. <see cref="From(AbilityDef)"/> builds
/// them from a content ability; the HUD builds them from the run's own (talented, gear-patched) sim ability through
/// Keyrun.Host.RunAbilityFacts, so the tooltip states what the button does in this run.
/// </summary>
public sealed record AbilityTooltipFacts
{
    /// <summary>The ability id.</summary>
    public required string Id { get; init; }

    /// <summary>Cast time, ticks (0 = instant).</summary>
    public required int CastTicks { get; init; }

    /// <summary>Cooldown per charge, ticks (0 = none).</summary>
    public required int CooldownTicks { get; init; }

    /// <summary>Maximum charges.</summary>
    public required int Charges { get; init; }

    /// <summary>Range, mm (0 = self).</summary>
    public required int Range { get; init; }

    /// <summary>Targeting shape.</summary>
    public required ShapeKind Shape { get; init; }

    /// <summary>Cost resource id ("none" = free).</summary>
    public required string CostResource { get; init; }

    /// <summary>Cost amount.</summary>
    public required int CostAmount { get; init; }

    /// <summary>Tags ("setup:x", "payoff", "interrupt", "defensive", "utility", "movement", ...).</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>The ops the cast applies (a Chanter stance ability's opening "mend" verse).</summary>
    public required IReadOnlyList<EffectOp> Effects { get; init; }

    /// <summary>A Chanter stance ability's "ward" verse ops, or null.</summary>
    public IReadOnlyList<EffectOp>? WardEffects { get; init; }

    /// <summary>The facts of a content ability (its "mend" verse when it has variants, as the run setup plays it).</summary>
    /// <param name="a">The content ability.</param>
    /// <returns>The facts.</returns>
    public static AbilityTooltipFacts From(AbilityDef a)
    {
        ArgumentNullException.ThrowIfNull(a);
        IReadOnlyList<EffectOp> ops = a.Variants is { } v && v.TryGetValue("mend", out var mend) ? mend : a.Effects;
        IReadOnlyList<EffectOp>? ward = a.Variants is { } w && w.TryGetValue("ward", out var wardOps) ? wardOps : null;
        return new AbilityTooltipFacts
        {
            Id = a.Id,
            CastTicks = a.CastTicks,
            CooldownTicks = a.CooldownTicks,
            Charges = a.Charges,
            Range = a.Range,
            Shape = a.Shape.Kind,
            CostResource = a.Cost.Resource,
            CostAmount = a.Cost.Amount,
            Tags = a.Tags.ToList(),
            Effects = ops,
            WardEffects = ward,
        };
    }
}

/// <summary>A generated tooltip: five lines, top to bottom.</summary>
/// <param name="Title">The display name.</param>
/// <param name="Role">The role line (builder / spender / setup: opens W / payoff: closes W for C / utility ...).</param>
/// <param name="Body">What it does, from the ability's ops.</param>
/// <param name="Stats">Cast time, cooldown and range (and charges when more than one).</param>
/// <param name="Flavor">The flavor line.</param>
public sealed record AbilityTooltipText(string Title, string Role, string Body, string Stats, string Flavor)
{
    /// <summary>The lines in draw order.</summary>
    public IReadOnlyList<string> Lines => new[] { Title, Role, Body, Stats, Flavor };
}

/// <summary>
/// Builds an ability's tooltip from content (Matt 2026-10-22: "can we please get some ability tooltips"): never text
/// written per ability in code. The title and flavor come from ability-text.json, the role line from the ability's tags,
/// its Window op and the combo table (which Windows a payoff closes, and into which combo), the body from its ops and
/// the effects they apply, and the stats line from its cast time, cooldown, range and charges.
/// </summary>
public static class AbilityTooltip
{
    /// <summary>Sim ticks per second (Keyrun.Sim.Core.Tick.TickHz; Content has no sim reference, a Playtest test pins it).</summary>
    public const int TickHz = 20;

    /// <summary>Joins the role line's parts.</summary>
    public const string RoleSeparator = " | ";

    /// <summary>Builds the tooltip.</summary>
    /// <param name="facts">The ability's numbers and ops.</param>
    /// <param name="text">The ability's ability-text.json row.</param>
    /// <param name="registry">The loaded content (classes, combos, effects).</param>
    /// <returns>The tooltip.</returns>
    public static AbilityTooltipText Build(AbilityTooltipFacts facts, AbilityTextRow text, ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(registry);
        var cls = registry.Classes.Values.FirstOrDefault(c => AbilityTextLoader.KitIds(c.Abilities).Contains(facts.Id, StringComparer.Ordinal))
            ?? throw new ArgumentException($"tooltip: \"{facts.Id}\" is not a class kit ability", nameof(facts));
        var body = Sentence(Clauses(facts, facts.Effects, cls, registry));
        if (facts.WardEffects is { } ward)
        {
            body += " Ward: " + Sentence(Clauses(facts, ward, cls, registry));
        }
        return new AbilityTooltipText(text.DisplayName, Role(facts, cls, registry), body, Stats(facts), text.Flavor);
    }

    /// <summary>Seconds for a tick count: "1s", "1.5s", "0.2s".</summary>
    /// <param name="ticks">Ticks.</param>
    /// <returns>The text.</returns>
    public static string Seconds(int ticks) => Ratio(ticks, TickHz) + "s";

    /// <summary>Meters for a distance in mm: "3 m", "2.5 m".</summary>
    /// <param name="mm">Millimetres.</param>
    /// <returns>The text.</returns>
    public static string Meters(int mm) => Ratio(mm, 1000) + " m";

    // n / d in integer math (content is integer-only), rounded to hundredths, trailing zeros dropped: 30/20 is "1.5".
    private static string Ratio(long n, long d)
    {
        var hundredths = ((Math.Abs(n) * 100) + (d / 2)) / d;
        var whole = (hundredths / 100).ToString(CultureInfo.InvariantCulture);
        var frac = (hundredths % 100).ToString("00", CultureInfo.InvariantCulture).TrimEnd('0');
        return (n < 0 ? "-" : string.Empty) + whole + (frac.Length > 0 ? "." + frac : string.Empty);
    }

    private static bool Has(AbilityTooltipFacts f, string tag) => f.Tags.Contains(tag, StringComparer.Ordinal);

    private static string WindowName(string windowId) =>
        windowId.Length == 0 ? windowId : char.ToUpperInvariant(windowId[0]) + windowId[1..];

    private static string Role(AbilityTooltipFacts f, ClassDef cls, ContentRegistry registry)
    {
        var parts = new List<string>();
        var utilityTag = Has(f, "interrupt") || Has(f, "defensive") || Has(f, "utility") || Has(f, "movement");
        var spender = f.CostAmount > 0 && f.CostResource != "none";
        if (spender)
        {
            parts.Add($"Spender: {f.CostAmount} {ResourceLabel.For(f.CostResource)}");
        }
        else if (!utilityTag && cls.Loop.Kind != LoopKind.Stance && f.Effects.OfType<ResourceOp>().Any(r => r.Delta > 0))
        {
            parts.Add("Builder");
        }
        if (cls.Loop.Kind == LoopKind.Stance && f.Effects.OfType<ResourceOp>().Any(r => r.Delta != 0))
        {
            parts.Add("Stance: swaps Mend and Ward");
        }
        if (f.Tags.Any(t => t.StartsWith("setup:", StringComparison.Ordinal)) && f.Effects.OfType<WindowOp>().FirstOrDefault() is { } w)
        {
            parts.Add($"Setup: opens {WindowName(w.WindowId)} {(w.OnTarget ? "on target" : "on the floor")}");
        }
        if (Has(f, "payoff"))
        {
            var closes = registry.Combos.Values
                .Where(c => c.PayoffClass == cls.Id && c.SetupClass != cls.Id && registry.Classes.ContainsKey(c.SetupClass))
                .Select(c => $"{WindowName(c.WindowId)} for {c.Name}")
                .ToList();
            parts.Add(closes.Count > 0 ? "Payoff: closes " + string.Join(", ", closes) : "Payoff");
        }
        foreach (var (tag, label) in new[] { ("interrupt", "Interrupt"), ("defensive", "Defensive"), ("utility", "Utility"), ("movement", "Movement") })
        {
            if (Has(f, tag))
            {
                parts.Add(label);
            }
        }
        if (parts.Count == 0)
        {
            parts.Add("Rotation");
        }
        return string.Join(RoleSeparator, parts);
    }

    private static string Stats(AbilityTooltipFacts f)
    {
        var parts = new List<string>
        {
            f.CastTicks > 0 ? "Cast " + Seconds(f.CastTicks) : "Cast instant",
            f.CooldownTicks > 0 ? "Cooldown " + Seconds(f.CooldownTicks) : "Cooldown none",
            f.Range > 0 ? "Range " + Meters(f.Range) : "Range self",
        };
        if (f.Charges > 1)
        {
            parts.Add(FormattableString.Invariant($"{f.Charges} charges"));
        }
        return string.Join(RoleSeparator, parts);
    }

    private static List<string> Clauses(AbilityTooltipFacts f, IReadOnlyList<EffectOp> ops, ClassDef cls, ContentRegistry registry)
    {
        var clauses = new List<string>();
        if (Has(f, "movement") && f.Range > 0 && f.Shape is ShapeKind.Line or ShapeKind.Self)
        {
            clauses.Add($"moves you up to {Meters(f.Range)}");
        }
        var area = f.Shape switch
        {
            ShapeKind.Cone => " in a cone",
            ShapeKind.Circle => " in an area",
            ShapeKind.Line => " in a line",
            _ => string.Empty,
        };
        foreach (var op in ops)
        {
            switch (op)
            {
                case DamageOp d:
                    clauses.Add(FormattableString.Invariant($"deals {d.Amount} {d.School} damage{area}"));
                    break;
                case HealOp h:
                    clauses.Add(FormattableString.Invariant($"heals {h.Amount}{area}"));
                    break;
                case ApplyEffectOp a:
                    clauses.Add(Applies(a, registry));
                    break;
                case RemoveEffectOp r:
                    clauses.Add(r.EffectId == "*"
                        ? "removes one dispellable effect"
                        : $"clears {(registry.Effects.TryGetValue(r.EffectId, out var e) ? e.Name : r.EffectId)}");
                    break;
                case InterruptOp:
                    clauses.Add("interrupts the target's cast");
                    break;
                case ResourceOp res when cls.Loop.Kind == LoopKind.Stance:
                    break; // the role line says it swaps the verse
                case ResourceOp res when res.Delta > 0:
                    clauses.Add(FormattableString.Invariant($"builds {res.Delta} {ResourceLabel.For(cls.Loop.Resource)}"));
                    break;
                case ResourceOp res when res.Delta < 0:
                    clauses.Add(FormattableString.Invariant($"vents {-res.Delta} {ResourceLabel.For(cls.Loop.Resource)}"));
                    break;
                case ThreatOp t when t.MultiplierBp >= 100000:
                    clauses.Add("forces the target to attack you");
                    break;
                case ThreatOp t when t.MultiplierBp > 10000:
                    clauses.Add($"generates {Ratio(t.MultiplierBp!.Value, 10000)}x threat");
                    break;
                case WindowOp w:
                    clauses.Add($"opens {WindowName(w.WindowId)} {(w.OnTarget ? "on the target" : "on the floor")} for {Seconds(w.DurationTicks)}");
                    break;
            }
        }
        return clauses;
    }

    private static string Applies(ApplyEffectOp a, ContentRegistry registry)
    {
        var dur = Seconds(a.DurationTicks);
        if (!registry.Effects.TryGetValue(a.EffectId, out var e))
        {
            return $"applies {a.EffectId} for {dur}";
        }
        var m = e.Modifiers;
        if (e.Periodic is { } p)
        {
            var every = Seconds(p.EveryTicks);
            return p.Op switch
            {
                HealOp h => FormattableString.Invariant($"heals {h.Amount} every {every} for {dur}"),
                DamageOp d => FormattableString.Invariant($"burns for {d.Amount} {d.School} every {every} for {dur}"),
                _ => $"applies {e.Name} for {dur}",
            };
        }
        return a.EffectId switch
        {
            "stunned" => $"stuns for {dur}",
            "silenced" => $"silences for {dur}",
            "blinded" => $"blinds for {dur}",
            _ when m.Absorb is > 0 => FormattableString.Invariant($"absorbs {m.Absorb} damage for {dur}"),
            _ when m.DamageTakenBp is < 0 => FormattableString.Invariant($"cuts damage taken by {-m.DamageTakenBp / 100}% for {dur}"),
            _ when m.MovementBp is > 0 => FormattableString.Invariant($"speeds movement by {m.MovementBp / 100}% for {dur}"),
            _ when m.MovementBp is < 0 => FormattableString.Invariant($"slows by {-m.MovementBp / 100}% for {dur}"),
            _ when m.HasteBp is > 0 => FormattableString.Invariant($"hastes by {m.HasteBp / 100}% for {dur}"),
            _ => $"applies {e.Name} for {dur}",
        };
    }

    private static string Sentence(IReadOnlyList<string> clauses)
    {
        if (clauses.Count == 0)
        {
            return "No direct effect.";
        }
        var joined = clauses.Count == 1
            ? clauses[0]
            : string.Join(", ", clauses.Take(clauses.Count - 1)) + " and " + clauses[^1];
        return char.ToUpperInvariant(joined[0]) + joined[1..] + ".";
    }
}
