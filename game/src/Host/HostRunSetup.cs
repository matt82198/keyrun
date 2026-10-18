using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// Everything <see cref="RunRoot.CreateRun"/> needs for one human run, built from the validated content registry:
/// the C# port of the frozen TS client's <c>buildRunSetup</c> (client/src/sim-host/run-setup.ts with
/// content-dungeon.ts, content-combos.ts and content/sim-adapt.ts). Godot-free.
/// </summary>
/// <param name="Party">The party: the human Warden plus a Chanter and a Cinder bot seat (lane 3H; see <see cref="Roster"/>).</param>
/// <param name="Dungeon">The sim dungeon: the content pulls in order (actor ids from 100) plus the boss pull.</param>
/// <param name="Content">The run content, movement from game/content/data/run.json.</param>
/// <param name="HumanActorId">The human's actor id.</param>
/// <param name="Slots">The human's keybind slots (1-5, Q, E, R, F) to ability ids, in ability-bar order.</param>
public sealed record HostRunSetup(
    IReadOnlyList<PartyMemberSpec> Party,
    DungeonDef Dungeon,
    RunContent Content,
    int HumanActorId,
    IReadOnlyList<AbilitySlot> Slots)
{
    /// <summary>run-setup.ts:52 INTERRUPT_LOCKOUT_TICKS (4 s at 20 Hz).</summary>
    public const int InterruptLockoutTicks = 80;

    /// <summary>content-dungeon.ts:38 PULL_ACTOR_ID_START.</summary>
    public const int PullActorIdStart = 100;

    /// <summary>content-dungeon.ts:39 BOSS_ACTOR_ID.</summary>
    public const int BossActorId = 900;

    /// <summary>content-dungeon.ts:43 BOSS_SPAWN (the dungeon data carries no boss spawn point).</summary>
    public static readonly Vec3 BossSpawn = new(15000, 0, 0);

    /// <summary>content-dungeon.ts:44 FACE_MINUS_Z, the boss's spawn yaw.</summary>
    public const int BossYaw = 512;

    /// <summary>content-party.ts:46 PARTY_SPAWN_Z with SPAWN_X[0] (the tank slot), mm.</summary>
    public static readonly Vec3 WardenSpawn = new(0, 0, 4000);

    /// <summary>Yaw 768 faces -z under Facing.cs (0 = +x, 256 = +z): toward the Choir at z 1000 and -1000.</summary>
    public const int WardenYaw = 768;

    /// <summary>content-party.ts:48 HUMAN_MAX_HEALTH.tank.</summary>
    public const long TankMaxHealth = 400;

    /// <summary>content-party.ts:48 HUMAN_MAX_HEALTH.healer and .damage.</summary>
    public const long SupportMaxHealth = 250;

    /// <summary>The human's actor id (every client file's PLAYER_ID); bot seats take 2, 3, ... in roster order.</summary>
    public const int HumanId = 1;

    /// <summary>
    /// The party roster (content-party.ts ROSTER cut to one seat per shipped class): Warden tank, Chanter healer,
    /// Cinder damage, at content-party.ts SPAWN_X[0..2] on PARTY_SPAWN_Z.
    /// </summary>
    public static readonly IReadOnlyList<RosterSlot> Roster = new[]
    {
        new RosterSlot("warden", ActorRole.Tank, 0, TankMaxHealth),
        new RosterSlot("chanter", ActorRole.Healer, -2100, SupportMaxHealth),
        new RosterSlot("cinder", ActorRole.Damage, -700, SupportMaxHealth),
    };

    /// <summary>The bot seats: every party member but the human, ascending by actor id.</summary>
    public IReadOnlyList<int> BotActorIds => Party.Where(p => p.Id != HumanActorId).Select(p => p.Id).OrderBy(id => id).ToList();

    /// <summary>
    /// Builds the setup. Fails closed (<see cref="InvalidOperationException"/>) on an unknown dungeon, class or enemy
    /// and on any content op the sim union does not carry, instead of inventing a default.
    /// </summary>
    /// <param name="registry">The loaded content (<see cref="C.ContentLoader.Load()"/>).</param>
    /// <param name="dungeonId">The dungeon, e.g. "hollow-vault".</param>
    /// <param name="keyLevel">The key level (Tempo scaling, Slack bank).</param>
    /// <param name="humanClassId">The human's class, e.g. "warden".</param>
    /// <param name="withBots">True (default) for the three-seat party: the human plus a bot in every other
    /// <see cref="Roster"/> slot. False for the human's seat alone.</param>
    /// <returns>The setup.</returns>
    public static HostRunSetup Build(C.ContentRegistry registry, string dungeonId, int keyLevel, string humanClassId, bool withBots = true)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (keyLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keyLevel), keyLevel, "key level must be at least 1");
        }
        if (!registry.Dungeons.TryGetValue(dungeonId, out var dungeonDef))
        {
            throw new InvalidOperationException($"run setup: unknown dungeon \"{dungeonId}\"");
        }
        if (!registry.Classes.TryGetValue(humanClassId, out var classDef))
        {
            throw new InvalidOperationException($"run setup: unknown class \"{humanClassId}\"");
        }

        var abilities = registry.Abilities.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);
        var effects = registry.Effects.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);
        var (combos, fieldCombos) = ToComboTables(registry.Combos.Values, registry.Classes.Keys.ToList());
        var (dungeon, scripts) = ToSimDungeon(registry, dungeonDef, keyLevel);
        var clauses = registry.Clauses.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);

        var run = registry.Run;
        var movement = new MovementConfig(run.Movement.SpeedMmPerTick, run.Movement.RadiusMm,
            new Bounds(run.ArenaBounds.MinX, run.ArenaBounds.MaxX, run.ArenaBounds.MinZ, run.ArenaBounds.MaxZ));
        var content = new RunContent
        {
            Abilities = abilities,
            Effects = effects,
            LockoutTicks = InterruptLockoutTicks,
            BaseSpeedMmPerTick = 0,
            ActorRadiusMm = 0,
            Bounds = movement.Bounds,
            EnemyScripts = scripts,
            KeyLevel = keyLevel,
            ParTicks = dungeonDef.ParTicks,
            Combos = combos,
            FieldCombos = fieldCombos,
            Clauses = clauses,
        }.WithMovement(movement);

        var party = BuildParty(registry, classDef.Id, withBots);
        return new HostRunSetup(party, dungeon, content, HumanId, ResolveSlots(classDef));
    }

    /// <summary>
    /// content-party.ts buildParty over the three shipped classes: the human takes the roster slot of its class (else
    /// the tank slot) as actor <see cref="HumanId"/>, every other slot becomes a bot seat with ids from 2 in roster
    /// order. Spawn x, role and max health come from the slot; max resource from the class's loop. With
    /// <paramref name="withBots"/> false only the human's seat is built (the phase 2 solo run).
    /// </summary>
    /// <param name="registry">The content (each roster class must exist).</param>
    /// <param name="humanClassId">The human's class.</param>
    /// <param name="withBots">True to fill the other roster slots with bot seats.</param>
    /// <returns>The party in actor id order.</returns>
    public static IReadOnlyList<PartyMemberSpec> BuildParty(C.ContentRegistry registry, string humanClassId, bool withBots = true)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var humanSlot = Math.Max(0, Roster.ToList().FindIndex(s => s.ClassId == humanClassId));
        var party = new List<PartyMemberSpec>();
        var nextBotId = HumanId + 1;
        for (var i = 0; i < Roster.Count; i++)
        {
            var slot = Roster[i];
            var isHuman = i == humanSlot;
            if (!isHuman && !withBots)
            {
                continue;
            }
            var classId = isHuman ? humanClassId : slot.ClassId;
            if (!registry.Classes.TryGetValue(classId, out var cls))
            {
                throw new InvalidOperationException($"run setup: party slot {i} references unknown class \"{classId}\"");
            }
            var id = isHuman ? HumanId : nextBotId++;
            party.Add(new PartyMemberSpec(id, cls.Id, slot.Role, new Vec3(slot.SpawnX, 0, WardenSpawn.Z), slot.MaxHealth, cls.Loop.Max) { Yaw = WardenYaw });
        }
        return party.OrderBy(p => p.Id).ToList();
    }

    /// <summary>
    /// input/slot-abilities.ts resolveSlotAbilities: each keybind slot to the class kit's ability, in ability-bar order
    /// (1-5 rotational, Q interrupt, E defensive, R utility, F movement). An empty slot throws.
    /// </summary>
    /// <param name="classDef">The class.</param>
    /// <returns>Nine slots.</returns>
    public static IReadOnlyList<AbilitySlot> ResolveSlots(C.ClassDef classDef)
    {
        ArgumentNullException.ThrowIfNull(classDef);
        var a = classDef.Abilities;
        var slots = new[]
        {
            new AbilitySlot(KeyAction.Ability1, "1", a.Rot1),
            new AbilitySlot(KeyAction.Ability2, "2", a.Rot2),
            new AbilitySlot(KeyAction.Ability3, "3", a.Rot3),
            new AbilitySlot(KeyAction.Ability4, "4", a.Rot4),
            new AbilitySlot(KeyAction.Ability5, "5", a.Rot5),
            new AbilitySlot(KeyAction.AbilityQ, "Q", a.Interrupt),
            new AbilitySlot(KeyAction.AbilityE, "E", a.Defensive),
            new AbilitySlot(KeyAction.AbilityR, "R", a.Utility),
            new AbilitySlot(KeyAction.AbilityF, "F", a.Movement),
        };
        foreach (var s in slots)
        {
            if (string.IsNullOrEmpty(s.AbilityId))
            {
                throw new InvalidOperationException($"run setup: class \"{classDef.Id}\" has no ability in slot {s.Key}");
            }
        }
        return slots;
    }

    // run-setup.ts:61 resolveVariant: Chanter's stance abilities always play their "mend" variant.
    private static AbilityDef ToSim(C.AbilityDef a)
    {
        if (a.Shape.Width is not null)
        {
            throw new InvalidOperationException($"run setup: ability \"{a.Id}\" has a shape width (facet patches only)");
        }
        var ops = a.Variants is { } v && v.TryGetValue("mend", out var mend) ? mend : a.Effects;
        return new AbilityDef
        {
            Id = a.Id,
            CastTicks = a.CastTicks,
            GcdTicks = a.GcdTicks,
            CooldownTicks = a.CooldownTicks,
            Charges = a.Charges,
            Cost = new AbilityCost(a.Cost.Resource, a.Cost.Amount),
            Range = a.Range,
            Shape = new AbilityShape(ToSim(a.Shape.Kind), a.Shape.Radius, a.Shape.Angle1024, a.Shape.Length),
            Interruptible = a.Interruptible,
            Effects = ops.Select(o => ToSim(o, a.Id)).ToList(),
            Tags = a.Tags.ToList(),
        };
    }

    private static ShapeKind ToSim(C.ShapeKind k) => k switch
    {
        C.ShapeKind.Target => ShapeKind.Target,
        C.ShapeKind.Self => ShapeKind.Self,
        C.ShapeKind.Cone => ShapeKind.Cone,
        C.ShapeKind.Circle => ShapeKind.Circle,
        C.ShapeKind.Line => ShapeKind.Line,
        _ => throw new InvalidOperationException($"run setup: unknown shape kind {k}"),
    };

    private static EffectOp ToSim(C.EffectOp op, string where) => op switch
    {
        C.DamageOp d => new DamageOp(d.Amount, d.School),
        C.HealOp h => new HealOp(h.Amount),
        C.ApplyEffectOp e => new ApplyEffectOp(e.EffectId, e.DurationTicks, e.Stacks),
        C.RemoveEffectOp r => new RemoveEffectOp(r.EffectId),
        C.InterruptOp => new InterruptOp(),
        C.ResourceOp r => new ResourceOp(r.Delta),
        C.ThreatOp t => new ThreatOp(t.Amount, t.MultiplierBp),
        C.WindowOp w => new WindowOp(
            w.WindowId,
            w.DurationTicks,
            w.OnTarget,
            w.SetupClass ?? throw new InvalidOperationException($"run setup: {where} has a window op without setupClass"),
            w.Self,
            w.Alternate,
            w.RadiusMm),
        C.PayoffOp => new PayoffOp(),
        C.ComboOp c => new ComboOp(c.ComboTable),
        _ => throw new InvalidOperationException($"run setup: {where} has an op the sim does not run ({op.GetType().Name})"),
    };

    private static EffectDef ToSim(C.EffectDef e)
    {
        var m = e.Modifiers;
        return new EffectDef(
            e.Id,
            e.MaxStacks,
            e.DispellableTag,
            new EffectModifiers(m.DamageDoneBp, m.DamageTakenBp, m.HasteBp, m.MovementBp, m.Absorb),
            e.Periodic is { } p ? new EffectPeriodic(p.EveryTicks, ToSim(p.Op, e.Id + ".periodic")) : null);
    }

    private static Keyrun.Sim.Encounter.ClauseDef ToSim(C.ClauseDef c) => new(
        c.Id,
        c.Name,
        c.Kind switch
        {
            C.ClauseKind.Echo => Keyrun.Sim.Encounter.ClauseKind.Echo,
            C.ClauseKind.Tide => Keyrun.Sim.Encounter.ClauseKind.Tide,
            C.ClauseKind.Hush => Keyrun.Sim.Encounter.ClauseKind.Hush,
            C.ClauseKind.Pact => Keyrun.Sim.Encounter.ClauseKind.Pact,
            _ => throw new InvalidOperationException($"run setup: clause \"{c.Id}\" has unknown kind {c.Kind}"),
        },
        c.Params.ToDictionary(kv => kv.Key, kv => (long)kv.Value, StringComparer.Ordinal),
        c.Description);

    /// <summary>
    /// content/sim-adapt.ts toComboTables: exact (setupClass, payoffClass) rows go to the burst table; a
    /// <c>payoffClass "*"</c> row goes to the field table once per other class.
    /// </summary>
    private static (ComboTable Combos, ComboTable FieldCombos) ToComboTables(IEnumerable<C.ComboEntry> entries, IReadOnlyList<string> classIds)
    {
        var combos = new ComboTable();
        var field = new ComboTable();
        foreach (var entry in entries)
        {
            var def = new ComboDef(entry.Id, entry.Effects.Select(o => ToSim(o, "combo " + entry.Id)).ToList());
            if (entry.PayoffClass == "*")
            {
                foreach (var payoff in classIds.Where(c => c != entry.SetupClass))
                {
                    field.Set(entry.SetupClass, payoff, def);
                }
            }
            else
            {
                combos.Set(entry.SetupClass, entry.PayoffClass, def);
            }
        }
        return (combos, field);
    }

    /// <summary>
    /// content/sim-adapt.ts toSimDungeon (ids from <see cref="PullActorIdStart"/> in pull, group, count order; yaw 0;
    /// speed and chase margin only when the enemy sets them) plus content-dungeon.ts buildDungeon: Tempo-scaled health
    /// and scripts, and the boss appended as one more pull at <see cref="BossActorId"/>.
    /// </summary>
    private static (DungeonDef Dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts) ToSimDungeon(
        C.ContentRegistry registry, C.DungeonDef dungeonDef, int keyLevel)
    {
        var nextId = PullActorIdStart;
        var scripts = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        var pulls = new List<PullDef>();
        foreach (var pull in dungeonDef.Pulls)
        {
            var spawns = new List<PullSpawn>();
            foreach (var group in pull.Enemies)
            {
                var def = Enemy(registry, group.EnemyId, $"pull \"{pull.Id}\"");
                for (var i = 0; i < group.Count; i++)
                {
                    var actorId = nextId++;
                    var spawn = new PullSpawn(actorId, def.Id, new Vec3(group.Spawn.X, 0, group.Spawn.Z), 0, def.MaxHealth)
                    {
                        SpeedMmPerTick = def.SpeedMmPerTick,
                        ChaseMarginMm = def.ChaseMarginMm,
                    };
                    spawns.Add(Tempo.ScaleSpawn(spawn, keyLevel));
                    scripts[actorId] = Tempo.ScaleScript(ToSim(def.Script), keyLevel);
                }
            }
            pulls.Add(new PullDef(pull.Id, spawns));
        }

        var boss = Enemy(registry, dungeonDef.Boss.EnemyId, $"dungeon \"{dungeonDef.Id}\" boss");
        pulls.Add(new PullDef("boss", new[]
        {
            new PullSpawn(BossActorId, boss.Id, BossSpawn, BossYaw, Tempo.ScaleHealth(boss.MaxHealth, keyLevel)),
        }));
        scripts[BossActorId] = Tempo.ScaleScript(ToSim(boss.Script), keyLevel);
        return (new DungeonDef(dungeonDef.Id, pulls), scripts);
    }

    private static C.EnemyDef Enemy(C.ContentRegistry registry, string id, string where) =>
        registry.Enemies.TryGetValue(id, out var def)
            ? def
            : throw new InvalidOperationException($"run setup: {where} references unknown enemy \"{id}\"");

    private static IReadOnlyList<ScriptStep> ToSim(IEnumerable<C.ScriptStep> script) => script.Select(ToSim).ToList();

    private static ScriptStep ToSim(C.ScriptStep s)
    {
        var step = s.At.Kind switch
        {
            C.ScriptAtKind.Loop => ScriptStep.Loop(s.Every, s.Cast),
            C.ScriptAtKind.Tick => ScriptStep.At(s.At.Tick, s.Cast, s.Every),
            C.ScriptAtKind.OnDeath => ScriptStep.OnDeath(s.Cast),
            _ => throw new InvalidOperationException($"run setup: script step casting \"{s.Cast}\" has unknown trigger {s.At.Kind}"),
        };
        return step with
        {
            Telegraph = s.Telegraph is { } t
                ? new TelegraphSpec(ShapeName(t.Shape), t.LeadTicks) { Radius = t.Radius, Angle1024 = t.Angle1024, Length = t.Length, Width = t.Width }
                : null,
            Target = s.Target switch
            {
                null => null,
                C.ScriptTarget.Self => ScriptTargetOverride.Self,
                C.ScriptTarget.Threat => ScriptTargetOverride.Threat,
                C.ScriptTarget.Random => ScriptTargetOverride.Random,
                _ => throw new InvalidOperationException($"run setup: script step casting \"{s.Cast}\" has unknown target {s.Target}"),
            },
        };
    }

    private static string ShapeName(C.TelegraphShape shape) => shape switch
    {
        C.TelegraphShape.Circle => "circle",
        C.TelegraphShape.Cone => "cone",
        C.TelegraphShape.Line => "line",
        _ => throw new InvalidOperationException($"run setup: unknown telegraph shape {shape}"),
    };
}

/// <summary>A keybind action (client/src/input/keybinds.ts KeybindAction).</summary>
public enum KeyAction
{
    /// <summary>Rotational slot 1.</summary>
    Ability1,

    /// <summary>Rotational slot 2.</summary>
    Ability2,

    /// <summary>Rotational slot 3.</summary>
    Ability3,

    /// <summary>Rotational slot 4.</summary>
    Ability4,

    /// <summary>Rotational slot 5.</summary>
    Ability5,

    /// <summary>Interrupt.</summary>
    AbilityQ,

    /// <summary>Defensive.</summary>
    AbilityE,

    /// <summary>Class utility.</summary>
    AbilityR,

    /// <summary>Movement ability.</summary>
    AbilityF,

    /// <summary>Cycle the hostile target.</summary>
    TargetCycle,

    /// <summary>Ping a target or spot.</summary>
    Ping,
}

/// <summary>One party roster slot (content-party.ts RosterSlot plus its SPAWN_X and HUMAN_MAX_HEALTH entries).</summary>
/// <param name="ClassId">The class that fills the slot.</param>
/// <param name="Role">The slot's role (bots dispatch on it).</param>
/// <param name="SpawnX">Spawn x, mm (z is the party spawn line).</param>
/// <param name="MaxHealth">Max health.</param>
public sealed record RosterSlot(string ClassId, ActorRole Role, long SpawnX, long MaxHealth);

/// <summary>One ability-bar slot: the action, its key label and the ability it casts.</summary>
/// <param name="Action">The keybind action.</param>
/// <param name="Key">The key label shown on the bar.</param>
/// <param name="AbilityId">The ability id.</param>
public sealed record AbilitySlot(KeyAction Action, string Key, string AbilityId);
