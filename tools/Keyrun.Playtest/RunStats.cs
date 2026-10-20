using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>A player death: when, who, and the enemy ability whose hit downed them.</summary>
/// <param name="Tick">The death tick.</param>
/// <param name="ActorId">The seat.</param>
/// <param name="ClassId">Its class.</param>
/// <param name="Cause">The killing ability id ("periodic" for a damage-over-time tick, "unknown" when no hit is seen).</param>
/// <param name="Source">The enemy's display name.</param>
/// <param name="Pull">The pull it happened in.</param>
public sealed record DeathRow(int Tick, int ActorId, string ClassId, string Cause, string Source, string Pull);

/// <summary>One pull's timing.</summary>
/// <param name="Id">Pull id.</param>
/// <param name="StartTick">pull_start tick, or null when never reached.</param>
/// <param name="ClearTick">pull_clear tick, or null when not cleared.</param>
public sealed record PullRow(string Id, int? StartTick, int? ClearTick);

/// <summary>
/// The counts the run summary reports, folded from each step's events and the pre-step state (the event stream
/// carries no ability on a damage event, so a hit is attributed to the source's <c>cast_complete</c> in the same tick,
/// else to the cast the source was channelling, else "periodic").
/// </summary>
public sealed class RunStats
{
    private readonly C.ContentRegistry _registry;
    private readonly Dictionary<int, string> _classOf;

    /// <summary>Creates the fold for <paramref name="party"/>.</summary>
    /// <param name="registry">The content (ability interruptibility, enemy names).</param>
    /// <param name="party">The seats.</param>
    public RunStats(C.ContentRegistry registry, IReadOnlyList<PartyMemberSpec> party)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(party);
        _classOf = party.ToDictionary(p => p.Id, p => p.ClassId);
        foreach (var p in party)
        {
            CastsBySeat[p.Id] = new SortedDictionary<string, int>(StringComparer.Ordinal);
            KicksLanded[p.Id] = 0;
            KickPresses[p.Id] = 0;
            LateKicks[p.Id] = 0;
            Dodges[p.Id] = 0;
        }
    }

    /// <summary>Health lost by the party per enemy ability id.</summary>
    public SortedDictionary<string, long> DamageTakenByAbility { get; } = new(StringComparer.Ordinal);

    /// <summary>Interruptible enemy casts that completed, per ability id.</summary>
    public SortedDictionary<string, int> InterruptibleResolved { get; } = new(StringComparer.Ordinal);

    /// <summary>Enemy casts interrupted, per ability id.</summary>
    public SortedDictionary<string, int> Interrupted { get; } = new(StringComparer.Ordinal);

    /// <summary>Successful interrupts per seat.</summary>
    public SortedDictionary<int, int> KicksLanded { get; } = new();

    /// <summary>Interrupt presses per seat (commands issued).</summary>
    public SortedDictionary<int, int> KickPresses { get; } = new();

    /// <summary>Kick presses that came late per seat (policy hands only).</summary>
    public SortedDictionary<int, int> LateKicks { get; } = new();

    /// <summary>Completed casts per seat per ability id.</summary>
    public SortedDictionary<int, SortedDictionary<string, int>> CastsBySeat { get; } = new();

    /// <summary>Player deaths in order.</summary>
    public List<DeathRow> Deaths { get; } = new();

    /// <summary>Pull timings in dungeon order (filled as pulls start).</summary>
    public List<PullRow> Pulls { get; } = new();

    /// <summary>Notable encounter events by type (lock_open, lock_broken, lock_failed, boss_enrage, lock_enrage, depleted).</summary>
    public SortedDictionary<string, int> Notable { get; } = new(StringComparer.Ordinal);

    private static readonly HashSet<string> NotableTypes = new(StringComparer.Ordinal)
    {
        "lock_open", "lock_broken", "lock_failed", "boss_enrage", "lock_enrage", "depleted", "stagger", "echo_spawn",
    };

    /// <summary>Counts interrupt presses in this tick's commands.</summary>
    /// <param name="commands">The commands about to be stepped.</param>
    public void ObserveCommands(IEnumerable<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var c in commands.OfType<CastCommand>())
        {
            if (_classOf.TryGetValue(c.ActorId, out var cls) && string.Equals(_registry.Classes[cls].Abilities.Interrupt, c.AbilityId, StringComparison.Ordinal))
            {
                KickPresses[c.ActorId]++;
            }
        }
    }

    /// <summary>Ticks each seat spent walking out of a danger telegraph (policy hands only; 0 for scripted seats).</summary>
    public SortedDictionary<int, int> Dodges { get; } = new();

    /// <summary>Records a policy seat's hands: late kicks and dodge ticks.</summary>
    /// <param name="actorId">The seat.</param>
    /// <param name="late">Kicks that came late.</param>
    /// <param name="dodges">Ticks spent dodging.</param>
    public void NoteHands(int actorId, int late, int dodges)
    {
        LateKicks[actorId] = late;
        Dodges[actorId] = dodges;
    }

    private static int I(SimEvent e, string key) => e.Get(key) is JsInt v ? (int)v.Value : -1;

    private static string S(SimEvent e, string key) => e.Get(key) is JsString v ? v.Value : string.Empty;

    private string NameOf(ActorState? a) =>
        a is null ? "unknown" : _registry.Enemies.TryGetValue(a.ClassId, out var en) ? en.Name : a.ClassId;

    /// <summary>Folds one step.</summary>
    /// <param name="before">The run before the step (casts in flight, actor kinds).</param>
    /// <param name="events">The step's events.</param>
    /// <param name="pullId">The active pull's id before the step.</param>
    public void ObserveStep(Run before, IReadOnlyList<SimEvent> events, string pullId)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(events);
        var actors = before.State.Actors;
        var lastHit = new Dictionary<int, (string Ability, string Source)>();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Type)
            {
                case "pull_start":
                    Pulls.Add(new PullRow(I(e, "pullIndex") >= before.Dungeon.Pulls.Count - 1 ? "boss" : S(e, "pullId"), e.Tick, null));
                    break;
                case "pull_clear":
                    var id = I(e, "pullIndex") >= before.Dungeon.Pulls.Count - 1 ? "boss" : S(e, "pullId");
                    var row = Pulls.FindLastIndex(p => p.Id == id);
                    if (row >= 0)
                    {
                        Pulls[row] = Pulls[row] with { ClearTick = e.Tick };
                    }
                    break;
                case "damage":
                {
                    var target = I(e, "targetId");
                    var source = I(e, "sourceId");
                    NoteFought(actors, source, target);
                    NoteFought(actors, target, source);
                    if (!actors.TryGetValue(target, out var t) || t.Kind != ActorKind.Player)
                    {
                        break;
                    }
                    actors.TryGetValue(source, out var s);
                    if (s is null || s.Kind != ActorKind.Enemy)
                    {
                        break;
                    }
                    var ability = AbilityOfHit(events, i, source, s);
                    var amount = e.Get("healthDamage") is JsInt hd ? hd.Value : 0;
                    DamageTakenByAbility[ability] = (DamageTakenByAbility.TryGetValue(ability, out var sum) ? sum : 0) + amount;
                    lastHit[target] = (ability, NameOf(s));
                    break;
                }
                case "death":
                {
                    var who = I(e, "actorId");
                    if (actors.TryGetValue(who, out var a) && a.Kind == ActorKind.Player)
                    {
                        var (cause, src) = lastHit.TryGetValue(who, out var h) ? h : ("unknown", NameOf(actors.TryGetValue(I(e, "sourceId"), out var s2) ? s2 : null));
                        Deaths.Add(new DeathRow(e.Tick, who, a.ClassId, cause, src, pullId));
                    }
                    break;
                }
                case "interrupt":
                {
                    var kicker = I(e, "sourceId");
                    var target = I(e, "targetId");
                    if (actors.TryGetValue(target, out var t) && t.Kind == ActorKind.Enemy)
                    {
                        var cast = t.CastId ?? "unknown";
                        Interrupted[cast] = (Interrupted.TryGetValue(cast, out var n) ? n : 0) + 1;
                        if (KicksLanded.ContainsKey(kicker))
                        {
                            KicksLanded[kicker]++;
                        }
                    }
                    break;
                }
                case "cast_complete":
                {
                    var actor = I(e, "actorId");
                    var ability = S(e, "abilityId");
                    if (CastsBySeat.TryGetValue(actor, out var seat))
                    {
                        seat[ability] = (seat.TryGetValue(ability, out var n) ? n : 0) + 1;
                    }
                    else if (actors.TryGetValue(actor, out var a) && a.Kind == ActorKind.Enemy &&
                        _registry.Abilities.TryGetValue(ability, out var def) && def.Interruptible)
                    {
                        InterruptibleResolved[ability] = (InterruptibleResolved.TryGetValue(ability, out var n2) ? n2 : 0) + 1;
                    }
                    break;
                }
                default:
                    if (NotableTypes.Contains(e.Type))
                    {
                        Notable[e.Type] = (Notable.TryGetValue(e.Type, out var n) ? n : 0) + 1;
                    }
                    break;
            }
        }
    }

    /// <summary>Enemy ids the party traded damage with, to their display names (the ids a plan's focus list takes).</summary>
    public SortedDictionary<string, string> EnemiesFought { get; } = new(StringComparer.Ordinal);

    // Records the enemy side of a damage event between an enemy and a player.
    private void NoteFought(SortedDictionary<int, ActorState> actors, int enemyId, int playerId)
    {
        if (actors.TryGetValue(enemyId, out var en) && en.Kind == ActorKind.Enemy &&
            actors.TryGetValue(playerId, out var pl) && pl.Kind == ActorKind.Player)
        {
            EnemiesFought[en.ClassId] = NameOf(en);
        }
    }

    // The ability behind a hit: the source's cast_complete later in the same step, else the cast it was channelling
    // before the step, else "periodic" (an effect tick or field).
    private static string AbilityOfHit(IReadOnlyList<SimEvent> events, int index, int source, ActorState s)
    {
        for (var j = index + 1; j < events.Count; j++)
        {
            var e = events[j];
            if (e.Type == "cast_complete" && I(e, "actorId") == source)
            {
                return S(e, "abilityId");
            }
        }
        return s.CastId ?? "periodic";
    }
}
