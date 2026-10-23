// Writes an oracle setup file (game/sim/ORACLE.md section 4) from the live content registry (R-K4-17). The committed
// setups are frozen snapshots of a TypeScript createRun; this re-derives one through the same production adapters the run
// setup uses (HostRunSetup.ToSim*, ContentCombos, BuildParty) and Tempo, and writes it in the committed format: compact,
// object keys sorted, ASCII, one trailing newline. Only the named setup file is written; fixtures are never touched here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Bots.Oracle;

/// <summary>A setup file the writer wrote, with the bytes and FNV-1a 32 hex of the exact written bytes.</summary>
/// <param name="Path">The written file.</param>
/// <param name="Bytes">Byte length of the file.</param>
/// <param name="Fnv">FNV-1a 32 of the file bytes, 8 lowercase hex digits.</param>
public sealed record OracleSetupRecord(string Path, long Bytes, string Fnv);

/// <summary>The setups KEYRUN_RERECORD re-writes (R-K4-17), in key order (k2 then k10). The hand-authored pull fixtures are not registry-derived and are not re-recorded.</summary>
public static class OracleRerecordPlan
{
    /// <summary>The setup file names the re-record driver writes, in key order.</summary>
    public static IReadOnlyList<string> SetupFiles { get; } = new[]
    {
        "hollow-vault-k2.setup.json",
        "hollow-vault-k10.setup.json",
    };
}

/// <summary>Writes one oracle setup document from the live registry (ORACLE.md section 4).</summary>
public static class OracleSetupWriter
{
    /// <summary>ORACLE.md section 4: the interrupt lockout the setup carries (the production constant).</summary>
    private const int LockoutTicks = HostRunSetup.InterruptLockoutTicks;

    /// <summary>The Hollow Vault, the one dungeon the registry-derived setups are written for.</summary>
    private const string HollowVault = "hollow-vault";

    /// <summary>Writes <c>&lt;outputDir&gt;/&lt;setupName&gt;</c> from the live registry at the setup's key and tempo, through the production ToSim adapter. Deterministic: same registry, same bytes.</summary>
    /// <param name="registry">The loaded content.</param>
    /// <param name="setupName">The setup file name, e.g. hollow-vault-k2.setup.json.</param>
    /// <param name="outputDir">The directory to write into (created if missing).</param>
    /// <returns>The file, its byte length and its FNV-1a 32 hex.</returns>
    /// <exception cref="NotSupportedException">A setup that is not derived from the registry (the hand-authored pull fixtures).</exception>
    public static OracleSetupRecord Write(C.ContentRegistry registry, string setupName, string outputDir)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrEmpty(setupName);
        ArgumentException.ThrowIfNullOrEmpty(outputDir);

        var keyLevel = KeyLevelOf(setupName);
        var document = BuildDocument(registry, keyLevel);
        var text = document.ToCanonicalJson() + "\n";
        if (text.Any(c => c > 0x7f))
        {
            throw new InvalidOperationException($"{setupName}: the setup is not ASCII");
        }
        var bytes = Encoding.ASCII.GetBytes(text);

        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, setupName);
        File.WriteAllBytes(path, bytes);
        return new OracleSetupRecord(path, bytes.LongLength, Fnv1a.Hex(bytes));
    }

    /// <summary>
    /// The key level a registry-derived setup is written at. The two hollow-vault setups carry their key in the name; the
    /// pull fixtures are hand-authored (content-pull is a synthetic kit, scripted-pull a golden-pull dungeon with an enemy
    /// the registry does not define), so they have no registry source and fail closed rather than invent content.
    /// </summary>
    private static int KeyLevelOf(string setupName) => setupName switch
    {
        "hollow-vault-k2.setup.json" => 2,
        "hollow-vault-k10.setup.json" => 10,
        "content-pull.setup.json" or "scripted-pull.setup.json" => throw new NotSupportedException(
            $"{setupName}: a hand-authored pull fixture, not derived from the content registry; it has no registry source to re-write from"),
        _ => throw new NotSupportedException($"{setupName}: no registry-derived setup named this"),
    };

    /// <summary>The whole setup document, keys in the committed top-level set (bossActorId, content, dungeon, ...).</summary>
    private static JsObject BuildDocument(C.ContentRegistry registry, int keyLevel)
    {
        var dungeon = registry.Dungeons.TryGetValue(HollowVault, out var d)
            ? d
            : throw new InvalidOperationException($"oracle setup writer: unknown dungeon \"{HollowVault}\"");
        var party = HostRunSetup.BuildParty(registry, "warden");
        var (dungeonJson, scripts) = DungeonJson(registry, dungeon, keyLevel);
        var tank = party.Single(p => p.Role == Keyrun.Sim.Core.ActorRole.Tank);

        return JsValue.Obj(
            ("bossActorId", JsValue.Int(HostRunSetup.BossActorId)),
            ("content", ContentJson(registry, dungeon, keyLevel, scripts)),
            ("dungeon", dungeonJson),
            ("dungeonId", JsValue.Str(dungeon.Id)),
            ("humanActorId", JsValue.Int(HostRunSetup.HumanId)),
            ("humanClassId", JsValue.Str("warden")),
            ("keyLevel", JsValue.Int(keyLevel)),
            ("party", JsValue.Arr(party.Select(PartyJson))),
            ("runOptions", JsValue.Obj()),
            ("tankActorId", JsValue.Int(tank.Id)));
    }

    /// <summary>
    /// The dungeon as the run plays it: every registry pull in order (actor ids from 100, one per spawn, yaw 0), then the boss
    /// pull at the boss actor id. Health is Tempo-scaled (no affix), each script is Tempo-scaled, keyed by actor id.
    /// </summary>
    private static (JsObject Dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts) DungeonJson(
        C.ContentRegistry registry, C.DungeonDef dungeon, int keyLevel)
    {
        var scripts = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        var pulls = new List<JsValue>();
        var nextId = HostRunSetup.PullActorIdStart;
        foreach (var pull in dungeon.Pulls)
        {
            var spawns = new List<JsValue>();
            foreach (var group in pull.Enemies)
            {
                var def = Enemy(registry, group.EnemyId, $"pull \"{pull.Id}\"");
                for (var i = 0; i < group.Count; i++)
                {
                    var actorId = nextId++;
                    spawns.Add(SpawnJson(def, actorId, new Vec3(group.Spawn.X, 0, group.Spawn.Z), 0, keyLevel));
                    scripts[actorId] = Tempo.ScaleScript(HostRunSetup.ToSimScript(def.Script), keyLevel);
                }
            }
            pulls.Add(JsValue.Obj(("id", JsValue.Str(pull.Id)), ("spawns", JsValue.Arr(spawns))));
        }

        var boss = Enemy(registry, dungeon.Boss.EnemyId, $"dungeon \"{dungeon.Id}\" boss");
        pulls.Add(JsValue.Obj(
            ("id", JsValue.Str(C.LayoutDef.BossPullId)),
            ("spawns", JsValue.Arr(SpawnJson(boss, HostRunSetup.BossActorId, Vec3.Zero, HostRunSetup.BossYaw, keyLevel)))));
        scripts[HostRunSetup.BossActorId] = Tempo.ScaleScript(HostRunSetup.ToSimScript(boss.Script), keyLevel);

        var json = JsValue.Obj(("id", JsValue.Str(dungeon.Id)), ("pulls", JsValue.Arr(pulls)));
        return (json, scripts);
    }

    private static JsValue SpawnJson(C.EnemyDef def, int actorId, Vec3 pos, int yaw, int keyLevel)
    {
        var members = new List<(string Key, JsValue? Value)>
        {
            ("actorId", JsValue.Int(actorId)),
            ("classId", JsValue.Str(def.Id)),
            ("health", JsValue.Int(Tempo.ScaleHealth(def.MaxHealth, keyLevel))),
            ("pos", JsValue.Vec(pos)),
            ("yaw", JsValue.Int(yaw)),
            ("speedMmPerTick", def.SpeedMmPerTick is { } s ? JsValue.Int(s) : null),
            ("chaseMarginMm", def.ChaseMarginMm is { } c ? JsValue.Int(c) : null),
        };
        return Object(members);
    }

    /// <summary>The content block: every registry ability, effect and clause through the production adapters, the combo tables and scripts.</summary>
    private static JsObject ContentJson(
        C.ContentRegistry registry, C.DungeonDef dungeon, int keyLevel, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts)
    {
        var (combos, fieldCombos) = ContentCombos.ToComboTables(
            registry.Combos.Values, registry.Classes.Keys.ToList(), registry.ComboRiders);
        var rows = registry.Combos.Values.Select(c => c.SetupClass).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var columns = registry.Classes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var movement = registry.Run.Movement;
        var bounds = registry.Run.ArenaBounds;

        return JsValue.Obj(
            ("abilities", Object(registry.Abilities.Select(kv => new KeyValuePair<string, JsValue>(kv.Key, AbilityJson(kv.Value))))),
            ("actorRadiusMm", JsValue.Int(movement.RadiusMm)),
            ("baseSpeedMmPerTick", JsValue.Int(movement.SpeedMmPerTick)),
            ("bounds", JsValue.Obj(
                ("maxX", JsValue.Int(bounds.MaxX)),
                ("maxZ", JsValue.Int(bounds.MaxZ)),
                ("minX", JsValue.Int(bounds.MinX)),
                ("minZ", JsValue.Int(bounds.MinZ)))),
            ("clauses", Object(registry.Clauses.Select(kv => new KeyValuePair<string, JsValue>(kv.Key, ClauseJson(HostRunSetup.ToSimClause(kv.Value)))))),
            ("combos", ComboTableJson(combos, rows, columns)),
            ("effects", Object(registry.Effects.Select(kv => new KeyValuePair<string, JsValue>(kv.Key, EffectJson(kv.Value))))),
            ("enemyScripts", Object(scripts.Select(kv => new KeyValuePair<string, JsValue>(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), ScriptJson(kv.Value))))),
            ("fieldCombos", ComboTableJson(fieldCombos, rows, columns)),
            ("keyLevel", JsValue.Int(keyLevel)),
            ("lockoutTicks", JsValue.Int(LockoutTicks)),
            ("parTicks", JsValue.Int(dungeon.ParTicks)));
    }

    private static JsValue AbilityJson(C.AbilityDef registryAbility)
    {
        var a = HostRunSetup.ToSimAbility(registryAbility);
        var members = new List<(string Key, JsValue? Value)>
        {
            ("castTicks", JsValue.Int(a.CastTicks)),
            ("charges", JsValue.Int(a.Charges)),
            ("cooldownTicks", JsValue.Int(a.CooldownTicks)),
            ("cost", JsValue.Obj(("amount", JsValue.Int(a.Cost.Amount)), ("resource", JsValue.Str(a.Cost.Resource)))),
            ("effects", JsValue.Arr(a.Effects.Select(Op))),
            ("gcdTicks", JsValue.Int(a.GcdTicks)),
            ("id", JsValue.Str(a.Id)),
            ("interruptible", JsValue.Bool(a.Interruptible)),
            ("name", JsValue.Str(registryAbility.Name)),
            ("range", JsValue.Int(a.Range)),
            ("shape", ShapeJson(a.Shape)),
            ("tags", JsValue.Arr((a.Tags ?? Array.Empty<string>()).Select(JsValue.Str))),
            ("variants", a.Variants is { } variants
                ? Object(variants.Select(kv => new KeyValuePair<string, JsValue>(kv.Key, JsValue.Arr(kv.Value.Select(Op)))))
                : null),
        };
        return Object(members);
    }

    private static JsValue ShapeJson(AbilityShape s) => Object(new List<(string Key, JsValue? Value)>
    {
        ("angle1024", s.Angle1024 is { } angle ? JsValue.Int(angle) : null),
        ("kind", JsValue.Str(s.Kind.ToString().ToLowerInvariant())),
        ("length", s.Length is { } length ? JsValue.Int(length) : null),
        ("radius", s.Radius is { } radius ? JsValue.Int(radius) : null),
    });

    private static JsValue EffectJson(C.EffectDef registryEffect)
    {
        var e = HostRunSetup.ToSimEffect(registryEffect);
        var m = e.Modifiers;
        var modifiers = Object(new List<(string Key, JsValue? Value)>
        {
            ("absorb", m.Absorb is { } absorb ? JsValue.Int(absorb) : null),
            ("damageDoneBp", m.DamageDoneBp is { } dd ? JsValue.Int(dd) : null),
            ("damageTakenBp", m.DamageTakenBp is { } dt ? JsValue.Int(dt) : null),
            ("hasteBp", m.HasteBp is { } haste ? JsValue.Int(haste) : null),
            ("movementBp", m.MovementBp is { } movement ? JsValue.Int(movement) : null),
        });
        var members = new List<(string Key, JsValue? Value)>
        {
            ("dispellableTag", e.DispellableTag is null ? JsValue.Null : JsValue.Str(e.DispellableTag)),
            ("id", JsValue.Str(e.Id)),
            ("maxStacks", JsValue.Int(e.MaxStacks)),
            ("modifiers", modifiers),
            ("name", JsValue.Str(registryEffect.Name)),
            ("periodic", e.Periodic is { } p
                ? JsValue.Obj(("everyTicks", JsValue.Int(p.EveryTicks)), ("op", Op(p.Op)))
                : null),
        };
        return Object(members);
    }

    private static JsValue ClauseJson(Keyrun.Sim.Encounter.ClauseDef c) => JsValue.Obj(
        ("description", JsValue.Str(c.Description)),
        ("id", JsValue.Str(c.Id)),
        ("kind", JsValue.Str(c.Kind.ToString().ToLowerInvariant())),
        ("name", JsValue.Str(c.Name)),
        ("params", Object(c.Params.Select(kv => new KeyValuePair<string, JsValue>(kv.Key, JsValue.Int(kv.Value))))));

    private static JsValue ComboTableJson(ComboTable table, IReadOnlyList<string> rows, IReadOnlyList<string> columns)
    {
        var members = new List<KeyValuePair<string, JsValue>>();
        foreach (var row in rows)
        {
            var cells = columns
                .Select(col => (Column: col, Combo: table.Lookup(row, col)))
                .Where(x => x.Combo is not null)
                .Select(x => new KeyValuePair<string, JsValue>(x.Column, ComboJson(x.Combo!)))
                .ToList();
            if (cells.Count > 0)
            {
                members.Add(new KeyValuePair<string, JsValue>(row, JsValue.Obj(cells)));
            }
        }
        return JsValue.Obj(members);
    }

    private static JsValue ComboJson(ComboDef combo) => JsValue.Obj(
        ("effects", JsValue.Arr(combo.Effects.Select(Op))),
        ("id", JsValue.Str(combo.Id)));

    private static JsValue ScriptJson(IReadOnlyList<ScriptStep> script) => JsValue.Arr(script.Select(StepJson));

    private static JsValue StepJson(ScriptStep s)
    {
        if (s.When is not null)
        {
            throw new NotSupportedException($"script step casting \"{s.Cast}\" has a gate; the setup format has no encoding for it");
        }
        var at = s.Trigger switch
        {
            StepTrigger.Loop => JsValue.Str("loop"),
            StepTrigger.OnDeath => JsValue.Str("onDeath"),
            StepTrigger.AtTick => JsValue.Int(s.AtTick),
            _ => throw new NotSupportedException($"script step casting \"{s.Cast}\" has an unknown trigger {s.Trigger}"),
        };
        var members = new List<(string Key, JsValue? Value)>
        {
            ("at", at),
            ("cast", JsValue.Str(s.Cast)),
            ("every", s.Every is { } every ? JsValue.Int(every) : null),
            ("target", s.Target is { } t ? JsValue.Str(t.ToString().ToLowerInvariant()) : null),
            ("telegraph", s.Telegraph is { } tg ? TelegraphJson(tg) : null),
        };
        return Object(members);
    }

    private static JsValue TelegraphJson(TelegraphSpec t)
    {
        var members = new List<(string Key, JsValue? Value)>
        {
            ("angle1024", t.Angle1024 is { } angle ? JsValue.Int(angle) : null),
            ("anchor", t.AnchorTarget ? JsValue.Str("target") : null),
            ("leadTicks", JsValue.Int(t.LeadTicks)),
            ("length", t.Length is { } length ? JsValue.Int(length) : null),
            ("radius", t.Radius is { } radius ? JsValue.Int(radius) : null),
            ("shape", JsValue.Str(t.Shape)),
            ("spansCast", t.SpansCast ? JsValue.Bool(true) : null),
            ("width", t.Width is { } width ? JsValue.Int(width) : null),
        };
        return Object(members);
    }

    /// <summary>One effect op as the setup format writes it (the reader's op set; any other op fails closed).</summary>
    private static JsValue Op(EffectOp op) => op switch
    {
        DamageOp d => JsValue.Obj(("amount", JsValue.Int(d.Amount)), ("op", JsValue.Str("damage")), ("school", JsValue.Str(d.School))),
        HealOp h => JsValue.Obj(("amount", JsValue.Int(h.Amount)), ("op", JsValue.Str("heal"))),
        ApplyEffectOp a => JsValue.Obj(
            ("durationTicks", JsValue.Int(a.DurationTicks)),
            ("effectId", JsValue.Str(a.EffectId)),
            ("op", JsValue.Str("applyEffect")),
            ("stacks", JsValue.Int(a.Stacks))),
        RemoveEffectOp r => JsValue.Obj(("effectId", JsValue.Str(r.EffectId)), ("op", JsValue.Str("removeEffect"))),
        InterruptOp => JsValue.Obj(("op", JsValue.Str("interrupt"))),
        ResourceOp r => JsValue.Obj(("delta", JsValue.Int(r.Delta)), ("op", JsValue.Str("resource"))),
        ThreatOp t => Object(new List<(string Key, JsValue? Value)>
        {
            ("amount", t.Amount is { } amount ? JsValue.Int(amount) : null),
            ("multiplierBp", t.MultiplierBp is { } mult ? JsValue.Int(mult) : null),
            ("op", JsValue.Str("threat")),
        }),
        WindowOp w => Object(new List<(string Key, JsValue? Value)>
        {
            ("durationTicks", JsValue.Int(w.DurationTicks)),
            ("alternate", w.Alternate is { } alt ? JsValue.Bool(alt) : null),
            ("onTarget", JsValue.Bool(w.OnTarget)),
            ("op", JsValue.Str("window")),
            ("radiusMm", w.RadiusMm is { } radius ? JsValue.Int(radius) : null),
            ("self", w.Self is { } self ? JsValue.Bool(self) : null),
            ("setupClass", JsValue.Str(w.SetupClass)),
            ("windowId", JsValue.Str(w.WindowId)),
        }),
        PayoffOp => JsValue.Obj(("op", JsValue.Str("payoff"))),
        ComboOp c => JsValue.Obj(("comboTable", JsValue.Str(c.ComboTable)), ("op", JsValue.Str("combo"))),
        DisplaceOp d => Object(new List<(string Key, JsValue? Value)>
        {
            ("distanceMm", JsValue.Int(d.DistanceMm)),
            ("freesBinds", d.FreesBinds ? JsValue.Bool(true) : null),
            ("op", JsValue.Str("displace")),
            ("towardTarget", d.TowardTarget ? JsValue.Bool(true) : null),
        }),
        PullOp p => JsValue.Obj(("distanceMm", JsValue.Int(p.DistanceMm)), ("op", JsValue.Str("pull"))),
        PartyEffectOp p => JsValue.Obj(
            ("durationTicks", JsValue.Int(p.DurationTicks)),
            ("effectId", JsValue.Str(p.EffectId)),
            ("op", JsValue.Str("partyEffect")),
            ("stacks", JsValue.Int(p.Stacks))),
        CloudOp c => JsValue.Obj(
            ("durationTicks", JsValue.Int(c.DurationTicks)),
            ("effectId", JsValue.Str(c.EffectId)),
            ("op", JsValue.Str("cloud")),
            ("radiusMm", JsValue.Int(c.RadiusMm))),
        CloserBurnOp b => JsValue.Obj(
            ("capBp", JsValue.Int(b.CapBp)),
            ("maxHealthBp", JsValue.Int(b.MaxHealthBp)),
            ("op", JsValue.Str("closerBurn")),
            ("school", JsValue.Str(b.School))),
        DamagePctOp d => JsValue.Obj(
            ("maxHealthBp", JsValue.Int(d.MaxHealthBp)),
            ("op", JsValue.Str("damagePct")),
            ("school", JsValue.Str(d.School))),
        _ => throw new NotSupportedException($"effect op {op.GetType().Name} has no setup-format encoding (the setup reader rejects it)"),
    };

    private static JsValue PartyJson(PartyMemberSpec p) => Object(new List<(string Key, JsValue? Value)>
    {
        ("classId", JsValue.Str(p.ClassId)),
        ("id", JsValue.Int(p.Id)),
        ("maxHealth", JsValue.Int(p.MaxHealth)),
        ("maxResource", JsValue.Int(p.MaxResource)),
        ("pos", JsValue.Vec(p.Pos)),
        ("role", JsValue.Str(p.Role.ToJs())),
        ("yaw", p.Yaw is { } yaw ? JsValue.Int(yaw) : null),
    });

    private static C.EnemyDef Enemy(C.ContentRegistry registry, string id, string where) =>
        registry.Enemies.TryGetValue(id, out var def)
            ? def
            : throw new InvalidOperationException($"oracle setup writer: {where} references unknown enemy \"{id}\"");

    /// <summary>A JSON object from members; a null value is left out (the setup format omits an absent optional field).</summary>
    private static JsObject Object(IEnumerable<(string Key, JsValue? Value)> members) =>
        JsValue.Obj(members.Where(m => m.Value is not null).Select(m => new KeyValuePair<string, JsValue>(m.Key, m.Value!)));

    /// <summary>A JSON object from members; a null value is left out.</summary>
    private static JsObject Object(IEnumerable<KeyValuePair<string, JsValue>> members) => JsValue.Obj(members);
}
