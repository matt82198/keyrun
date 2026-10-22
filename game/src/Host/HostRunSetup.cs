using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
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
/// <param name="Boss">The armed boss rules for the dungeon's boss (lane 3I; 3F's <see cref="BossRules.Arm"/> hand-off):
/// the Sundered Twins, their shared Break bar and the Lock, built from the boss's content entry.</param>
public sealed record HostRunSetup(
    IReadOnlyList<PartyMemberSpec> Party,
    DungeonDef Dungeon,
    RunContent Content,
    int HumanActorId,
    IReadOnlyList<AbilitySlot> Slots,
    BossConfig Boss)
{
    /// <summary>run-setup.ts:52 INTERRUPT_LOCKOUT_TICKS (4 s at 20 Hz).</summary>
    public const int InterruptLockoutTicks = 80;

    /// <summary>
    /// BIBLE s9.3 "Stops as well as kicks": the effects that stop an interruptible cast when they land (Shatter's and Cold
    /// Snap's freeze and the Twins' Break-bar stagger apply <c>stunned</c>; Ward Dissonance applies <c>silenced</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> StopEffectIds = new[] { "stunned", "silenced" };

    /// <summary>
    /// Finding f-c63b43bf (ruling "defer to WoW"): the stops that are stuns. A stun stops every cast of a stunnable actor,
    /// the uninterruptible ones included (uninterruptible = kick-immune only); the boss is exempt through run.json
    /// stunImmuneEnemies. A silence stays an interruptible-only stop.
    /// </summary>
    public static readonly IReadOnlyList<string> StunEffectIds = new[] { "stunned" };

    /// <summary>
    /// BOSS-CC-IMMUNE (R-BOSS-1 follow-up; WoW: bosses are immune to crowd control): what a stunImmuneEnemies actor
    /// refuses on landing, the stun plus Steam's <c>blinded</c> (its carrier misses every hit). Before this the boss was
    /// blinded for most of its fight and missed 31 of 48 hits, so a seat standing in its Cleave was barely hurt.
    /// </summary>
    public static readonly IReadOnlyList<string> ImmuneRefusedEffectIds = new[] { "stunned", "blinded" };

    /// <summary>content-dungeon.ts:38 PULL_ACTOR_ID_START.</summary>
    public const int PullActorIdStart = 100;

    /// <summary>content-dungeon.ts:39 BOSS_ACTOR_ID.</summary>
    public const int BossActorId = 900;

    /// <summary>
    /// The boss's spawn yaw, 512 = facing -x under Facing.cs (0 = +x, 256 = +z): toward the Warden arena's entry door,
    /// since the route runs along +x (STATE.md D18). The boss stands at the arena origin (game/content/data/layout.json);
    /// the flat arena's content-dungeon.ts:43 BOSS_SPAWN (15000, 0) is gone with the flat arena.
    /// </summary>
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

    /// <summary>The first Sundered Twin's actor id; the second is the next id (both free: pulls use 100-899).</summary>
    public const int TwinActorIdStart = BossActorId + 1;

    /// <summary>
    /// How far each Twin spawns from the boss along x, mm: the first at +x, the second at -x (the content carries no
    /// twin spawn point; lane 3F's boss tests flank the boss the same way). 4000 since lane NAV-CORE (design/ART-PLAN.md
    /// section 3, was 2000): at 2000 the Twins' bodies and name plates overlapped the boss's in every frame.
    /// </summary>
    public const int TwinOffsetMm = 4000;

    /// <summary>The content tag that marks the boss's Lock ability (hollowWarden.lock <c>setup:lock</c>).</summary>
    public const string LockTag = "setup:lock";

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

    /// <summary>The class of the human's seat (the screen's Character tab pick; "warden" by default).</summary>
    public string HumanClassId => Party.First(p => p.Id == HumanActorId).ClassId;

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
    /// <param name="rotationIndex">The host's affix rotation index (BIBLE s8: the ISO week, passed in so the setup stays
    /// deterministic). Its parity picks Fortified or Tyrannical on keys 5-9 (plan M5).</param>
    /// <returns>The setup.</returns>
    public static HostRunSetup Build(C.ContentRegistry registry, string dungeonId, int keyLevel, string humanClassId, bool withBots = true, int rotationIndex = 0)
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

        var affixTable = AffixTableOf(registry.Run.Affixes);
        var affixes = Affixes.AffixesFor(affixTable, keyLevel, rotationIndex);
        var bossEnemy = Enemy(registry, dungeonDef.Boss.EnemyId, $"dungeon \"{dungeonDef.Id}\" boss");
        var abilities = ScaleEnemyDamage(
            registry,
            dungeonDef,
            bossEnemy,
            registry.Abilities.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal),
            Affixes.DamageBp(affixTable, affixes, bossSide: false),
            Affixes.DamageBp(affixTable, affixes, bossSide: true));
        var effects = registry.Effects.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);
        var (combos, fieldCombos) = ContentCombos.ToComboTables(registry.Combos.Values, registry.Classes.Keys.ToList(), registry.ComboRiders);
        var (dungeon, scripts) = ToSimDungeon(
            registry, dungeonDef, keyLevel, Affixes.HealthBp(affixTable, affixes, bossSide: false), Affixes.HealthBp(affixTable, affixes, bossSide: true));
        var clauses = registry.Clauses.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);
        var boss = BuildBossConfig(registry, bossEnemy, keyLevel, Affixes.HealthBp(affixTable, affixes, bossSide: true));

        // Lane NAV-CORE (STATE.md D18): the dungeon's rooms and corridors. Pulls stand in their rooms, the party in the
        // Entrance; every pull stands idle from tick 0 and engages on its aggro ring, along one open path (STATE.md D44).
        var layout = registry.Layout;
        if (!string.Equals(layout.Dungeon, dungeonDef.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"run setup: layout.json is for dungeon \"{layout.Dungeon}\", not \"{dungeonDef.Id}\"");
        }
        var nav = NavLayouts.ToSim(layout);
        dungeon = NavLayouts.PlaceDungeon(layout, dungeon);
        // PATH-PACKS (BIBLE s9.1): the layout's trash packs, required and optional, idle on the path from tick 0 and each
        // pulled whole by proximity, like a pull.
        // Mythic+ trash only a pack fields (hall.sentry) is scaled here exactly as a pull spawn is (ScaledTrashSpawn).
        var packOnly = PackOnlyTemplates(registry, layout, dungeonDef, keyLevel, Affixes.HealthBp(affixTable, affixes, bossSide: false));
        var (packs, packScripts) = LayoutPackGroups.Build(layout, dungeon, scripts, packOnly: packOnly);
        // MPLUS-PATROLS (BIBLE s9.1): the layout's patrol packs, each member copied from the same scaled template as a pack
        // member, ids after the packs. allScripts is the pull, boss and pack scripts plus every patrol member's.
        var (patrols, allScripts) = LayoutPackGroups.BuildPatrols(layout, dungeon, packs, packScripts, packOnly: packOnly);
        // R-PAIR-1: a teaching pull's signature casts start early enough to be seen (run.json enemyScriptAt).
        allScripts = WithScriptAt(registry.Run.EnemyScriptAt, dungeon, packs, patrols, allScripts);
        // TEMPO-CAST-FLOOR (BIBLE s8): every cast an enemy script of the run makes (pulls, boss, pack members including
        // pack-only enemies such as the Hall Sentry, and the Twins) gets the key-scaled cooldown its scaled loop was
        // floored against, so the loop speeds up and its cast is ready on the fire tick. allScripts is the pull and boss
        // scripts plus every pack and patrol member's, so this runs after LayoutPackGroups.BuildPatrols; ScaleEnemyCooldowns scales each
        // ability id once however many scripts cast it (finding f-c94c7cd7).
        abilities = Tempo.ScaleEnemyCooldowns(abilities, allScripts.Values.Concat(boss.Twins.Select(t => t.Script)), keyLevel);
        // AGGRO-CHASE (f-f0c886eb): every placed enemy carries its run.json enemyAggro radius, assist and leash.
        (dungeon, packs, patrols) = WithEnemyAggro(registry.Run.EnemyAggro, layout, dungeon, packs, patrols);

        var run = registry.Run;
        var movement = new MovementConfig(run.Movement.SpeedMmPerTick, run.Movement.RadiusMm, nav.Extent);
        var content = new RunContent
        {
            Abilities = abilities,
            Effects = effects,
            LockoutTicks = InterruptLockoutTicks,
            BaseSpeedMmPerTick = 0,
            ActorRadiusMm = 0,
            Bounds = movement.Bounds,
            Overheat = OverheatRulesOf(registry),
            EnemyScripts = allScripts,
            KeyLevel = keyLevel,
            ParTicks = dungeonDef.ParTicks,
            Combos = combos,
            FieldCombos = fieldCombos,
            Clauses = clauses,
            Layout = nav,
            Packs = packs,
            Patrols = patrols,
            StopEffectIds = StopEffectIds,
            StunEffectIds = StunEffectIds,
            StunImmuneClassIds = run.StunImmuneEnemies.ToList(),
            ImmuneRefusedEffectIds = ImmuneRefusedEffectIds,
            Marks = MarkRulesOf(run.PairMarks),
            // R-BOSS-4: an unanswered Cleave stacks Cleaved on each seat it hits other than its target.
            CleaveMark = run.Cleaved is { } cleaved ? new CleaveMarkRule(cleaved.AbilityId, cleaved.EffectId, cleaved.DurationTicks) : null,
        }.WithMovement(movement);

        var party = NavLayouts.PlaceParty(layout, BuildParty(registry, classDef.Id, withBots), dungeon.Pulls[0].Id);
        var botGear = BuildBotGear(registry, party);
        return new HostRunSetup(party, dungeon, content, HumanId, ResolveSlots(classDef), boss)
        {
            Layout = layout,
            KeyAffixes = affixes,
            ClauseStartKey = registry.Run.Affixes.ClauseStartKey,
            RotationIndex = rotationIndex,
            BotGear = botGear,
            BotLoadouts = botGear.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, AbilityDef>)kv.Value.Abilities.ToDictionary(a => a.Key, a => ToSim(a.Value), StringComparer.Ordinal)),
        };
    }

    /// <summary>The key's Fortified/Tyrannical affixes this setup was scaled with (plan M5; the screen shows them).</summary>
    public KeyAffixes KeyAffixes { get; init; } = KeyAffixes.None;

    /// <summary>The first key that carries a Clause (content run.json affixes.clauseStartKey).</summary>
    public int ClauseStartKey { get; init; }

    /// <summary>The affix rotation index the setup was built with.</summary>
    public int RotationIndex { get; init; }

    /// <summary>The sim's Pair mark rules from content run.json <c>pairMarks</c> (BIBLE section 9.6 item 2).</summary>
    /// <param name="m">The content block (validated at load).</param>
    /// <returns>The rules.</returns>
    public static MarkRules MarkRulesOf(C.PairMarksConfig m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new MarkRules(m.SpreadEffectId, m.SpreadRadiusMm, m.SpreadDamage, m.StackEffectId, m.StackRadiusMm, m.StackDamage);
    }

    /// <summary>
    /// AGGRO-CHASE (finding f-f0c886eb): every pull, pack and patrol spawn with its run.json <c>enemyAggro</c> values
    /// (<see cref="C.EnemyAggroConfig.For"/>; 0 = off, so null on the spawn). Every enemy assists. A group whose ring the
    /// layout authors keeps that ring as its whole aggro rule, so its members carry no radius of their own: every pack and
    /// patrol (layout.json <c>aggroMm</c>, the ring LayoutValidator proves skippable or disjoint from the pulls) and a pull
    /// whose room sets <c>aggroMm</c> (the boss arena). The other pulls (the Choir, the Pair, the Seed behind its room gate)
    /// aggro on each member's own radius beside the default ring. Patrol members never leash (a patrol's home is its moving
    /// loop). Unchanged when the content has no block.
    /// </summary>
    /// <param name="aggro">The run.json block, or null.</param>
    /// <param name="layout">The content layout (which rooms author a ring).</param>
    /// <param name="dungeon">The placed dungeon.</param>
    /// <param name="packs">The layout packs.</param>
    /// <param name="patrols">The layout patrols.</param>
    /// <returns>The three with the values set.</returns>
    public static (DungeonDef Dungeon, IReadOnlyList<PackGroup> Packs, IReadOnlyList<PatrolPack> Patrols) WithEnemyAggro(
        C.EnemyAggroConfig? aggro, C.LayoutDef layout, DungeonDef dungeon, IReadOnlyList<PackGroup> packs, IReadOnlyList<PatrolPack> patrols)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(patrols);
        if (aggro is null)
        {
            return (dungeon, packs, patrols);
        }
        var authored = layout.Boxes.Where(b => b.Pull is not null && b.AggroMm is not null).Select(b => b.Pull!).ToHashSet(StringComparer.Ordinal);
        static long? On(int mm) => mm > 0 ? mm : null;
        List<PullSpawn> Tuned(IEnumerable<PullSpawn> spawns, bool ownRadius, bool leash) => spawns.Select(s =>
        {
            var (aggroMm, assistMm, leashMm) = aggro.For(s.ClassId);
            return s with { AggroMm = ownRadius ? On(aggroMm) : null, AssistMm = On(assistMm), LeashMm = leash ? On(leashMm) : null };
        }).ToList();
        return (
            dungeon with { Pulls = dungeon.Pulls.Select(p => p with { Spawns = Tuned(p.Spawns, !authored.Contains(p.Id), leash: true) }).ToList() },
            packs.Select(k => k with { Spawns = Tuned(k.Spawns, ownRadius: false, leash: true) }).ToList(),
            patrols.Select(k => k with { Spawns = Tuned(k.Spawns, ownRadius: false, leash: false) }).ToList());
    }

    /// <summary>
    /// R-PAIR-1 (ruling on #573): every placed enemy's script with each run.json <c>enemyScriptAt</c> cast moved to fire
    /// first on the given tick from engage (a numeric <c>at</c>; the step keeps its own <c>every</c>, so a loop keeps its
    /// period). The Pair's Spread Mark at 80 and Stack Call at 280 put its mechanic inside a fight the party wins in about
    /// 400 ticks. Unchanged when the content has no block.
    /// </summary>
    /// <param name="scriptAt">The run.json block, or null.</param>
    /// <param name="dungeon">The placed dungeon.</param>
    /// <param name="packs">The layout packs.</param>
    /// <param name="patrols">The layout patrols.</param>
    /// <param name="scripts">Actor id to its script.</param>
    /// <returns>The scripts with the casts moved.</returns>
    public static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> WithScriptAt(
        C.ValueMap<C.ValueMap<int>>? scriptAt, DungeonDef dungeon, IReadOnlyList<PackGroup> packs, IReadOnlyList<PatrolPack> patrols,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(patrols);
        ArgumentNullException.ThrowIfNull(scripts);
        if (scriptAt is null || scriptAt.Count == 0)
        {
            return scripts;
        }
        var classOf = dungeon.Pulls.SelectMany(p => p.Spawns)
            .Concat(packs.SelectMany(k => k.Spawns))
            .Concat(patrols.SelectMany(k => k.Spawns))
            .GroupBy(s => s.ActorId)
            .ToDictionary(g => g.Key, g => g.First().ClassId);
        var moved = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var (actorId, script) in scripts)
        {
            moved[actorId] = classOf.TryGetValue(actorId, out var cls) && scriptAt.TryGetValue(cls, out var casts)
                ? script.Select(s => s.Trigger != StepTrigger.OnDeath && casts.TryGetValue(s.Cast, out var at) ? s with { Trigger = StepTrigger.AtTick, AtTick = at } : s).ToList()
                : script;
        }
        return moved;
    }

    /// <summary>The sim affix table from content run.json <c>affixes</c> (the values are data, pending Matt).</summary>
    /// <param name="a">The content block.</param>
    /// <returns>The table.</returns>
    public static AffixTable AffixTableOf(C.AffixesConfig a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new AffixTable(
            new AffixScale(a.Fortified.HealthBp, a.Fortified.DamageBp),
            new AffixScale(a.Tyrannical.HealthBp, a.Tyrannical.DamageBp),
            a.OneAffixKey,
            a.BothAffixKey);
    }

    /// <summary>
    /// The sim's Heat/Overheat rules (BIBLE section 5 Cinder): one <see cref="OverheatRule"/> per class whose loop is
    /// heat-overheat, its threshold that loop's max, its Chill the content run.json <c>heatOverheat</c> block. A class
    /// with a rule opens the run at 0 Heat and its cast at the threshold is the free Overheat (Combat/Casting.cs).
    /// </summary>
    /// <param name="registry">The loaded content (validated at load).</param>
    /// <returns>Class id to its rule.</returns>
    public static IReadOnlyDictionary<string, OverheatRule> OverheatRulesOf(C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var chill = registry.Run.HeatOverheat;
        return registry.Classes.Values
            .Where(c => c.Loop.Kind == C.LoopKind.HeatOverheat)
            .ToDictionary(c => c.Id, c => new OverheatRule(c.Loop.Max, chill.ChillEffectId, chill.ChillTicks), StringComparer.Ordinal);
    }

    /// <summary>
    /// Plan M5: every enemy ability's damage scaled by its side's affix. The trash side is every ability a pull enemy
    /// lists or casts; the boss side is the boss's and the Twins' (Tyrannical: "the boss and its adds"). An ability on
    /// both sides fails closed, since one shared map cannot hold two scales for it.
    /// </summary>
    private static Dictionary<string, AbilityDef> ScaleEnemyDamage(
        C.ContentRegistry registry, C.DungeonDef dungeonDef, C.EnemyDef boss, Dictionary<string, AbilityDef> abilities, int trashBp, int bossBp)
    {
        static IEnumerable<string> Casts(C.EnemyDef e) => e.Abilities.Concat(e.Script.Select(s => s.Cast));
        var trash = dungeonDef.Pulls
            .SelectMany(p => p.Enemies)
            .Select(g => Enemy(registry, g.EnemyId, $"pull enemy \"{g.EnemyId}\""))
            .SelectMany(Casts)
            .ToHashSet(StringComparer.Ordinal);
        var bossSide = registry.Enemies.Values
            .Where(e => e.Id == boss.Id || (e.BreakBarId is { } bar && boss.BreakBars?.Any(b => b.Id == bar) == true))
            .SelectMany(Casts)
            .ToHashSet(StringComparer.Ordinal);
        if (trash.Intersect(bossSide, StringComparer.Ordinal).FirstOrDefault() is { } shared)
        {
            throw new InvalidOperationException($"run setup: ability \"{shared}\" is cast by trash and by the boss side; affix damage needs one side");
        }
        foreach (var (ids, bp) in new[] { (trash, trashBp), (bossSide, bossBp) })
        {
            foreach (var id in ids.Where(abilities.ContainsKey))
            {
                abilities[id] = Affixes.ScaleDamage(abilities[id], bp);
            }
        }
        return abilities;
    }

    /// <summary>The content layout the setup was placed with (game/content/data/layout.json).</summary>
    public C.LayoutDef? Layout { get; init; }

    /// <summary>
    /// Each bot seat's gear layer (BIBLE s6 parity, D32 gap audit): bot actor id to its <see cref="C.BotGear.Resolve"/>
    /// result, the class's default loadout through the same <see cref="C.GearFacets.Resolve"/> the human's gear uses.
    /// Empty for a solo run.
    /// </summary>
    public IReadOnlyDictionary<int, C.FacetResolution> BotGear { get; init; } = new Dictionary<int, C.FacetResolution>();

    /// <summary>
    /// <see cref="BotGear"/> as <see cref="RunOptions.Loadouts"/> entries: bot actor id to its Facet-patched abilities
    /// converted to sim AbilityDefs (the conversion the run's base abilities go through). SimDriver merges these with the
    /// queued request's (human) loadouts for every run it creates.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>> BotLoadouts { get; init; } =
        new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();

    /// <summary>
    /// <paramref name="options"/> (the queued request's, or null) with every bot seat's <see cref="BotLoadouts"/> entry
    /// added for the seats in <paramref name="party"/>. A loadout the options already carry for an actor wins.
    /// </summary>
    /// <param name="options">The run options, or null.</param>
    /// <param name="party">The run's party.</param>
    /// <returns>The options to create the run with (unchanged when no bot seat has gear).</returns>
    public RunOptions? WithBotLoadouts(RunOptions? options, IReadOnlyList<PartyMemberSpec> party)
    {
        ArgumentNullException.ThrowIfNull(party);
        var seats = party.Select(p => p.Id).Where(id => BotLoadouts.TryGetValue(id, out var kit) && kit.Count > 0).ToList();
        if (seats.Count == 0)
        {
            return options;
        }
        var merged = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();
        foreach (var id in seats)
        {
            merged[id] = BotLoadouts[id];
        }
        if (options?.Loadouts is { } carried)
        {
            foreach (var kv in carried)
            {
                merged[kv.Key] = kv.Value;
            }
        }
        return (options ?? new RunOptions()) with { Loadouts = merged };
    }

    // Every bot seat's default gear through C.BotGear.Resolve (GearFacets.Resolve). The refuse check is what the sim
    // conversion cannot play (a line width, an op the sim union lacks): that Facet is skipped and reported, never a
    // refused run, as the human's gear layer does (Screen.RunLoadout).
    private static IReadOnlyDictionary<int, C.FacetResolution> BuildBotGear(C.ContentRegistry registry, IReadOnlyList<PartyMemberSpec> party)
    {
        var gear = new Dictionary<int, C.FacetResolution>();
        foreach (var seat in party.Where(p => p.Id != HumanId).OrderBy(p => p.Id))
        {
            gear[seat.Id] = C.BotGear.Resolve(registry, registry.Classes[seat.ClassId], RefuseUnplayable);
        }
        return gear;
    }

    private static string? RefuseUnplayable(C.AbilityDef a)
    {
        if (a.Shape.Width is not null)
        {
            return "line width is not in the sim shape";
        }
        try
        {
            ToSim(a);
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// The options a run is created with (BIBLE s7, finding f-f9b80112): the request's Clause and talents plus the
    /// human's equipped gear, every slot and the keystone included, each Facet's content patch stacked on the human's
    /// (talented) kit (<see cref="RunStart.Of"/> over <see cref="GearPatches"/>). SimDriver.StartRun passes
    /// <see cref="RunStart.Options"/> to <see cref="RunRoot.CreateRun"/>. With no progression (no screen yet) or nothing
    /// equipped the options are the request's alone, so such a run is created exactly as before.
    /// </summary>
    /// <param name="request">The queued request, or null.</param>
    /// <param name="progression">The screen's progression, or null.</param>
    /// <param name="registry">The content.</param>
    /// <param name="humanClassId">The human's class (scopes the Facets and fills an appended Window's setupClass).</param>
    /// <returns>The run start.</returns>
    public RunStart StartFor(QueueRequest? request, Progression? progression, C.ContentRegistry registry, string humanClassId) =>
        RunStart.Of(
            request,
            progression,
            HumanActorId,
            Content.Abilities,
            progression is null ? new Dictionary<string, GearPatch>(StringComparer.Ordinal) : GearPatches(registry, humanClassId));

    /// <summary>
    /// The sim patch of every content Facet on <paramref name="classId"/>'s kit (content/schema/facet.ts FacetPatch to
    /// <see cref="GearPatch"/>, ops through the same <c>ToSim</c> the base abilities use). An appended Window op without
    /// a setupClass is the wearer's class Window (every base Window op of a class carries that class id), so it gets
    /// <paramref name="classId"/>. A line shape's full width becomes the sim line's half-width radius
    /// (<see cref="Abilities.LineShape"/>), so a width Facet (twinStrike, infernoBolt) is applied, not just carried.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="classId">The wearer's class.</param>
    /// <returns>Patches by Facet id.</returns>
    /// <exception cref="InvalidOperationException">The class is unknown, or a Facet op is one the sim does not run.</exception>
    public static IReadOnlyDictionary<string, GearPatch> GearPatches(C.ContentRegistry registry, string classId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!registry.Classes.TryGetValue(classId, out var classDef))
        {
            throw new InvalidOperationException($"run setup: unknown class \"{classId}\"");
        }
        var kit = new HashSet<string>(ResolveSlots(classDef).Select(s => s.AbilityId), StringComparer.Ordinal);
        var patches = new Dictionary<string, GearPatch>(StringComparer.Ordinal);
        foreach (var facet in registry.Facets.Values.Where(f => kit.Contains(f.AbilityId)).OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            var p = facet.Patch;
            var where = $"facet \"{facet.Id}\"";
            IReadOnlyList<EffectOp>? Ops(IEnumerable<C.EffectOp>? ops) => ops?.Select(o => ToSim(
                o is C.WindowOp { SetupClass: null } w ? w with { SetupClass = classId } : o, where)).ToList();
            patches[facet.Id] = new GearPatch
            {
                CastTicks = p.CastTicks,
                GcdTicks = p.GcdTicks,
                CooldownTicks = p.CooldownTicks,
                Charges = p.Charges,
                Cost = p.Cost is { } c ? new AbilityCost(c.Resource, c.Amount) : null,
                Range = p.Range,
                Shape = p.Shape is { } s ? ToSim(s, where) : null,
                Interruptible = p.Interruptible,
                Tags = p.Tags?.ToList(),
                Effects = Ops(p.Effects),
                Variants = p.Variants?.ToDictionary(kv => kv.Key, kv => Ops(kv.Value)!, StringComparer.Ordinal),
                TagAdd = p.TagAdd?.ToList(),
                TagRemove = p.TagRemove?.ToList(),
                EffectsAppend = Ops(p.EffectsAppend),
            };
        }
        return patches;
    }

    /// <summary>
    /// The party for a run over <paramref name="runDungeon"/> (the setup's dungeon, or a <c>--pull N</c> slice of it):
    /// <see cref="Party"/> in the Entrance for a run from the first pull, else the same seats at the start of the first
    /// pull's room, inside its trigger, so the sliced run begins in its fight as the flat arena's did.
    /// </summary>
    /// <param name="runDungeon">The dungeon the run is created with.</param>
    /// <returns>The placed party.</returns>
    public IReadOnlyList<PartyMemberSpec> PartyFor(DungeonDef runDungeon)
    {
        ArgumentNullException.ThrowIfNull(runDungeon);
        if (Layout is null || runDungeon.Pulls.Count == 0 || runDungeon.Pulls[0].Id == Dungeon.Pulls[0].Id)
        {
            return Party;
        }
        return NavLayouts.PlaceParty(Layout, Party, runDungeon.Pulls[0].Id);
    }

    /// <summary>
    /// The armed boss rules from the boss's content entry (content/data/enemies/hollow-warden.json for hollow-vault):
    /// the boss's one Break bar becomes the Twins' shared bar, the two enemies whose <c>breakBarId</c> names it are the
    /// Twins (ids from <see cref="TwinActorIdStart"/> in content id order, Tempo-scaled health and scripts, flanking the
    /// boss at +/-<see cref="TwinOffsetMm"/> x), and the boss ability tagged <see cref="LockTag"/> gives the Lock its
    /// Window length (its window op), shield effect (its applyEffect op) and the script cast the armed rules suppress.
    /// The Lock is cast (<see cref="BossConfig.CastLock"/>, LOCK-TELEGRAPH): its castTicks and its script step's
    /// telegraph give it a cast bar and a floor circle before the Window opens. The 50% gate, 30 s repeat, three-fail enrage and its Tempo spike are BIBLE s9 structure (the
    /// <see cref="BossConfig"/> defaults; content has no field for them). Fails closed on any missing or extra piece.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="boss">The dungeon's boss enemy.</param>
    /// <param name="keyLevel">The key level (Tempo scaling of the Twins, as the boss pull is scaled).</param>
    /// <returns>The config for <see cref="BossRules.Arm"/>.</returns>
    /// <exception cref="InvalidOperationException">The boss has not exactly one Break bar, the bar has not exactly two
    /// Twins, an onEmpty op is not applyEffect, or the boss has not exactly one Lock ability with one window and one
    /// applyEffect op.</exception>
    /// <param name="twinHealthBp">Tyrannical's health on the Twins, bp (plan M5: "the boss and its adds"); 10000 for none.</param>
    public static BossConfig BuildBossConfig(C.ContentRegistry registry, C.EnemyDef boss, int keyLevel, int twinHealthBp = 10000)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(boss);
        var where = $"boss \"{boss.Id}\"";
        if (boss.BreakBars is not { Count: 1 } bars)
        {
            throw new InvalidOperationException($"run setup: {where} must declare exactly one Break bar (the Twins' shared bar)");
        }
        var bar = bars[0];
        var twinDefs = registry.Enemies.Values
            .Where(e => string.Equals(e.BreakBarId, bar.Id, StringComparison.Ordinal))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        if (twinDefs.Count != 2)
        {
            throw new InvalidOperationException($"run setup: {where} bar \"{bar.Id}\" is shared by {twinDefs.Count} enemies, expected the two Twins");
        }
        var twins = twinDefs.Select((def, i) => new BossTwinSpec(
            TwinActorIdStart + i,
            def.Id,
            Affixes.ScaleHealth(Tempo.ScaleHealth(def.MaxHealth, keyLevel), twinHealthBp),
            new Vec3(i == 0 ? TwinOffsetMm : -TwinOffsetMm, 0, 0),
            Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel)))).ToList();
        var onEmpty = bar.OnEmpty.Select(op => op is C.ApplyEffectOp e
            ? new BreakBarOnEmptyEffect(e.EffectId, e.DurationTicks, e.Stacks)
            : throw new InvalidOperationException($"run setup: {where} bar \"{bar.Id}\" onEmpty has a {op.GetType().Name}, only applyEffect is run")).ToList();
        var twinsBar = new BreakBarSource(bar.Id, twins.Select(t => t.ActorId).ToList(), bar.Max, bar.DrainPerInterrupt, bar.DrainPerControl, onEmpty);

        var locks = boss.Abilities
            .Select(id => registry.Abilities.TryGetValue(id, out var a) ? a : throw new InvalidOperationException($"run setup: {where} lists unknown ability \"{id}\""))
            .Where(a => a.Tags.Contains(LockTag))
            .ToList();
        if (locks.Count != 1)
        {
            throw new InvalidOperationException($"run setup: {where} has {locks.Count} abilities tagged \"{LockTag}\", expected one Lock");
        }
        var lockAbility = locks[0];
        var window = lockAbility.Effects.OfType<C.WindowOp>().ToList();
        var shield = lockAbility.Effects.OfType<C.ApplyEffectOp>().ToList();
        if (window.Count != 1 || shield.Count != 1)
        {
            throw new InvalidOperationException($"run setup: Lock \"{lockAbility.Id}\" needs one window op and one applyEffect op (has {window.Count} and {shield.Count})");
        }
        return new BossConfig(BossActorId, twins, twinsBar)
        {
            LockWindowTicks = window[0].DurationTicks,
            ShieldEffectId = shield[0].EffectId,
            SuppressedBossCasts = new[] { lockAbility.Id },
            CastLock = true,
        };
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
            party.Add(new PartyMemberSpec(id, cls.Id, slot.Role, new Vec3(slot.SpawnX, 0, WardenSpawn.Z), slot.MaxHealth, cls.Loop.Max)
            {
                Yaw = WardenYaw,
                // design/BIBLE.md section 3.1: the class's passive armor from run.json classArmorBp.
                ArmorBp = registry.Run.ClassArmorBp.TryGetValue(cls.Id, out var armorBp) ? armorBp : 0,
            });
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

    // run-setup.ts:61 resolveVariant: a stance ability's static Effects are its "mend" variant (the opening verse).
    // BIBLE section 5: the variants also ride along, and the sim (Abilities.ResolveVerse) plays the caster's verse.
    private static AbilityDef ToSim(C.AbilityDef a)
    {
        var ops = a.Variants is { } v && v.TryGetValue("mend", out var mend) ? mend : a.Effects;
        Dictionary<string, IReadOnlyList<EffectOp>>? variants = null;
        if (a.Variants is { } all)
        {
            variants = new Dictionary<string, IReadOnlyList<EffectOp>>(StringComparer.Ordinal);
            foreach (var kv in all)
            {
                variants[kv.Key] = kv.Value.Select(o => ToSim(o, a.Id + " variant " + kv.Key)).ToList();
            }
        }
        return new AbilityDef
        {
            Id = a.Id,
            CastTicks = a.CastTicks,
            GcdTicks = a.GcdTicks,
            CooldownTicks = a.CooldownTicks,
            Charges = a.Charges,
            Cost = new AbilityCost(a.Cost.Resource, a.Cost.Amount),
            Range = a.Range,
            Shape = ToSim(a.Shape, a.Id),
            Interruptible = a.Interruptible,
            Effects = ops.Select(o => ToSim(o, a.Id)).ToList(),
            Tags = a.Tags.ToList(),
            Variants = variants,
        };
    }

    // A content shape in sim terms. A width (a Facet's line: twinStrike, infernoBolt) is the line's full width, and the
    // sim line's radius is its half-width (Abilities.LineShape); a width on any other kind fails closed.
    private static AbilityShape ToSim(C.AbilityShape s, string where)
    {
        if (s.Width is not { } width)
        {
            return new AbilityShape(ToSim(s.Kind), s.Radius, s.Angle1024, s.Length);
        }
        if (s.Kind != C.ShapeKind.Line)
        {
            throw new InvalidOperationException($"run setup: {where} has a width on a {s.Kind} shape (only a line has one)");
        }
        return Abilities.LineShape(s.Length ?? 0, width);
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

    // The content-to-sim op mapping lives in Keyrun.Bots.Shared.ContentCombos so the headless tests run it (f-89b0825e).
    private static EffectOp ToSim(C.EffectOp op, string where) => ContentCombos.ToSimOp(op, where);

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
    /// content/sim-adapt.ts toSimDungeon (ids from <see cref="PullActorIdStart"/> in pull, group, count order; yaw 0;
    /// speed and chase margin only when the enemy sets them) plus content-dungeon.ts buildDungeon: Tempo-scaled health
    /// and scripts, and the boss appended as one more pull at <see cref="BossActorId"/>.
    /// </summary>
    private static (DungeonDef Dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts) ToSimDungeon(
        C.ContentRegistry registry, C.DungeonDef dungeonDef, int keyLevel, int trashHealthBp, int bossHealthBp)
    {
        // Plan M5: Fortified (trash) and Tyrannical (boss) health on top of Tempo, applied at spawn.
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
                    spawns.Add(ScaledTrashSpawn(def, actorId, new Vec3(group.Spawn.X, 0, group.Spawn.Z), keyLevel, trashHealthBp));
                    scripts[actorId] = Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel));
                }
            }
            pulls.Add(new PullDef(pull.Id, spawns));
        }

        var boss = Enemy(registry, dungeonDef.Boss.EnemyId, $"dungeon \"{dungeonDef.Id}\" boss");
        // The boss pull's id is the layout's boss room key; NavLayouts.PlaceDungeon (Build, after this) stands it at the
        // arena origin and gives it the arena's aggroMm (layout.json), the same helper the bots use.
        pulls.Add(new PullDef(C.LayoutDef.BossPullId, new[]
        {
            new PullSpawn(BossActorId, boss.Id, Vec3.Zero, BossYaw, Affixes.ScaleHealth(Tempo.ScaleHealth(boss.MaxHealth, keyLevel), bossHealthBp)),
        }));
        scripts[BossActorId] = Tempo.ScaleScript(ToSim(boss.Script), keyLevel, CastFloors(registry, keyLevel));
        return (new DungeonDef(dungeonDef.Id, pulls), scripts);
    }

    /// <summary>
    /// TEMPO-CAST-FLOOR: ability id to <see cref="Tempo.ScaledCastFloorTicks"/> (the key-scaled cooldown and the cast
    /// time), so Tempo never schedules a cast before its ability is ready (BIBLE s3) while the cooldown itself scales
    /// with the key (<see cref="Tempo.ScaleEnemyCooldowns"/>, BIBLE s8). An id the registry lacks gets 0 (no extra floor).
    /// </summary>
    private static Func<string, int> CastFloors(C.ContentRegistry registry, int keyLevel) =>
        id => registry.Abilities.TryGetValue(id, out var a) ? Tempo.ScaledCastFloorTicks(a.CooldownTicks, a.CastTicks, keyLevel) : 0;

    /// <summary>
    /// One trash spawn as content/sim-adapt.ts toSimDungeon builds it (yaw 0; speed and chase margin only when the enemy
    /// sets them), Tempo-scaled, then Fortified (trash health affix) on top.
    /// </summary>
    private static PullSpawn ScaledTrashSpawn(C.EnemyDef def, int actorId, Vec3 pos, int keyLevel, int trashHealthBp)
    {
        var spawn = new PullSpawn(actorId, def.Id, pos, 0, def.MaxHealth)
        {
            SpeedMmPerTick = def.SpeedMmPerTick,
            ChaseMarginMm = def.ChaseMarginMm,
        };
        var scaled = Tempo.ScaleSpawn(spawn, keyLevel);
        return scaled with { Health = Affixes.ScaleHealth(scaled.Health, trashHealthBp) };
    }

    /// <summary>
    /// PATH-PACKS: a scaled template (<see cref="ScaledTrashSpawn"/>, Tempo-scaled script floored against each cast's
    /// key-scaled cooldown, <see cref="CastFloors"/>, as a pull's script is) for every layout pack enemy no
    /// pull of <paramref name="dungeonDef"/> fields, such as the Hall Sentry; an enemy a pull fields is copied from that
    /// pull's spawn by <see cref="LayoutPackGroups.Build"/> instead. Fails closed on an enemy id content does not define.
    /// </summary>
    private static IReadOnlyDictionary<string, PackTemplate> PackOnlyTemplates(
        C.ContentRegistry registry, C.LayoutDef layout, C.DungeonDef dungeonDef, int keyLevel, int trashHealthBp)
    {
        var fielded = dungeonDef.Pulls.SelectMany(p => p.Enemies).Select(e => e.EnemyId).ToHashSet(StringComparer.Ordinal);
        var templates = new SortedDictionary<string, PackTemplate>(StringComparer.Ordinal);
        foreach (var pack in layout.Packs)
        {
            foreach (var group in pack.Enemies.Where(g => !fielded.Contains(g.EnemyId) && !templates.ContainsKey(g.EnemyId)))
            {
                var def = Enemy(registry, group.EnemyId, $"pack \"{pack.Id}\"");
                templates[def.Id] = new PackTemplate(
                    ScaledTrashSpawn(def, 0, Vec3.Zero, keyLevel, trashHealthBp),
                    Tempo.ScaleScript(ToSim(def.Script), keyLevel, CastFloors(registry, keyLevel)));
            }
        }
        return templates;
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
                ? new TelegraphSpec(ShapeName(t.Shape), t.LeadTicks) { Radius = t.Radius, Angle1024 = t.Angle1024, Length = t.Length, Width = t.Width, SpansCast = t.SpansCast ?? false, AnchorTarget = t.Anchor == C.TelegraphAnchor.Target }
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
