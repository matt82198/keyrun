using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Guides;

/// <summary>What a guide condition reads: the bot view, the deciding seat, and its effective kit.</summary>
/// <param name="View">The bot view of this tick.</param>
/// <param name="Self">The deciding seat.</param>
/// <param name="Kit">The seat's effective kit.</param>
public sealed record GuideContext(BotView View, ActorState Self, EffectiveKit Kit);

/// <summary>One guide rotation entry: a press, the condition under which the guide wants it, and the quoted reason.</summary>
/// <param name="Id">The entry id; a receipt's reason is <c>guide:&lt;class&gt;:&lt;Id&gt;</c>.</param>
/// <param name="Press">The ability id to cast (validated against content).</param>
/// <param name="When">The condition, compiled from the guide's <c>when</c> object.</param>
/// <param name="Reason">The guide's quoted reason; never empty.</param>
public sealed record GuideRule(string Id, string Press, Func<GuideContext, bool> When, string Reason);

/// <summary>One class's guide: its class id and its rotation in priority order.</summary>
/// <param name="ClassId">The class the guide plays.</param>
/// <param name="Rotation">The rotation entries, first castable and holding entry wins.</param>
public sealed record BotGuide(string ClassId, IReadOnlyList<GuideRule> Rotation);

/// <summary>
/// The per-class playtester guides (design/PLAYTESTER-GUIDE.md section 4). A guide file is
/// <c>game/content/data/guides/&lt;classId&gt;.json</c>. Parsing fails closed: any unknown key, unknown ability, unknown
/// condition or malformed value throws an <see cref="InvalidOperationException"/> that names the file's class and rule.
/// </summary>
public sealed class GuideSet
{
    private static readonly string[] Responses = { "faceAway", "stepOut", "spread", "stack", "dragIntoHazard", "take" };

    private readonly IReadOnlyDictionary<string, BotGuide> _byClass;

    private GuideSet(IReadOnlyDictionary<string, BotGuide> byClass) => _byClass = byClass;

    /// <summary>No guides loaded: every <see cref="Require"/> throws.</summary>
    public static GuideSet Empty { get; } = new(new Dictionary<string, BotGuide>(StringComparer.Ordinal));

    /// <summary>Parses one class guide file.</summary>
    /// <param name="json">The guide file's text.</param>
    /// <param name="abilityIds">Every ability id in content; a press outside it is rejected.</param>
    /// <returns>A set holding the one class.</returns>
    /// <exception cref="InvalidOperationException">The guide is malformed or names an unknown ability or condition.</exception>
    public static GuideSet Parse(string json, IReadOnlySet<string> abilityIds)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(abilityIds);
        using var doc = ParseOrThrow(json);
        var root = doc.RootElement;
        RequireObject(root, "guide");
        RejectUnknownKeys(root, "guide", "classId", "version", "rotation", "kicks", "telegraphs", "never");
        var classId = RequireString(root, "classId", "guide");
        var where = $"guide '{classId}'";
        if (RequireInt(root, "version", where) != 1)
        {
            throw Fail(where, "version must be 1");
        }
        var rules = new List<GuideRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in RequireArray(root, "rotation", where).EnumerateArray())
        {
            RequireObject(row, where);
            var id = RequireString(row, "id", where);
            var rw = $"{where} rule '{id}'";
            if (!seen.Add(id))
            {
                throw Fail(rw, "duplicate rule id");
            }
            RejectUnknownKeys(row, rw, "id", "press", "when", "reason");
            var press = RequireString(row, "press", rw);
            if (!abilityIds.Contains(press))
            {
                throw Fail(rw, $"press '{press}' is not an ability in content");
            }
            var reason = RequireString(row, "reason", rw);
            var when = ParseWhen(RequireProperty(row, "when", rw), rw, abilityIds, press);
            rules.Add(new GuideRule(id, press, when, reason));
        }
        ValidateSections(root, where, abilityIds);
        return new GuideSet(new Dictionary<string, BotGuide>(StringComparer.Ordinal) { [classId] = new BotGuide(classId, rules) });
    }

    /// <summary>The guide for <paramref name="classId"/>; a Guided seat with no guide fails here, at plan load.</summary>
    /// <param name="classId">The class id.</param>
    /// <returns>The guide.</returns>
    /// <exception cref="InvalidOperationException">No guide is loaded for the class.</exception>
    public BotGuide Require(string classId)
    {
        ArgumentNullException.ThrowIfNull(classId);
        return _byClass.TryGetValue(classId, out var guide)
            ? guide
            : throw new InvalidOperationException($"no playtester guide for class '{classId}' (guided bots need game/content/data/guides/{classId}.json)");
    }

    private static JsonDocument ParseOrThrow(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"guide is not valid JSON: {ex.Message}", ex);
        }
    }

    private static InvalidOperationException Fail(string where, string why) => new($"{where}: {why}");

    private static void RequireObject(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            throw Fail(where, "expected an object");
        }
    }

    private static void RejectUnknownKeys(JsonElement e, string where, params string[] allowed)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (!allowed.Contains(p.Name, StringComparer.Ordinal))
            {
                throw Fail(where, $"unknown key '{p.Name}'");
            }
        }
    }

    private static JsonElement RequireProperty(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) ? v : throw Fail(where, $"missing '{name}'");

    private static JsonElement RequireArray(JsonElement e, string name, string where) =>
        ArrayOf(RequireProperty(e, name, where), name, where);

    private static JsonElement ArrayOf(JsonElement v, string name, string where) =>
        v.ValueKind == JsonValueKind.Array ? v : throw Fail(where, $"'{name}' must be an array");

    private static string RequireString(JsonElement e, string name, string where)
    {
        var v = RequireProperty(e, name, where);
        var text = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return string.IsNullOrWhiteSpace(text) ? throw Fail(where, $"'{name}' must be a non-empty string") : text;
    }

    private static int RequireInt(JsonElement e, string name, string where) =>
        IntOf(RequireProperty(e, name, where), name, where);

    private static int IntOf(JsonElement v, string name, string where) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : throw Fail(where, $"'{name}' must be an integer");

    private static bool BoolOf(JsonProperty p, string where) =>
        p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.Value.GetBoolean()
            : throw Fail(where, $"'{p.Name}' must be true or false");

    private static string StringOf(JsonProperty p, string where) =>
        p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())
            ? p.Value.GetString()!
            : throw Fail(where, $"'{p.Name}' must be a non-empty string");

    /// <summary>
    /// Validates the kicks (s3.3), telegraphs (s3.4) and never (s3.5) sections in the s4 shape. They are checked at load
    /// but not yet consumed: GuidedWardenRole does not read them in this slice, so the rotation alone drives behaviour.
    /// </summary>
    private static void ValidateSections(JsonElement root, string where, IReadOnlySet<string> abilityIds)
    {
        if (root.TryGetProperty("kicks", out var kicks))
        {
            foreach (var row in ArrayOf(kicks, "kicks", where).EnumerateArray())
            {
                var rw = $"{where} kick";
                RequireObject(row, rw);
                RejectUnknownKeys(row, rw, "cast", "order", "reason");
                RequireString(row, "cast", rw);
                RequireString(row, "reason", rw);
                var classes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var c in RequireArray(row, "order", rw).EnumerateArray())
                {
                    var id = c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id) || !classes.Add(id))
                    {
                        throw Fail(rw, "'order' holds a class id that is empty or repeated");
                    }
                }
            }
        }
        if (root.TryGetProperty("telegraphs", out var telegraphs))
        {
            foreach (var row in ArrayOf(telegraphs, "telegraphs", where).EnumerateArray())
            {
                var rw = $"{where} telegraph";
                RequireObject(row, rw);
                RejectUnknownKeys(row, rw, "cast", "response", "fallback", "reason");
                RequireString(row, "cast", rw);
                RequireString(row, "reason", rw);
                var response = RequireString(row, "response", rw);
                if (!Responses.Contains(response, StringComparer.Ordinal))
                {
                    throw Fail(rw, $"response '{response}' is not one of {string.Join(", ", Responses)}");
                }
                if (row.TryGetProperty("fallback", out _))
                {
                    var fallback = RequireString(row, "fallback", rw);
                    if (!abilityIds.Contains(fallback))
                    {
                        throw Fail(rw, $"fallback '{fallback}' is not an ability in content");
                    }
                }
            }
        }
        if (root.TryGetProperty("never", out var never))
        {
            foreach (var row in ArrayOf(never, "never", where).EnumerateArray())
            {
                var rw = $"{where} never";
                RequireObject(row, rw);
                RejectUnknownKeys(row, rw, "press", "when", "reason");
                var press = RequireString(row, "press", rw);
                if (!abilityIds.Contains(press))
                {
                    throw Fail(rw, $"press '{press}' is not an ability in content");
                }
                RequireString(row, "reason", rw);
                _ = ParseWhen(RequireProperty(row, "when", rw), rw, abilityIds, press);
            }
        }
    }

    /// <summary>The Warden's kill target (s3.2 row 7): its current target when that is the engage target (the party's focus, else the lowest-health enemy it holds).</summary>
    private static bool IsKillTarget(GuideContext ctx) =>
        ctx.Self.TargetId is { } held && TankEngage.EngageTarget(ctx.View, ctx.Self) is { } kill && kill.Id == held;

    /// <summary>Open Windows on the seat's current target (BotWindows.OpenWindowsOn), empty without a target.</summary>
    private static List<OpenWindow> TargetWindows(GuideContext ctx) =>
        BotWindows.OpenWindowsOn(ctx.View.State.Actors, ctx.View.State.Tick).Where(w => w.TargetId == ctx.Self.TargetId).ToList();

    private static Func<GuideContext, bool> ParseWhen(JsonElement cond, string where, IReadOnlySet<string> abilityIds, string press)
    {
        if (cond.ValueKind != JsonValueKind.Object || cond.EnumerateObject().Count() != 1)
        {
            throw Fail(where, "a condition is an object with exactly one key");
        }
        var p = cond.EnumerateObject().First();
        switch (p.Name)
        {
            case "all":
                var all = Conditions(p.Value, p.Name, where, abilityIds, press);
                return ctx => all.All(f => f(ctx));
            case "any":
                var any = Conditions(p.Value, p.Name, where, abilityIds, press);
                return ctx => any.Any(f => f(ctx));
            case "not":
                var inner = ParseWhen(p.Value, where, abilityIds, press);
                return ctx => !inner(ctx);
            case "resourceAtLeast":
                var resource = IntOf(p.Value, p.Name, where);
                return ctx => ctx.Self.Resource >= resource;
            case "healthBelowPct":
                var pct = IntOf(p.Value, p.Name, where);
                return ctx => ctx.Self.Health * 100 < (long)pct * ctx.Self.MaxHealth;
            case "attackersOnSelfAtLeast":
                var attackers = IntOf(p.Value, p.Name, where);
                return ctx => TankPressure.AttackersOf(ctx.View, ctx.Self) >= attackers;
            case "abilityReady":
                if (p.Value.ValueKind != JsonValueKind.String || !abilityIds.Contains(p.Value.GetString() ?? string.Empty))
                {
                    throw Fail(where, "'abilityReady' must name an ability in content");
                }
                var ability = p.Value.GetString()!;
                return ctx => RoleShared.ReadyAndAffordable(ctx.Self, ability, ctx.View.State.Tick, ctx.Kit);
            case "kickDuty":
                if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw Fail(where, "'kickDuty' must be true or false");
                }
                var wantKick = p.Value.ValueKind == JsonValueKind.True;
                return ctx => (RoleShared.LedgerKickCommand(ctx.View, ctx.Self, ctx.Kit) is not null) == wantKick;
            case "castTicksLeftAtMost":
                // The ledger's kick target (RoleShared.LedgerKickCommand names the caster): its cast ticks left.
                var castWindow = IntOf(p.Value, p.Name, where);
                return ctx => RoleShared.LedgerKickCommand(ctx.View, ctx.Self, ctx.Kit) is { TargetId: { } casterId }
                    && ctx.View.State.Actors.TryGetValue(casterId, out var caster)
                    && caster.Cast is { } cast
                    && cast.EndsTick - ctx.View.State.Tick <= castWindow;
            case "effectNotUp":
                // The seat's own effect list, read the way TankPressure.EffectTicksLeft reads it (that helper is private).
                var notUpId = StringOf(p, where);
                return ctx => !ctx.Self.Effects.Any(e => e.EffectId == notUpId && e.EndsTick > ctx.View.State.Tick);
            case "effectTicksLeftAtMost":
                // The same rule as TankPressure's EffectTicksLeft: zero when the effect is not up.
                if (p.Value.ValueKind != JsonValueKind.Object)
                {
                    throw Fail(where, "'effectTicksLeftAtMost' must be { effectId, ticks }");
                }
                RejectUnknownKeys(p.Value, where, "effectId", "ticks");
                var ticksEffectId = RequireString(p.Value, "effectId", where);
                var ticksMax = RequireInt(p.Value, "ticks", where);
                return ctx => ctx.Self.Effects
                    .Where(e => e.EffectId == ticksEffectId)
                    .Select(e => Math.Max(0, e.EndsTick - ctx.View.State.Tick))
                    .DefaultIfEmpty(0)
                    .Max() <= ticksMax;
            case "targetIsKillTarget":
                var killWant = BoolOf(p, where);
                return ctx => IsKillTarget(ctx) == killWant;
            case "targetWindowNot":
                var windowNot = StringOf(p, where);
                return ctx => !TargetWindows(ctx).Any(w => w.WindowId == windowNot);
            case "targetHasAllyWindow":
                // An ally's Window is one the Warden's payoff class may close (BotWindows.CanClose): set by another class.
                var allyWant = BoolOf(p, where);
                return ctx => TargetWindows(ctx).Any(w => BotWindows.CanClose(w.SetupClass, ctx.Self.ClassId)) == allyWant;
            case "insideTelegraph":
                var insideWant = BoolOf(p, where);
                return ctx => ctx.View.Telegraphs.Any(t => t.Danger && t.ResolvesTick > ctx.View.State.Tick
                    && !Positioning.IsSpent(t, ctx.View.State)
                    && Positioning.InTelegraph(new PointMm(ctx.Self.Pos.X, ctx.Self.Pos.Z), t)) == insideWant;
            case "enemiesInShapeAtLeast":
                // AoeGate.EnemiesInShape against the rule's own press (the shape it would cast) on the seat's target.
                var shapeMin = IntOf(p.Value, p.Name, where);
                return ctx => ctx.View.Kits.Content is { } reg
                    && reg.Abilities.TryGetValue(press, out var pressDef)
                    && AoeGate.EnemiesInShape(ctx.View, ctx.Self, pressDef, ctx.Self.TargetId) >= shapeMin;
            case "seatWithinMm":
                // Any other living party seat within the distance (RoleShared.Dist, the planar distance the bots use).
                var seatMm = IntOf(p.Value, p.Name, where);
                return ctx => ctx.View.State.Actors.Values.Any(a =>
                    a.Id != ctx.Self.Id && a.Kind == ActorKind.Player && a.Alive && RoleShared.Dist(ctx.Self, a) <= seatMm);
            default:
                throw Fail(where, $"'{p.Name}' is not in the guide condition vocabulary");
        }
    }

    private static List<Func<GuideContext, bool>> Conditions(JsonElement list, string op, string where, IReadOnlySet<string> abilityIds, string press)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Fail(where, $"'{op}' must be an array");
        }
        return list.EnumerateArray().Select(c => ParseWhen(c, where, abilityIds, press)).ToList();
    }
}
