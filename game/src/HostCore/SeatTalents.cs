using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// One <c>--talent SEAT:TALENT</c> (finding f-a3de0a01): seat SEAT (a party actor id, <see cref="HostRunSetup.Roster"/>:
/// Warden 1, Chanter 2, Cinder 3) plays with talent option TALENT picked on its class's fork. Talents were the human's
/// only (RunStart.Of and QueuedKit.Queue patch <c>Loadouts[human]</c>), so a capture of a talent-driven HUD row such as
/// Encore's ECHO chip needed a human seat driven by an input log; this puts the pick on any seat, bot or human, with
/// no input. Godot-free.
/// </summary>
/// <param name="Seat">The seat's actor id.</param>
/// <param name="TalentId">The talent option id (content/data/classes/*.json talents[].options[].id).</param>
public sealed record SeatTalent(int Seat, string TalentId)
{
    /// <summary>The arena flag (repeatable).</summary>
    public const string Flag = "--talent";

    /// <summary>Parses <c>SEAT:TALENT</c>: SEAT a positive integer, TALENT a non-empty id of letters, digits, '_' or '-'.</summary>
    /// <param name="raw">The flag's value.</param>
    /// <returns>The seat talent.</returns>
    /// <exception cref="ArgumentException">The value is not of that form.</exception>
    public static SeatTalent Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var colon = raw.IndexOf(':', StringComparison.Ordinal);
        var seatText = colon < 0 ? raw : raw[..colon];
        var id = colon < 0 ? string.Empty : raw[(colon + 1)..];
        if (colon < 0 ||
            !int.TryParse(seatText, NumberStyles.None, CultureInfo.InvariantCulture, out var seat) || seat <= 0 ||
            id.Length == 0 || !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-'))
        {
            throw new ArgumentException($"{Flag} needs SEAT:TALENT (a positive seat id and a talent id), got '{raw}'");
        }
        return new SeatTalent(seat, id);
    }

    /// <summary>The run log's proof line, logged once per launch: <c>TALENT seat=S id=T</c>.</summary>
    /// <returns>The line.</returns>
    public string ProofLine() => FormattableString.Invariant($"TALENT seat={Seat} id={TalentId}");
}

/// <summary>
/// The <see cref="SeatTalent"/> picks of one run, resolved against its setup (<see cref="HostRunFactory.Create"/>). Each
/// seat's picks go through the human path's talent layer: <see cref="C.TalentPicks"/> on the seat's class, the patched
/// content abilities, then <see cref="HostRunSetup.Build"/> over them for the sim definitions (QueuedKit.Queue's
/// conversion). The human seat's kit rides the queued request's loadout, so RunStart.Of stacks its Facets on the talented
/// ability as it does for a screen pick; a bot seat's talented ability replaces its carried one, with its default gear
/// stacked on top (<see cref="C.BotGear.Resolve"/> over the talented abilities) when the run gives bots their gear.
/// </summary>
public sealed class SeatTalentPatch
{
    private readonly int _humanId;
    private readonly HostRunSetup _talented;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _abilitiesBySeat;

    private SeatTalentPatch(int humanId, HostRunSetup talented, IReadOnlyDictionary<int, IReadOnlyList<string>> abilitiesBySeat)
    {
        _humanId = humanId;
        _talented = talented;
        _abilitiesBySeat = abilitiesBySeat;
    }

    /// <summary>Resolves <paramref name="talents"/> on <paramref name="setup"/>'s party, or null when there are none.</summary>
    /// <param name="setup">The run's setup.</param>
    /// <param name="registry">The content the setup was built from.</param>
    /// <param name="talents">The picks, in flag order.</param>
    /// <returns>The patch, or null.</returns>
    /// <exception cref="ArgumentException">A seat is not in the party, a talent is not one of the seat's class's options
    /// (or names options on two forks), or two picks land on one fork of a seat.</exception>
    public static SeatTalentPatch? Resolve(HostRunSetup setup, C.ContentRegistry registry, IReadOnlyList<SeatTalent> talents)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(talents);
        if (talents.Count == 0)
        {
            return null;
        }
        var picks = new SortedDictionary<int, C.TalentPicks>();
        foreach (var t in talents)
        {
            var member = setup.Party.FirstOrDefault(p => p.Id == t.Seat) ?? throw new ArgumentException(
                $"{SeatTalent.Flag} {t.Seat}:{t.TalentId}: no seat {t.Seat} in the party (seats {string.Join(",", setup.Party.Select(p => p.Id).OrderBy(id => id))})");
            var cls = registry.Classes[member.ClassId];
            var at = cls.Talents.SelectMany((f, fi) => f.Options.Select((o, oi) => (Fork: fi, Option: oi, o.Id))).Where(x => x.Id == t.TalentId).ToList();
            if (at.Count != 1)
            {
                var known = string.Join(",", cls.Talents.SelectMany(f => f.Options.Select(o => o.Id)));
                throw new ArgumentException(at.Count == 0
                    ? $"{SeatTalent.Flag} {t.Seat}:{t.TalentId}: \"{t.TalentId}\" is not a talent of seat {t.Seat}'s class \"{cls.Id}\" (talents: {known})"
                    : $"{SeatTalent.Flag} {t.Seat}:{t.TalentId}: \"{t.TalentId}\" names options on {at.Count} forks of class \"{cls.Id}\"");
            }
            var seatPicks = picks.TryGetValue(t.Seat, out var p0) ? p0 : C.TalentPicks.Start(cls);
            if (seatPicks.PickedOption(at[0].Fork) is { } earlier)
            {
                throw new ArgumentException(
                    $"{SeatTalent.Flag} {t.Seat}:{t.TalentId}: seat {t.Seat} already picked \"{earlier}\" on fork \"{cls.Talents[at[0].Fork].AbilityId}\"");
            }
            picks[t.Seat] = seatPicks.Pick(at[0].Fork, at[0].Option);
        }
        var patched = new Dictionary<string, C.AbilityDef>(StringComparer.Ordinal);
        var bySeat = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var (seat, seatPicks) in picks)
        {
            var resolved = seatPicks.Resolve(registry.Abilities);
            foreach (var (id, def) in resolved)
            {
                patched[id] = def;
            }
            bySeat[seat] = resolved.Keys.ToList();
        }
        var merged = registry.Abilities.Select(kv => patched.TryGetValue(kv.Key, out var def) ? new KeyValuePair<string, C.AbilityDef>(kv.Key, def) : kv);
        var talented = HostRunSetup.Build(
            registry with { Abilities = new C.ValueMap<C.AbilityDef>(merged) },
            setup.Dungeon.Id,
            setup.Content.KeyLevel ?? throw new InvalidOperationException("seat talents: the run setup has no key level"),
            setup.HumanClassId,
            withBots: setup.Party.Count > 1,
            rotationIndex: setup.RotationIndex,
            dungeonSeed: DungeonPick.KitSeed);
        return new SeatTalentPatch(setup.HumanActorId, talented, bySeat);
    }

    /// <summary>
    /// <paramref name="queued"/> with the human seat's talented abilities in its loadout (a request with no Clause is made
    /// for a run that has none, as the CLI and <c>--autorun</c> starts do), or <paramref name="queued"/> unchanged when
    /// the human seat has no <c>--talent</c>.
    /// </summary>
    /// <param name="queued">The queued request, or null.</param>
    /// <returns>The request the run starts from.</returns>
    public QueueRequest? WithHuman(QueueRequest? queued)
    {
        if (!_abilitiesBySeat.TryGetValue(_humanId, out var ids))
        {
            return queued;
        }
        var request = queued ?? new QueueRequest(
            _talented.Dungeon.Id, _talented.Content.KeyLevel ?? throw new InvalidOperationException("seat talents: the run setup has no key level"), null);
        var loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>(request.Loadouts ?? new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>());
        var kit = new Dictionary<string, AbilityDef>(loadouts.TryGetValue(_humanId, out var carried) ? carried : new Dictionary<string, AbilityDef>(), StringComparer.Ordinal);
        foreach (var id in ids)
        {
            kit[id] = _talented.Content.Abilities[id];
        }
        loadouts[_humanId] = kit;
        return request with { Loadouts = loadouts };
    }

    /// <summary><paramref name="options"/> with every bot seat's talented abilities in its loadout.</summary>
    /// <param name="options">The run options so far (after <see cref="HostRunSetup.WithBotLoadouts"/> when bots wear gear).</param>
    /// <param name="botGear">Whether the bots wear their default gear (<see cref="RunRequest.BotLoadouts"/>).</param>
    /// <returns>The options (unchanged when no bot seat has a <c>--talent</c>).</returns>
    public RunOptions? WithBots(RunOptions? options, bool botGear)
    {
        var seats = _abilitiesBySeat.Keys.Where(s => s != _humanId).OrderBy(s => s).ToList();
        if (seats.Count == 0)
        {
            return options;
        }
        var loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>(options?.Loadouts ?? new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>());
        foreach (var seat in seats)
        {
            var kit = new Dictionary<string, AbilityDef>(loadouts.TryGetValue(seat, out var carried) ? carried : new Dictionary<string, AbilityDef>(), StringComparer.Ordinal);
            foreach (var id in _abilitiesBySeat[seat])
            {
                kit[id] = botGear && _talented.BotLoadouts.TryGetValue(seat, out var geared) && geared.TryGetValue(id, out var withGear)
                    ? withGear
                    : _talented.Content.Abilities[id];
            }
            loadouts[seat] = kit;
        }
        return (options ?? new RunOptions()) with { Loadouts = loadouts };
    }
}
