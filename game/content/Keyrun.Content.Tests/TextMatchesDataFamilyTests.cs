using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The rule families <see cref="TextMatchesDataTests"/> does not check: GCD, Brace, aura durations and magnitudes,
/// and "adjacent" targeting. It walks the same shown sentences (<see cref="TextMatchesDataTests.AllShown"/>: every
/// gear Facet and every talent option, with the base and the patched ability) and fails on any phrase the patched
/// data does not back. Tooltip text that disagrees with the sim is wrong data shown to the player.
/// </summary>
public sealed class TextMatchesDataFamilyTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    /// <summary>Ticks per second (game/sim/Keyrun.Sim/Core/Tick.cs TickHz): "5s" in a sentence is 100 ticks.</summary>
    private const int TickHz = 20;

    /// <summary>
    /// The longest an effect called "brief" may last: the 3 s of the shortest setup Window (warden.strike's
    /// sundered, 60 ticks). A "brief" effect is gone before the Window it rides with closes.
    /// </summary>
    private const int BriefMaxTicks = 3 * TickHz;

    /// <summary>A phrase a text can carry and the data check it implies (shown sentence, regex match).</summary>
    private sealed record Rule(string Family, string Name, Regex Phrase, Func<TextMatchesDataTests.Shown, Match, bool> Holds);

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A capitalised name ("Braced", "Aegis Shield"): case-sensitive so "grants far more" is not a name.
    private static Regex Named(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    // Every effect op the ability can run: its effects plus every stance variant (Chanter's mend/ward).
    private static IEnumerable<EffectOp> AllOps(AbilityDef a) =>
        (a.Effects ?? Enumerable.Empty<EffectOp>()).Concat(a.Variants?.Values.SelectMany(v => v) ?? Enumerable.Empty<EffectOp>());

    private static IEnumerable<ApplyEffectOp> Applied(AbilityDef a) => AllOps(a).OfType<ApplyEffectOp>();

    private static bool AppliesBrace(AbilityDef a) => Applied(a).Any(o => o.EffectId.StartsWith("brace", StringComparison.Ordinal));

    private static bool Spreads(AbilityDef a) => a.Tags?.Contains("warcry:multi-target") == true;

    private static bool IsArea(AbilityDef a) => a.Shape.Kind is ShapeKind.Cone or ShapeKind.Circle or ShapeKind.Line;

    // "aegisShield" read as "aegis shield": a sentence may name an effect by its id words or its display name.
    private static string Words(string id) => Regex.Replace(id, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

    private static bool EffectNamed(EffectDef e, string name) =>
        string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) || Words(e.Id) == name.ToLowerInvariant();

    /// <summary>The effect a sentence names ("Braced", "Aegis Shield", "Ward Pulse"), or null.</summary>
    private static EffectDef? EffectByName(string name) => Reg.Effects.Values.FirstOrDefault(e => EffectNamed(e, name));

    /// <summary>
    /// Every timed op an ability runs, keyed by what it is ("window:sundered", "effect:stunned"), with its longest
    /// duration, narrowed to what <paramref name="subject"/> names: a Window id ("Sundered", "Attuned"), an effect
    /// ("Braced"), "shield" (an effect whose name or id says shield), "patch" (a floor Window). A subject that names
    /// none of those ("Freeze") keeps every timed op.
    /// </summary>
    private static Dictionary<string, int> Durations(AbilityDef a, string? subject)
    {
        var all = new List<(string Key, int Ticks, bool Floor, EffectDef? Effect)>();
        foreach (var op in AllOps(a))
        {
            if (op is WindowOp w) all.Add(("window:" + w.WindowId, w.DurationTicks, !w.OnTarget, null));
            if (op is ApplyEffectOp e) all.Add(("effect:" + e.EffectId, e.DurationTicks, false, Reg.Effects.TryGetValue(e.EffectId, out var d) ? d : null));
        }
        var s = subject?.ToLowerInvariant();
        IEnumerable<(string Key, int Ticks, bool Floor, EffectDef? Effect)> pick = all;
        if (s == "patch") pick = all.Where(x => x.Floor);
        else if (s == "shield") pick = all.Where(x => x.Effect is { } d && (d.Name.Contains("Shield", StringComparison.OrdinalIgnoreCase) || d.Id.Contains("shield", StringComparison.OrdinalIgnoreCase)));
        else if (s is not null && all.Any(x => x.Key == "window:" + s || (x.Effect is { } d && EffectNamed(d, s))))
            pick = all.Where(x => x.Key == "window:" + s || (x.Effect is { } d && EffectNamed(d, s)));
        return pick.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Max(x => x.Ticks), StringComparer.Ordinal);
    }

    // The subject before "lasts": "Sundered lasts", "the shield lasts", "patch, lasts" (no subject: null).
    private static string? Subject(Match m) => m.Groups["subj"].Success ? m.Groups["subj"].Value : null;

    // Some timed op both versions run whose duration moved as claimed.
    private static bool DurationMoved(TextMatchesDataTests.Shown s, string? subject, Func<int, int, bool> moved)
    {
        var b = Durations(s.Base, subject);
        var p = Durations(s.Patched, subject);
        return p.Any(kv => b.TryGetValue(kv.Key, out var was) && was > 0 && moved(was, kv.Value));
    }

    // Effects the patch adds that the base does not apply.
    private static List<ApplyEffectOp> Added(TextMatchesDataTests.Shown s)
    {
        var had = Applied(s.Base).Select(o => o.EffectId).ToHashSet(StringComparer.Ordinal);
        return Applied(s.Patched).Where(o => !had.Contains(o.EffectId)).ToList();
    }

    /// <summary>The class an ability belongs to: its id prefix ("warden.strike" is Warden's).</summary>
    private static string ClassOf(AbilityDef a) => a.Id[..a.Id.IndexOf('.', StringComparison.Ordinal)];

    private static IEnumerable<AbilityDef> ClassAbilities(AbilityDef a) =>
        Reg.Abilities.Values.Where(x => x.Id.StartsWith(ClassOf(a) + ".", StringComparison.Ordinal));

    /// <summary>The class ability a sentence opens with ("Brace becomes ...", "Cold Snap's cooldown ..."), or null.</summary>
    private static AbilityDef? Opens(TextMatchesDataTests.Shown s) =>
        ClassAbilities(s.Base).OrderByDescending(x => x.Name.Length)
            .FirstOrDefault(x => Regex.IsMatch(s.Text, "^" + Regex.Escape(x.Name) + "('s)?\\b", RegexOptions.CultureInvariant));

    /// <summary>Every rule this suite applies. A text matching a phrase must satisfy that phrase's check.</summary>
    private static readonly Rule[] Rules =
    {
        // GCD.
        new("gcd", "no GCD", Rx(@"\bno GCD\b"), (s, _) => s.Patched.GcdTicks == 0 && s.Base.GcdTicks > 0),
        // No content field makes a GCD conditional: a patched gcdTicks applies to every cast. A sentence that limits
        // the free GCD to some casts is backed only when the patched GCD is still the base one outside that case.
        new("gcd", "no GCD only sometimes", Rx(@"\bno GCD (when|after|if|following)\b"), (s, _) => s.Patched.GcdTicks == s.Base.GcdTicks),
        new("gcd", "follows a class ability", Named(@"\bfollows (?<name>[A-Z][a-z]+( [A-Z][a-z]+)?)"),
            (s, m) => ClassAbilities(s.Base).Any(a => a.Name == m.Groups["name"].Value)),

        // Brace.
        new("brace", "braces", Rx(@"\bbrac(es|ed)\b"), (s, _) => AppliesBrace(s.Patched)),
        new("brace", "braces only you", Rx(@"\bbraces only you\b"),
            (s, _) => s.Patched.Shape.Kind == ShapeKind.Self && s.Base.Shape.Kind != ShapeKind.Self && AppliesBrace(s.Patched)),
        new("brace", "the sentence opens with its own ability", Rx(@"^"),
            (s, _) => Opens(s) is not { } named || named.Id == s.Base.Id),
        new("brace", "stacks with an effect it keeps", Rx(@"\bstacking with its (?<noun>\w+)"),
            (s, m) => Applied(s.Patched).Select(o => o.EffectId).Distinct().Count() >= 2
                && Applied(s.Base).Any(o => Durations(s.Base, m.Groups["noun"].Value).ContainsKey("effect:" + o.EffectId))),

        // Aura durations and magnitudes.
        new("aura", "lasts longer", Rx(@"(\b(?<subj>\w+) )?lasts longer"), (s, m) => DurationMoved(s, Subject(m), (b, p) => p > b)),
        new("aura", "lasts Xs instead of Ys", Rx(@"(\b(?<subj>\w+) )?lasts (?<now>\d+)s instead of (?<was>\d+)s"),
            (s, m) => DurationMoved(s, Subject(m), (b, p) => b == int.Parse(m.Groups["was"].Value) * TickHz && p == int.Parse(m.Groups["now"].Value) * TickHz)),
        new("aura", "lasts twice as long", Rx(@"(\b(?<subj>\w+) )?lasts twice as long"), (s, m) => DurationMoved(s, Subject(m), (b, p) => p == 2 * b)),
        new("aura", "brief", Rx(@"\bbrief(ly)?\b"), (s, _) => Added(s) is { Count: > 0 } added && added.All(o => o.DurationTicks <= BriefMaxTicks)),
        new("aura", "grants a named effect", Named(@"\bgrants (an? )?(brief )?(?<name>[A-Z][a-z]+( [A-Z][a-z]+)?)"),
            (s, m) => EffectByName(m.Groups["name"].Value) is { } e && Added(s).Any(o => o.EffectId == e.Id)),
        new("aura", "self-aura", Rx(@"\bself-aura\b"), (s, _) => s.Patched.Shape.Kind == ShapeKind.Self),
        // No content field or sim system moves a floor patch after it opens: Windows.cs OpenWindow plants it at the
        // caster's position and nothing writes FloorWindow.Pos again. When one does, this becomes "the patch follows".
        new("aura", "follows its caster", Rx(@"\bfollow(ing|s) you\b"), (_, _) => false),

        // Adjacent targeting.
        new("adjacent", "reaches an adjacent enemy", Rx(@"\badjacent enem|\bnearest other enemy"),
            (s, _) => Spreads(s.Patched) && IsArea(s.Patched) && AllOps(s.Patched).OfType<WindowOp>().Any(w => w.OnTarget && w.Self != true)),
        new("adjacent", "in the cone", Rx(@"\bin the (cone|arc)\b"), (s, _) => s.Patched.Shape.Kind == ShapeKind.Cone),
    };

    /// <summary>
    /// Shown sentences these rules find false on the current data, each handed back for a text fix (wrong data shown
    /// to the player is P0). The walk must find exactly these: a new mismatch fails it, and a fixed one fails it
    /// until it is struck from this list.
    /// </summary>
    private static readonly HashSet<string> KnownMismatches = new(StringComparer.Ordinal)
    {
        "talent warden/warden.strike/riposte [no GCD only sometimes]",
        "facet warden.weapon.mobileWard [follows its caster]",
    };

    // A rule has something to check in this sentence: its phrase matches, and the opening-ability rule (whose phrase
    // matches every sentence) only when the sentence opens with a class ability's name.
    private static bool Fires(Rule r, TextMatchesDataTests.Shown s) =>
        r.Phrase.IsMatch(s.Text) && (r.Name != "the sentence opens with its own ability" || Opens(s) is not null);

    private static IEnumerable<string> Violations(TextMatchesDataTests.Shown s) =>
        Rules.Where(r => r.Phrase.Match(s.Text) is { Success: true } m && !r.Holds(s, m)).Select(r => s.Where + " [" + r.Name + "]");

    [Fact]
    public void Every_shown_text_is_backed_by_its_data_for_gcd_brace_aura_and_adjacent()
    {
        var violations = TextMatchesDataTests.AllShown().SelectMany(Violations).ToHashSet(StringComparer.Ordinal);

        var unknown = violations.Except(KnownMismatches).ToList();
        var fixedNow = KnownMismatches.Except(violations).ToList();
        Assert.True(unknown.Count == 0, "text claims the data does not back:\n" + string.Join("\n", unknown));
        Assert.True(fixedNow.Count == 0, "known mismatches no longer found (strike them from KnownMismatches):\n" + string.Join("\n", fixedNow));
    }

    [Theory]
    [InlineData("gcd")]
    [InlineData("brace")]
    [InlineData("aura")]
    [InlineData("adjacent")]
    public void Each_family_checks_shipped_text(string family)
    {
        var shown = TextMatchesDataTests.AllShown();
        var matched = shown.Where(s => Rules.Any(r => r.Family == family && Fires(r, s))).ToList();
        Assert.True(matched.Count > 0, "no shipped text carries a " + family + " phrase: the family checks nothing");
    }

    private static AbilityDef Base(string id) => Reg.Abilities[id];

    private static TextMatchesDataTests.Shown Probe(string text, AbilityDef b, AbilityDef p) => new("probe", text, b, p);

    private static ValueList<EffectOp> Ops(params EffectOp[] ops) => new(ops);

    // Each rule rejects a false sentence built on real content.
    public static IEnumerable<object[]> FalseClaims()
    {
        var strike = Base("warden.strike");
        var brace = Base("warden.brace");
        var bulwark = Base("warden.bulwark");
        var vigil = Base("warden.vigil");
        var snap = Base("cinder.coldSnap");
        var sundered = new WindowOp { WindowId = "sundered", DurationTicks = 60, OnTarget = true, SetupClass = "warden" };
        var strikeOps = strike.Effects.Where(o => o is not WindowOp).ToArray();
        yield return new object[] { "Strike costs no GCD.", strike, strike };
        yield return new object[] { "Strike costs no GCD when it follows Brace.", strike, strike with { GcdTicks = 0 } };
        yield return new object[] { "Strike is free when it follows Bulwurk.", strike, strike };
        yield return new object[] { "Strike also braces you.", strike, strike };
        yield return new object[] { "Bulwark braces only you.", bulwark, bulwark };
        yield return new object[] { "Brace becomes a Payoff.", strike, strike };
        yield return new object[] { "Vigil grants Braced, stacking with its shield.", vigil, vigil with { Effects = Ops(new ApplyEffectOp { EffectId = "braced", DurationTicks = 100, Stacks = 1 }) } };
        yield return new object[] { "Sundered lasts longer.", strike, strike };
        yield return new object[] { "Sundered lasts 5s instead of 3s.", strike, strike with { Effects = Ops(strikeOps.Append(sundered with { DurationTicks = 80 }).ToArray()) } };
        yield return new object[] { "Freeze lasts twice as long.", snap, snap };
        yield return new object[] { "Strike also stuns briefly.", strike, strike with { Effects = Ops(strike.Effects.Append(new ApplyEffectOp { EffectId = "stunned", DurationTicks = 200, Stacks = 1 }).ToArray()) } };
        yield return new object[] { "Brace also grants Aegis Shield.", brace, brace };
        yield return new object[] { "Brace also grants Ward Wall.", brace, brace with { Effects = Ops(brace.Effects.Append(new ApplyEffectOp { EffectId = "wardShield", DurationTicks = 40, Stacks = 1 }).ToArray()) } };
        yield return new object[] { "Bulwark becomes a self-aura.", bulwark, bulwark };
        yield return new object[] { "Bulwark follows you.", bulwark, bulwark with { Shape = new AbilityShape { Kind = ShapeKind.Self } } };
        yield return new object[] { "Strike applies Sundered to two adjacent enemies.", strike, strike };
        yield return new object[] { "Strike reaches the nearest other enemy in the cone.", strike, strike with { Shape = new AbilityShape { Kind = ShapeKind.Circle, Radius = 3000 }, Tags = new ValueList<string>(new[] { "warcry:multi-target" }) } };
    }

    [Theory]
    [MemberData(nameof(FalseClaims))]
    public void A_false_claim_is_rejected(string text, AbilityDef b, AbilityDef p)
    {
        Assert.NotEmpty(Violations(Probe(text, b, p)));
    }

    // Each rule accepts the true sentence the shipped data does back, so a rule cannot pass by rejecting everything.
    [Theory]
    [InlineData("talent warden/warden.strike/rend")]
    [InlineData("talent warden/warden.vigil/reserve")]
    [InlineData("talent cinder/cinder.coldSnap/deepFreeze")]
    [InlineData("talent warden/warden.bulwark/mobile")]
    [InlineData("talent warden/warden.strike/warcry")]
    [InlineData("facet warden.armor.layeredPlate")]
    [InlineData("facet warden.keystone.lastStand")]
    [InlineData("facet warden.keystone.sunderedStorm")]
    [InlineData("facet warden.charm.releaseTheGuard")]
    public void A_backed_shipped_claim_is_accepted(string where)
    {
        var s = TextMatchesDataTests.AllShown().Single(x => x.Where == where);
        Assert.Contains(Rules, r => Fires(r, s));
        Assert.Empty(Violations(s));
    }
}
