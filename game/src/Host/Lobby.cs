using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>The three tabs of the between-runs screen, in the order the tab bar draws them.</summary>
public enum LobbyTab
{
    /// <summary>The class pick and the five gear slots with tier (BIBLE s5, s7).</summary>
    Character = 0,

    /// <summary>The chosen class's talent forks (BIBLE s6).</summary>
    Build = 1,

    /// <summary>The key, its Clause and the Queue button (BIBLE s8, s10).</summary>
    Play = 2,
}

/// <summary>
/// The selection state of the one between-runs screen (BIBLE s1 "Screen -> Queue -> Dungeon -> End-of-run choice of
/// three -> Screen"; Matt 2026-10-20 "just one screen with tabs should be enough before entering a game/queue"). It
/// holds the open tab, the human's class, that class's talent build and the Key band (<see cref="ScreenState"/>); the
/// character's save (<see cref="Progression"/>: Ash, gear, saved talents) stays the caller's and is passed in where it
/// is read. Pure, immutable and Godot-free: the Godot <c>Screen</c> renders it and maps input onto it, and every refused
/// change returns this same instance so a caller can tell a refusal by reference.
/// </summary>
public sealed record Lobby
{
    /// <summary>The class the human plays when nothing else is picked (the host's historical solo Warden).</summary>
    public const string DefaultClassId = "warden";

    /// <summary>The tabs in draw order.</summary>
    public static readonly IReadOnlyList<LobbyTab> Tabs = new[] { LobbyTab.Character, LobbyTab.Build, LobbyTab.Play };

    private Lobby(LobbyTab tab, string classId, C.TalentPicks talents, ScreenState key)
    {
        Tab = tab;
        ClassId = classId;
        Talents = talents;
        Key = key;
    }

    /// <summary>The open tab.</summary>
    public LobbyTab Tab { get; private init; }

    /// <summary>The human's class id.</summary>
    public string ClassId { get; private init; }

    /// <summary>The human's talent build for <see cref="ClassId"/>.</summary>
    public C.TalentPicks Talents { get; private init; }

    /// <summary>The Key band: key, Clause, and the queue lock.</summary>
    public ScreenState Key { get; private init; }

    /// <summary>True once Queue was pressed (the class and the key are locked until the next run starts).</summary>
    public bool Locked => Key.Locked;

    /// <summary>
    /// Opens the screen on the Play tab, unlocked, at <paramref name="keyLevel"/>, as <paramref name="classId"/> with that
    /// class's saved build restored from <paramref name="progression"/> (a pick the content no longer has is dropped).
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="progression">The character's save (its saved talents).</param>
    /// <param name="dungeonId">The dungeon.</param>
    /// <param name="clauseIds">Every Clause id content defines.</param>
    /// <param name="keyLevel">The key (clamped to 2-20).</param>
    /// <param name="classId">The human's class.</param>
    /// <returns>The lobby.</returns>
    /// <exception cref="InvalidOperationException">The class is not in content (fail closed).</exception>
    public static Lobby Open(
        C.ContentRegistry registry,
        Progression progression,
        string dungeonId,
        IReadOnlyList<string> clauseIds,
        int keyLevel,
        string classId = DefaultClassId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        if (!registry.Classes.TryGetValue(classId, out var cls))
        {
            throw new InvalidOperationException($"lobby: unknown class \"{classId}\"");
        }
        return new Lobby(LobbyTab.Play, cls.Id, C.TalentPicks.Restore(cls, progression.Talents), ScreenState.Open(dungeonId, clauseIds, keyLevel));
    }

    /// <summary>The classes the Character tab offers: the host roster's classes that content defines, in roster order.</summary>
    /// <param name="registry">The content.</param>
    /// <returns>Class ids.</returns>
    public static IReadOnlyList<string> ClassIds(C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return HostRunSetup.Roster.Select(s => s.ClassId).Where(registry.Classes.ContainsKey).ToList();
    }

    /// <summary>
    /// The class a run plays (SimDriver.StartRun's read): for a run the screen's Queue started, the screen's class;
    /// for a CLI run (no request) or with no screen, <see cref="DefaultClassId"/>. The class stays on the host and is
    /// never part of the queue payload (<see cref="QueueRequest"/>; BIBLE s3 "The queue matches on key level only").
    /// The screen raises its Queue event while still locked on the class it queued, so the class read here is that one.
    /// </summary>
    /// <param name="request">The queued request, or null for a CLI run.</param>
    /// <param name="screen">The screen's lobby, or null.</param>
    /// <returns>The class id.</returns>
    public static string ClassFor(QueueRequest? request, Lobby? screen) =>
        request is not null && screen is not null ? screen.ClassId : DefaultClassId;

    /// <summary>
    /// The host setup a queued run is built on (SimDriver.StartRun's call): the request's dungeon and key, the party
    /// built around <paramref name="classId"/> (<see cref="HostRunSetup.BuildParty"/>: the human takes that class's
    /// roster seat and bots fill the others).
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="request">The request.</param>
    /// <param name="classId">The human's class (<see cref="ClassFor"/>).</param>
    /// <param name="withBots">False for the solo seat.</param>
    /// <param name="rotationIndex">The affix rotation index.</param>
    /// <returns>The setup.</returns>
    public static HostRunSetup SetupFor(C.ContentRegistry registry, QueueRequest request, string classId, bool withBots = true, int rotationIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HostRunSetup.Build(registry, request.DungeonId, request.KeyLevel, classId, withBots, rotationIndex);
    }

    /// <summary>
    /// One gear slot's listed Facet pool for a class kit: the content Facets of <paramref name="slot"/> that patch an
    /// ability in <paramref name="kit"/>, ordered by id, as sim <see cref="Facet"/>s (the reroll pool and the run-end roll).
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="kit">The class kit's ability ids.</param>
    /// <param name="slot">A gear slot ("weapon", "armor", ...).</param>
    /// <returns>The pool, possibly empty.</returns>
    public static IReadOnlyList<Facet> SlotPool(C.ContentRegistry registry, IReadOnlySet<string> kit, string slot)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(kit);
        return registry.Facets
            .Select(kv => kv.Value)
            .Where(f => f.Slot.ToString().ToLowerInvariant() == slot && kit.Contains(f.AbilityId))
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => new Facet(f.Id, f.Text, f.AbilityId))
            .ToList();
    }

    /// <summary>Every slot's pool for <paramref name="classId"/>'s kit (<see cref="SlotPool"/>), keyed by slot.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="classId">The class.</param>
    /// <returns>Slot to pool.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<Facet>> FacetPools(C.ContentRegistry registry, string classId)
    {
        var kit = Kit(registry, classId);
        return Rewards.GearSlots.ToDictionary(s => s, s => SlotPool(registry, kit, s), StringComparer.Ordinal);
    }

    /// <summary>The class's nine keybind abilities (<see cref="HostRunSetup.ResolveSlots"/>) as a set.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="classId">The class.</param>
    /// <returns>The ability ids.</returns>
    public static IReadOnlySet<string> Kit(C.ContentRegistry registry, string classId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!registry.Classes.TryGetValue(classId, out var cls))
        {
            throw new InvalidOperationException($"lobby: unknown class \"{classId}\"");
        }
        return new HashSet<string>(HostRunSetup.ResolveSlots(cls).Select(s => s.AbilityId), StringComparer.Ordinal);
    }

    /// <summary>Opens <paramref name="tab"/>. Tabs can be browsed while queued; an undefined tab is refused.</summary>
    /// <param name="tab">The tab.</param>
    /// <returns>The lobby on that tab.</returns>
    public Lobby SelectTab(LobbyTab tab) => !Enum.IsDefined(tab) ? this : tab == Tab ? this : this with { Tab = tab };

    /// <summary>Moves <paramref name="delta"/> tabs along the bar, wrapping at both ends.</summary>
    /// <param name="delta">Steps, negative for left.</param>
    /// <returns>The lobby on that tab.</returns>
    public Lobby CycleTab(int delta)
    {
        var i = Tabs.ToList().IndexOf(Tab);
        var n = Tabs.Count;
        return SelectTab(Tabs[(((i + delta) % n) + n) % n]);
    }

    /// <summary>
    /// Plays <paramref name="classId"/>: the build becomes that class's saved build from <paramref name="progression"/>
    /// (respec is free, so switching back restores what was saved). Refused while queued, for an unknown class, and for
    /// the class already picked.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="progression">The character's save.</param>
    /// <param name="classId">The class.</param>
    /// <returns>The lobby, or this one when refused.</returns>
    public Lobby PickClass(C.ContentRegistry registry, Progression progression, string classId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        if (Locked || classId == ClassId || !ClassIds(registry).Contains(classId))
        {
            return this;
        }
        var cls = registry.Classes[classId];
        return this with { ClassId = cls.Id, Talents = C.TalentPicks.Restore(cls, progression.Talents) };
    }

    /// <summary>Replaces the build (a pick or a respec on the Build tab); a build of another class is refused.</summary>
    /// <param name="talents">The new build.</param>
    /// <returns>The lobby, or this one when refused.</returns>
    public Lobby WithTalents(C.TalentPicks talents)
    {
        ArgumentNullException.ThrowIfNull(talents);
        return talents.ClassId != ClassId ? this : this with { Talents = talents };
    }

    /// <summary>
    /// The save with this class's build written over that class's forks; every other class's saved picks are kept, so
    /// switching classes never loses a build.
    /// </summary>
    /// <param name="progression">The save.</param>
    /// <returns>The new save.</returns>
    public Progression SaveTalents(Progression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        var mine = new HashSet<string>(Talents.Forks.Select(f => f.AbilityId), StringComparer.Ordinal);
        var kept = progression.Talents.Where(kv => !mine.Contains(kv.Key));
        return progression.WithTalents(kept.Concat(Talents.Choices).ToList());
    }

    /// <summary>Steps the key (<see cref="ScreenState.StepKey"/>).</summary>
    /// <param name="delta">Steps.</param>
    /// <returns>The lobby, or this one when the band refused.</returns>
    public Lobby StepKey(int delta) => WithKey(Key.StepKey(delta));

    /// <summary>Picks a Clause offer (<see cref="ScreenState.PickClause"/>).</summary>
    /// <param name="clauseId">The Clause id.</param>
    /// <returns>The lobby, or this one when the band refused.</returns>
    public Lobby PickClause(string clauseId) => WithKey(Key.PickClause(clauseId));

    /// <summary>
    /// Queue: the locked lobby (still on <see cref="ClassId"/>, which <see cref="ClassFor"/> reads) and the request
    /// (<see cref="QueuedKit.Queue"/> for this class's build and the equipped gear), or null when already queued.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="progression">The character's save (equipped gear).</param>
    /// <returns>The locked lobby and the request.</returns>
    public (Lobby Lobby, QueueRequest? Request) Queue(C.ContentRegistry registry, Progression progression)
    {
        var (locked, request) = QueuedKit.Queue(Key, registry, Talents, progression, ClassId);
        return request is null ? (this, null) : (this with { Key = locked }, request);
    }

    /// <summary>
    /// Back on the screen after <paramref name="run"/> ended (the s1 loop's last arrow): the Play tab, unlocked, at the
    /// key the run moved to (<see cref="KeyMove.ForRun"/>: a timed run raises it, any other end keeps it), class and build
    /// kept, so the next Queue is one press. A run that has not ended returns this lobby.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The lobby.</returns>
    public Lobby ReturnFrom(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return KeyMove.ForRun(run) is { } move ? Reopen(move.To) : this;
    }

    /// <summary>The screen reopened at <paramref name="keyLevel"/>: Play tab, unlocked, first Clause offer picked.</summary>
    /// <param name="keyLevel">The key.</param>
    /// <returns>The lobby.</returns>
    public Lobby Reopen(int keyLevel) =>
        this with { Tab = LobbyTab.Play, Key = ScreenState.Open(Key.DungeonId, Key.ClauseIds, keyLevel) };

    private Lobby WithKey(ScreenState next) => ReferenceEquals(next, Key) ? this : this with { Key = next };
}
