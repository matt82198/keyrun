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
public sealed partial record HostRunSetup(
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
    /// R-MULTIBOSS-1: the gap between consecutive bosses' actor ids (the boss, then its adds at +1 to +<see cref="MaxBossAdds"/>),
    /// so a later boss's id never collides with an earlier boss's adds. The first boss keeps <see cref="BossActorId"/>.
    /// </summary>
    public const int BossActorIdStride = 10;

    /// <summary>R-MULTIBOSS-1: the actor id of the boss fought <paramref name="index"/>th (0 = <see cref="BossActorId"/>).</summary>
    /// <param name="index">The boss's 0-based fight-order index.</param>
    public static int BossActorIdOf(int index) => BossActorId + (index * BossActorIdStride);

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
    /// <param name="bossEnemyId">
    /// D66 boss arena: the boss enemy to stand in the dungeon's boss arena instead of the dungeon's own boss, or null (the
    /// dungeon's boss). Lets a boss of a dungeon with no laid-out map yet (the Sunken Bellfoundry, whose rooms the seeded
    /// generator places) be fought, and measured against R-BOSS-1, in the tutorial's arena with the real party and bots.
    /// </param>
    /// <param name="dungeonSeed">
    /// R-MULTIBOSS-1: the seed a multi-segment dungeon (the Sunken Bellfoundry) is generated, filled and laid out from
    /// (<see cref="SeededLayout.ArmSegmented"/>); required for such a dungeon, ignored for the hand-built Hollow Vault.
    /// </param>
    /// <returns>The setup.</returns>
    public static HostRunSetup Build(C.ContentRegistry registry, string dungeonId, int keyLevel, string humanClassId, bool withBots = true, int rotationIndex = 0, string? bossEnemyId = null, long? dungeonSeed = null)
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
        // R-MULTIBOSS-1: a multi-segment dungeon has one boss per segment, in fight order, each at its own actor id and in its
        // own boss room of the seeded layout; every other dungeon (and a D66 boss-arena override) has the one boss "boss".
        var multi = bossEnemyId is null && dungeonDef.Segments.Count > 1;
        if (multi)
        {
            registry = SeededLayout.ArmSegmented(
                registry, dungeonDef.Id, dungeonSeed ?? throw new InvalidOperationException($"run setup: dungeon \"{dungeonDef.Id}\" is generated by seed; pass dungeonSeed"));
        }
        var bossPullIds = multi ? C.LayoutDef.BossPullIds(dungeonDef) : new[] { C.LayoutDef.BossPullId };
        var bossSlots = multi
            ? dungeonDef.Segments.Select((s, i) => new BossSlot(bossPullIds[i], Enemy(registry, s.Boss.EnemyId, $"dungeon \"{dungeonDef.Id}\" segment \"{s.Id}\" boss"), BossActorIdOf(i))).ToList()
            : new List<BossSlot> { new(C.LayoutDef.BossPullId, Enemy(registry, bossEnemyId ?? dungeonDef.Boss.EnemyId, $"dungeon \"{dungeonDef.Id}\" boss"), BossActorId) };
        if (bossSlots.FirstOrDefault(s => s.Def.Role != C.EnemyRole.Boss) is { } notBoss)
        {
            throw new InvalidOperationException($"run setup: boss \"{notBoss.Def.Id}\" has role {notBoss.Def.Role}, not boss");
        }
        var abilities = ScaleEnemyDamage(
            registry,
            dungeonDef,
            bossSlots.Select(s => s.Def).ToList(),
            registry.Abilities.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal),
            Affixes.DamageBp(affixTable, affixes, bossSide: false),
            Affixes.DamageBp(affixTable, affixes, bossSide: true));
        var effects = registry.Effects.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);
        var (combos, fieldCombos) = ContentCombos.ToComboTables(registry.Combos.Values, registry.Classes.Keys.ToList(), registry.ComboRiders);
        var (dungeon, scripts) = ToSimDungeon(
            registry, dungeonDef, bossSlots, keyLevel, Affixes.HealthBp(affixTable, affixes, bossSide: false), Affixes.HealthBp(affixTable, affixes, bossSide: true));
        var clauses = registry.Clauses.ToDictionary(kv => kv.Key, kv => ToSim(kv.Value), StringComparer.Ordinal);
        var bossConfigs = bossSlots.Select(s => BuildBossConfig(registry, s.Def, keyLevel, Affixes.HealthBp(affixTable, affixes, bossSide: true), s.ActorId)).ToList();
        var boss = bossConfigs[0];

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
        abilities = Tempo.ScaleEnemyCooldowns(abilities, allScripts.Values.Concat(bossConfigs.SelectMany(c => c.Twins.Select(t => t.Script))), keyLevel);
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
            ForcesBar = ForcesBarOf(registry),
        }.WithMovement(movement);

        // HUMAN-PLAY (D65/D66): a party whose human is not the tank starts in its travel formation behind the human.
        var party = InFormation(layout, NavLayouts.PlaceParty(layout, BuildParty(registry, classDef.Id, withBots), dungeon.Pulls[0].Id));
        var botGear = BuildBotGear(registry, party);
        return new HostRunSetup(party, dungeon, content, HumanId, ResolveSlots(classDef), boss)
        {
            Layout = layout,
            NextBosses = bossConfigs.Skip(1).ToList(),
            KeyAffixes = affixes,
            ClauseStartKey = registry.Run.Affixes.ClauseStartKey,
            RotationIndex = rotationIndex,
            BotGear = botGear,
            BotLoadouts = botGear.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, AbilityDef>)kv.Value.Abilities.ToDictionary(a => a.Key, a => ToSim(a.Value), StringComparer.Ordinal)),
        };
    }

    /// <summary>
    /// R-FORCES-COMPLETE-1 (WoW Mythic+): the run's Enemy Forces bar from forces.json. A key completes only once the bar is
    /// full AND the final boss is dead, so the run keeps going after a final boss killed below 100%.
    /// </summary>
    /// <param name="registry">The validated content.</param>
    /// <returns>The bar, or null when content has no forces table.</returns>
    private static RunForces? ForcesBarOf(C.ContentRegistry registry)
    {
        if (registry.Forces is not { } forces)
        {
            return null;
        }
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in forces.Values)
        {
            table[v.EnemyId] = v.Points;
        }
        return new RunForces(table, forces.Required);
    }

    /// <summary>
    /// R-MULTIBOSS-1: the bosses after <see cref="Boss"/>, in fight order (empty for a one-boss dungeon). The factory arms the
    /// whole list (<see cref="BossRules.ArmAll"/>): each boss's rules arm when the one before it dies.
    /// </summary>
    public IReadOnlyList<BossConfig> NextBosses { get; init; } = Array.Empty<BossConfig>();

    /// <summary>The key's Fortified/Tyrannical affixes this setup was scaled with (plan M5; the screen shows them).</summary>
    public KeyAffixes KeyAffixes { get; init; } = KeyAffixes.None;

    /// <summary>The first key that carries a Clause (content run.json affixes.clauseStartKey).</summary>
    public int ClauseStartKey { get; init; }

    /// <summary>The affix rotation index the setup was built with.</summary>
    public int RotationIndex { get; init; }

    /// <summary>The content layout the setup was placed with (game/content/data/layout.json).</summary>
    public C.LayoutDef? Layout { get; init; }
}
