using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Tools.Replay;

/// <summary>
/// The createRun inputs of one oracle setup file (game/sim/ORACLE.md section 4), read into the C# run-root types. The
/// setup's <c>content</c> is the RunContent exactly as the TS run held it, so it is already in sim shape: this reader
/// maps it field for field and fails closed on any field or op it does not know (a setup that grows a new shape fails
/// loudly instead of being replayed with a silent default).
/// </summary>
/// <param name="Seed">The run seed.</param>
/// <param name="Party">The party, in order.</param>
/// <param name="Dungeon">The sim DungeonDef.</param>
/// <param name="Content">The RunContent.</param>
/// <param name="Options">The createRun options (null when the setup's runOptions is absent or null).</param>
public sealed record RunSetup(long Seed, IReadOnlyList<PartyMemberSpec> Party, DungeonDef Dungeon, RunContent Content, RunOptions? Options)
{
    private static readonly HashSet<string> ContentKeys = new(StringComparer.Ordinal)
    {
        "abilities", "effects", "lockoutTicks", "baseSpeedMmPerTick", "actorRadiusMm", "bounds", "enemyScripts",
        "keyLevel", "parTicks", "combos", "fieldCombos", "breakBarSources", "clauses",
    };

    /// <summary>Reads a setup file.</summary>
    /// <param name="path">The setup file path.</param>
    /// <param name="seed">The run seed; null = the setup's own <c>seed</c> (golden setups carry one, bot setups do not).</param>
    /// <returns>The setup.</returns>
    /// <exception cref="FormatException">A missing, mistyped or unknown field.</exception>
    public static RunSetup Load(string path, long? seed = null) => Parse(File.ReadAllText(path), seed);

    /// <summary>Parses setup JSON text (see <see cref="Load"/>).</summary>
    /// <param name="json">The setup JSON.</param>
    /// <param name="seed">The run seed; null = the setup's <c>seed</c>.</param>
    /// <returns>The setup.</returns>
    public static RunSetup Parse(string json, long? seed = null)
    {
        var root = Obj(JsValue.Parse(json), "setup");
        var runSeed = seed ?? (root.Get("seed") is JsInt s ? s.Value : throw new FormatException("setup has no seed and none was given"));
        var party = Arr(Req(root, "party", "setup"), "party").Select((m, i) => Member(m, Idx("party", i))).ToList();
        var dungeon = EncounterJson.ParseDungeon(Req(root, "dungeon", "setup"));
        var content = ParseContent(Obj(Req(root, "content", "setup"), "content"));
        var options = root.Get("runOptions") is JsObject o ? ParseOptions(o) : null;
        return new RunSetup(runSeed, party, dungeon, content, options);
    }

    /// <summary>
    /// R-BOTS-9: this setup as the frozen TS run played it, for a replay of its recorded trace. The loader keeps every
    /// ability's <c>variants</c> (the content's verse kit, which the C# sim plays through Abilities.ResolveVerse), but
    /// the TS sim has no verse rule (sim/**/*.ts reads no <c>variants</c>), so the TS played every verse ability as its
    /// static Effects and the trace records that. A trace replay strips the variants here, as <see cref="TsScripts"/>
    /// and <see cref="RunContent.TsSymmetricPushOut"/> keep the other TS-only behaviours; every other reader of a setup
    /// plays the content's kit.
    /// </summary>
    /// <returns>The setup with no ability variants, in content and in every loadout.</returns>
    public RunSetup WithTsKit()
    {
        static IReadOnlyDictionary<string, AbilityDef> Strip(IReadOnlyDictionary<string, AbilityDef> abilities) =>
            abilities.ToDictionary(kv => kv.Key, kv => kv.Value.Variants is null ? kv.Value : kv.Value with { Variants = null }, StringComparer.Ordinal);
        var options = Options?.Loadouts is { } loadouts
            ? Options with { Loadouts = loadouts.ToDictionary(kv => kv.Key, kv => Strip(kv.Value)) }
            : Options;
        return this with { Content = Content with { Abilities = Strip(Content.Abilities) }, Options = options };
    }

    private static string Idx(string what, int i) => string.Create(CultureInfo.InvariantCulture, $"{what}[{i}]");

    private static JsObject Obj(JsValue? v, string what) => v as JsObject ?? throw new FormatException($"{what} must be an object");

    private static IReadOnlyList<JsValue> Arr(JsValue? v, string what) => v is JsArray a ? a.Items : throw new FormatException($"{what} must be an array");

    private static JsValue Req(JsObject o, string key, string what) => o.Get(key) ?? throw new FormatException($"{what} lacks '{key}'");

    private static long Long(JsValue v, string what) => v is JsInt i ? i.Value : throw new FormatException($"{what} must be an integer");

    private static int Int(JsValue v, string what) => checked((int)Long(v, what));

    private static string Str(JsValue v, string what) => v is JsString s ? s.Value : throw new FormatException($"{what} must be a string");

    private static bool Bool(JsValue v, string what) => v is JsBool b ? b.Value : throw new FormatException($"{what} must be a boolean");

    private static long? OptLong(JsObject o, string key, string what) => o.Get(key) switch
    {
        null or JsNull => null,
        var v => Long(v, $"{what}.{key}"),
    };

    private static bool? OptBool(JsObject o, string key, string what) => o.Get(key) switch
    {
        null or JsNull => null,
        var v => Bool(v, $"{what}.{key}"),
    };

    private static void OnlyKeys(JsObject o, string what, params string[] allowed)
    {
        foreach (var kv in o.Members)
        {
            if (Array.IndexOf(allowed, kv.Key) < 0)
            {
                throw new FormatException($"{what} has unknown field '{kv.Key}'");
            }
        }
    }

    private static Vec3 Vec(JsValue v, string what)
    {
        var o = Obj(v, what);
        return new Vec3(Long(Req(o, "x", what), what + ".x"), Long(Req(o, "y", what), what + ".y"), Long(Req(o, "z", what), what + ".z"));
    }

    private static PartyMemberSpec Member(JsValue v, string what)
    {
        var o = Obj(v, what);
        OnlyKeys(o, what, "id", "classId", "role", "pos", "yaw", "maxHealth", "maxResource");
        return new PartyMemberSpec(
            Int(Req(o, "id", what), what + ".id"),
            Str(Req(o, "classId", what), what + ".classId"),
            ActorTags.ParseRole(Str(Req(o, "role", what), what + ".role")),
            Vec(Req(o, "pos", what), what + ".pos"),
            Long(Req(o, "maxHealth", what), what + ".maxHealth"),
            Long(Req(o, "maxResource", what), what + ".maxResource"))
        {
            Yaw = o.Get("yaw") is { } y ? Int(y, what + ".yaw") : null,
        };
    }

    private static RunContent ParseContent(JsObject c)
    {
        foreach (var kv in c.Members)
        {
            if (!ContentKeys.Contains(kv.Key))
            {
                throw new FormatException($"content has unsupported field '{kv.Key}'");
            }
        }
        var bounds = Obj(Req(c, "bounds", "content"), "content.bounds");
        return new RunContent
        {
            Abilities = Abilities(Req(c, "abilities", "content"), "content.abilities"),
            Effects = Obj(Req(c, "effects", "content"), "content.effects").Members
                .ToDictionary(kv => kv.Key, kv => Effect(kv.Value, "content.effects." + kv.Key), StringComparer.Ordinal),
            LockoutTicks = Int(Req(c, "lockoutTicks", "content"), "content.lockoutTicks"),
            BaseSpeedMmPerTick = Long(Req(c, "baseSpeedMmPerTick", "content"), "content.baseSpeedMmPerTick"),
            ActorRadiusMm = Long(Req(c, "actorRadiusMm", "content"), "content.actorRadiusMm"),
            Bounds = new Bounds(
                Long(Req(bounds, "minX", "bounds"), "bounds.minX"),
                Long(Req(bounds, "maxX", "bounds"), "bounds.maxX"),
                Long(Req(bounds, "minZ", "bounds"), "bounds.minZ"),
                Long(Req(bounds, "maxZ", "bounds"), "bounds.maxZ")),
            EnemyScripts = TsScripts(EncounterJson.ParseScripts(Req(c, "enemyScripts", "content"))),
            KeyLevel = c.Get("keyLevel") is { } k ? Int(k, "content.keyLevel") : null,
            ParTicks = c.Get("parTicks") is { } p ? Int(p, "content.parTicks") : null,
            Combos = c.Get("combos") is { } combos ? Table(combos, "content.combos") : null,
            FieldCombos = c.Get("fieldCombos") is { } field ? Table(field, "content.fieldCombos") : null,
            BreakBarSources = c.Get("breakBarSources") is { } bars
                ? Obj(bars, "content.breakBarSources").Members.Select(kv => Bar(kv.Value, "content.breakBarSources." + kv.Key)).ToList()
                : null,
            Clauses = c.Get("clauses") is { } clauses
                ? Obj(clauses, "content.clauses").Members.ToDictionary(kv => kv.Key, kv => Clause(kv.Value, "content.clauses." + kv.Key), StringComparer.Ordinal)
                : null,
            // The oracle traces record the TS push-out (an enemy can shove a player); STATE.md D54 supersedes it in the
            // game, never in a replay of the frozen TS run (TraceReplayTests replays it tick for tick).
            TsSymmetricPushOut = true,
        };
    }

    /// <summary>
    /// SLAM-TELEGRAPH: a setup is a recording of the frozen TS run, and the TS has no <c>spansCast</c> rule (its
    /// telegraphs always resolve when the cast starts). The TS copies a content telegraph's fields into the setup
    /// verbatim, so a re-recorded setup can carry <c>spansCast: true</c>; it is cleared here so the replay keeps the
    /// TS behaviour the trace recorded. Only the host and content-driven tests apply it. TELEGRAPH-READY: every
    /// telegraph is also marked <see cref="TelegraphSpec.TsUnchecked"/>, because the TS drew a scheduled step's
    /// telegraph even when combat then refused its cast, and the trace records those telegraphs. CAST-FINISH: every step
    /// is also marked <see cref="ScriptStep.TsCastReplace"/>, because the TS fired each step on its scheduled tick even
    /// mid-cast, replacing the cast under way, and the trace records those casts.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> TsScripts(IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts)
    {
        var result = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var kv in scripts)
        {
            result[kv.Key] = kv.Value
                .Select(s => s.Telegraph is { } tg ? s with { Telegraph = tg with { SpansCast = false, TsUnchecked = true } } : s)
                .Select(s => s with { TsCastReplace = true })
                .ToList();
        }
        return result;
    }

    private static Dictionary<string, AbilityDef> Abilities(JsValue v, string what) =>
        Obj(v, what).Members.ToDictionary(kv => kv.Key, kv => Ability(kv.Value, what + "." + kv.Key), StringComparer.Ordinal);

    private static AbilityDef Ability(JsValue v, string what)
    {
        var a = Obj(v, what);
        // "name" (display) rides along in RunContent and no sim code reads it, so it is accepted and not carried.
        // "variants" (a Chanter verse ability's per-verse ops, BIBLE section 5) IS read: Abilities.ResolveVerse plays the
        // caster's verse variant. R-BOTS-9 (BIBLE section 7, "Facets change function"): a loader that drops them plays
        // every verse as the static Effects, a different kit than the content (ForkParityTests.ToSim keeps them).
        OnlyKeys(a, what, "id", "castTicks", "gcdTicks", "cooldownTicks", "charges", "cost", "range", "shape", "interruptible", "effects", "tags", "name", "variants");
        var shape = Obj(Req(a, "shape", what), what + ".shape");
        OnlyKeys(shape, what + ".shape", "kind", "radius", "angle1024", "length");
        var kind = Str(Req(shape, "kind", what + ".shape"), what + ".shape.kind") switch
        {
            "target" => ShapeKind.Target,
            "self" => ShapeKind.Self,
            "cone" => ShapeKind.Cone,
            "circle" => ShapeKind.Circle,
            "line" => ShapeKind.Line,
            var k => throw new FormatException($"{what}.shape.kind '{k}' is unknown"),
        };
        var cost = Obj(Req(a, "cost", what), what + ".cost");
        return new AbilityDef
        {
            Id = Str(Req(a, "id", what), what + ".id"),
            CastTicks = Int(Req(a, "castTicks", what), what + ".castTicks"),
            GcdTicks = Int(Req(a, "gcdTicks", what), what + ".gcdTicks"),
            CooldownTicks = Int(Req(a, "cooldownTicks", what), what + ".cooldownTicks"),
            Charges = Long(Req(a, "charges", what), what + ".charges"),
            Cost = new AbilityCost(Str(Req(cost, "resource", what), what + ".cost.resource"), Long(Req(cost, "amount", what), what + ".cost.amount")),
            Range = Long(Req(a, "range", what), what + ".range"),
            Shape = new AbilityShape(kind, OptLong(shape, "radius", what), OptLong(shape, "angle1024", what), OptLong(shape, "length", what)),
            Interruptible = Bool(Req(a, "interruptible", what), what + ".interruptible"),
            Effects = Ops(Req(a, "effects", what), what + ".effects"),
            Tags = a.Get("tags") is { } tags ? Arr(tags, what + ".tags").Select((t, i) => Str(t, Idx(what + ".tags", i))).ToList() : null,
            Variants = a.Get("variants") is { } variants && variants is not JsNull
                ? Obj(variants, what + ".variants").Members.ToDictionary(
                    kv => kv.Key,
                    kv => Ops(kv.Value, what + ".variants." + kv.Key),
                    StringComparer.Ordinal)
                : null,
        };
    }

    private static EffectDef Effect(JsValue v, string what)
    {
        var e = Obj(v, what);
        OnlyKeys(e, what, "id", "maxStacks", "dispellableTag", "modifiers", "periodic", "name");
        var m = Obj(Req(e, "modifiers", what), what + ".modifiers");
        OnlyKeys(m, what + ".modifiers", "damageDoneBp", "damageTakenBp", "hasteBp", "movementBp", "absorb");
        EffectPeriodic? periodic = null;
        if (e.Get("periodic") is JsObject p)
        {
            periodic = new EffectPeriodic(Int(Req(p, "everyTicks", what), what + ".periodic.everyTicks"), Op(Req(p, "op", what), what + ".periodic.op"));
        }
        else if (e.Get("periodic") is { } bad && bad is not JsNull)
        {
            throw new FormatException(what + ".periodic must be an object or null");
        }
        var tag = e.Get("dispellableTag") switch
        {
            null or JsNull => null,
            var t => Str(t, what + ".dispellableTag"),
        };
        return new EffectDef(
            Str(Req(e, "id", what), what + ".id"),
            Long(Req(e, "maxStacks", what), what + ".maxStacks"),
            tag,
            new EffectModifiers(
                OptLong(m, "damageDoneBp", what), OptLong(m, "damageTakenBp", what), OptLong(m, "hasteBp", what),
                OptLong(m, "movementBp", what), OptLong(m, "absorb", what)),
            periodic);
    }

    private static IReadOnlyList<EffectOp> Ops(JsValue v, string what) => Arr(v, what).Select((o, i) => Op(o, Idx(what, i))).ToList();

    private static EffectOp Op(JsValue v, string what)
    {
        var o = Obj(v, what);
        var kind = Str(Req(o, "op", what), what + ".op");
        string S(string key) => Str(Req(o, key, what), what + "." + key);
        long L(string key) => Long(Req(o, key, what), what + "." + key);
        int I(string key) => Int(Req(o, key, what), what + "." + key);
        switch (kind)
        {
            case "damage":
                OnlyKeys(o, what, "op", "amount", "school");
                return new DamageOp(L("amount"), S("school"));
            case "heal":
                OnlyKeys(o, what, "op", "amount");
                return new HealOp(L("amount"));
            case "applyEffect":
                OnlyKeys(o, what, "op", "effectId", "durationTicks", "stacks");
                return new ApplyEffectOp(S("effectId"), I("durationTicks"), L("stacks"));
            case "removeEffect":
                OnlyKeys(o, what, "op", "effectId");
                return new RemoveEffectOp(S("effectId"));
            case "interrupt":
                OnlyKeys(o, what, "op");
                return new InterruptOp();
            case "resource":
                OnlyKeys(o, what, "op", "delta");
                return new ResourceOp(L("delta"));
            case "threat":
                OnlyKeys(o, what, "op", "amount", "multiplierBp");
                return new ThreatOp(OptLong(o, "amount", what), OptLong(o, "multiplierBp", what));
            case "window":
                OnlyKeys(o, what, "op", "windowId", "durationTicks", "onTarget", "setupClass", "self", "alternate", "radiusMm");
                return new WindowOp(S("windowId"), I("durationTicks"), Bool(Req(o, "onTarget", what), what + ".onTarget"), S("setupClass"),
                    OptBool(o, "self", what), OptBool(o, "alternate", what), OptLong(o, "radiusMm", what));
            case "payoff":
                OnlyKeys(o, what, "op");
                return new PayoffOp();
            case "combo":
                OnlyKeys(o, what, "op", "comboTable");
                return new ComboOp(S("comboTable"));
            case "displace":
                OnlyKeys(o, what, "op", "distanceMm", "freesBinds", "towardTarget");
                return new DisplaceOp(L("distanceMm"), OptBool(o, "freesBinds", what) ?? false, OptBool(o, "towardTarget", what) ?? false);
            case "pull":
                OnlyKeys(o, what, "op", "distanceMm");
                return new PullOp(L("distanceMm"));
            case "partyEffect":
                OnlyKeys(o, what, "op", "effectId", "durationTicks", "stacks");
                return new PartyEffectOp(S("effectId"), I("durationTicks"), L("stacks"));
            case "cloud":
                OnlyKeys(o, what, "op", "effectId", "durationTicks", "radiusMm");
                return new CloudOp(S("effectId"), I("durationTicks"), L("radiusMm"));
            case "closerBurn":
                OnlyKeys(o, what, "op", "maxHealthBp", "capBp", "school");
                return new CloserBurnOp(L("maxHealthBp"), L("capBp"), S("school"));
            case "damagePct":
                OnlyKeys(o, what, "op", "maxHealthBp", "school");
                return new DamagePctOp(L("maxHealthBp"), S("school"));
            default:
                throw new FormatException($"{what}.op '{kind}' is not an op this reader knows");
        }
    }

    private static ComboTable Table(JsValue v, string what)
    {
        var table = new ComboTable();
        foreach (var setup in Obj(v, what).Members)
        {
            foreach (var payoff in Obj(setup.Value, what + "." + setup.Key).Members)
            {
                var where = what + "." + setup.Key + "." + payoff.Key;
                var c = Obj(payoff.Value, where);
                OnlyKeys(c, where, "id", "effects");
                table.Set(setup.Key, payoff.Key, new ComboDef(Str(Req(c, "id", where), where + ".id"), Ops(Req(c, "effects", where), where + ".effects")));
            }
        }
        return table;
    }

    private static BreakBarSource Bar(JsValue v, string what)
    {
        var b = Obj(v, what);
        OnlyKeys(b, what, "id", "actorIds", "max", "drainPerInterrupt", "drainPerControl", "onEmpty");
        return new BreakBarSource(
            Str(Req(b, "id", what), what + ".id"),
            Arr(Req(b, "actorIds", what), what + ".actorIds").Select((x, i) => Int(x, Idx(what + ".actorIds", i))).ToList(),
            Long(Req(b, "max", what), what + ".max"),
            Long(Req(b, "drainPerInterrupt", what), what + ".drainPerInterrupt"),
            Long(Req(b, "drainPerControl", what), what + ".drainPerControl"),
            Arr(Req(b, "onEmpty", what), what + ".onEmpty").Select((x, i) =>
            {
                var where = Idx(what + ".onEmpty", i);
                var e = Obj(x, where);
                OnlyKeys(e, where, "effectId", "durationTicks", "stacks");
                return new BreakBarOnEmptyEffect(Str(Req(e, "effectId", where), where), Int(Req(e, "durationTicks", where), where), Long(Req(e, "stacks", where), where));
            }).ToList());
    }

    private static ClauseDef Clause(JsValue v, string what)
    {
        var c = Obj(v, what);
        OnlyKeys(c, what, "id", "name", "kind", "params", "description");
        var kind = Str(Req(c, "kind", what), what + ".kind") switch
        {
            "echo" => ClauseKind.Echo,
            "tide" => ClauseKind.Tide,
            "hush" => ClauseKind.Hush,
            "pact" => ClauseKind.Pact,
            var k => throw new FormatException($"{what}.kind '{k}' is unknown"),
        };
        var parameters = Obj(Req(c, "params", what), what + ".params").Members
            .ToDictionary(kv => kv.Key, kv => Long(kv.Value, what + ".params." + kv.Key), StringComparer.Ordinal);
        return new ClauseDef(Str(Req(c, "id", what), what + ".id"), Str(Req(c, "name", what), what + ".name"), kind, parameters,
            Str(Req(c, "description", what), what + ".description"));
    }

    private static RunOptions ParseOptions(JsObject o)
    {
        OnlyKeys(o, "runOptions", "clauseId", "loadouts", "rotationIndex");
        Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>? loadouts = null;
        if (o.Get("loadouts") is { } l)
        {
            loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();
            foreach (var kv in Obj(l, "runOptions.loadouts").Members)
            {
                if (!int.TryParse(kv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var actorId))
                {
                    throw new FormatException($"runOptions.loadouts key '{kv.Key}' is not an actor id");
                }
                loadouts[actorId] = Abilities(kv.Value, "runOptions.loadouts." + kv.Key);
            }
        }
        return new RunOptions
        {
            ClauseId = o.Get("clauseId") is { } id ? Str(id, "runOptions.clauseId") : null,
            Loadouts = loadouts,
            // The weekly affix rotation index the run was recorded at (absent = 0): a replay takes it from the trace,
            // never from the replay's date.
            RotationIndex = o.Get("rotationIndex") is { } r ? Int(r, "runOptions.rotationIndex") : 0,
        };
    }
}
