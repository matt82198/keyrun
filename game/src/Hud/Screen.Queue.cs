using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

public partial class Screen
{
    private void Queue()
    {
        if (_lobby is not { } lobby || _driver?.Registry is not { } reg)
        {
            return;
        }
        // The loadout here only reports what each layer contributes. The request is QueuedKit.Queue's (through
        // Lobby.Queue, which adds the class): the talent kit and the gear stats, never the Facets (finding f-494ea8ef:
        // the host's RunStart.Of applies each Facet once from the progression; a Facet-patched kit on the request was
        // patched a second time).
        // D65: a dungeon the content lays out no map for cannot be built (HostRunSetup.Build); the screen says so.
        if (!lobby.Locked && !DungeonPick.HasMap(reg, lobby.Key.DungeonId))
        {
            _status = FormattableString.Invariant($"{lobby.Key.DungeonId} has no map yet.");
            Proof(FormattableString.Invariant($"SCREEN queue refused dungeon={lobby.Key.DungeonId} reason=no-map"));
            return;
        }
        var loadout = lobby.Locked ? null : RunLoadout(withFacets: true);
        var (locked, request) = lobby.Queue(reg, _progression);
        _lobby = locked;
        if (request is null)
        {
            return;
        }
        Proof(FormattableString.Invariant($"SCREEN queue class={locked.ClassId}"));
        // BIBLE s7 "Tier is power": the five slots' summed stats ride the request onto the human's actor.
        foreach (var (actor, gear) in request.Gear ?? new Dictionary<int, GearStats>())
        {
            Proof(FormattableString.Invariant(
                $"SCREEN queue gear actor={actor} damageDoneBp={gear.DamageDoneBp} damageTakenBp={gear.DamageTakenBp}"));
        }
        LastRequest = request;
        Proof(FormattableString.Invariant($"SCREEN queue dungeon={request.DungeonId} key={request.KeyLevel} clause={request.ClauseId}"));
        ProofQueuedTalents(request, loadout?.TalentIds);
        ProofQueuedFacets(request, loadout?.Facets);
        var before = _driver?.Run;
        Queued?.Invoke(request);
        if (_driver?.Run is { } started && !ReferenceEquals(started, before))
        {
            ProofRunFacets(started, loadout?.Facets);
        }
        // The Key band is locked in now: re-proof it so the readiness line reads the wait ("Queued at key N"), BIBLE s10.
        // Emitted after the queue lines above, which the scene test reads by position.
        ProofKeyBand();
        Close();
    }

    // The human's patched kit for the next run plus what each layer contributed: the talent ability ids and the gear
    // layer's resolution (null when the facet layer was not asked for or content is not loaded).
    private sealed record HumanLoadout(
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>? Loadouts,
        IReadOnlySet<string> TalentIds,
        Keyrun.Content.FacetResolution? Facets);

    /// <summary>
    /// The human's talent-resolved kit for the next run (BIBLE s6), keyed by the human's actor id, or null when no fork
    /// is picked or content is not loaded. The talent count the panel shows; Queue hands the run
    /// <see cref="RunLoadout"/> with the gear layer as well.
    /// </summary>
    /// <returns>The talent-only loadouts, or null.</returns>
    private IReadOnlyDictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>? TalentLoadouts() =>
        RunLoadout(withFacets: false)?.Loadouts;

    /// <summary>
    /// The human's kit for the next run in content/apply.ts applyLoadout order (BIBLE s6 then s7): the talent picks are
    /// resolved on the content abilities with <see cref="TalentPicks.Resolve"/>, then (with <paramref name="withFacets"/>)
    /// every equipped item's Facet is applied on top with <see cref="Keyrun.Content.GearFacets.Resolve"/> in slot order.
    /// The patched content is converted to sim AbilityDefs by the same <see cref="HostRunSetup.Build"/> conversion the
    /// run itself is built with, so a talented or Facet-patched ability is exactly what the host would build if the
    /// patch were the base definition. A line width (twinStrike, infernoBolt) converts to the sim line's half-width, so
    /// every shipped Facet applies; one the content cannot honour is still skipped and reported, never a refused Queue.
    /// </summary>
    /// <param name="withFacets">True to apply the gear layer.</param>
    /// <returns>The loadout, or null when content or the Key band is not there.</returns>
    private HumanLoadout? RunLoadout(bool withFacets)
    {
        if (_driver?.Registry is not { } reg || _lobby is not { } lobby)
        {
            return null;
        }
        var s = lobby.Key;
        var talented = lobby.Talents is { Count: > 0 } picks ? picks.Resolve(reg.Abilities) : new Keyrun.Content.ValueMap<Keyrun.Content.AbilityDef>();
        var patched = new Dictionary<string, Keyrun.Content.AbilityDef>(talented, StringComparer.Ordinal);
        Keyrun.Content.FacetResolution? facets = null;
        if (withFacets && reg.Classes.TryGetValue(lobby.ClassId, out var cls))
        {
            facets = Keyrun.Content.GearFacets.Resolve(
                reg.Facets,
                Merge(reg.Abilities, talented),
                cls,
                Rewards.GearSlots.Where(_progression.Equipped.ContainsKey).Select(slot => (slot, _progression.Equipped[slot].Facet.Id)));
            foreach (var (id, def) in facets.Abilities)
            {
                patched[id] = def;
            }
        }
        var talentIds = new HashSet<string>(talented.Keys, StringComparer.Ordinal);
        if (patched.Count == 0)
        {
            return new HumanLoadout(null, talentIds, facets);
        }
        var setup = HostRunSetup.Build(reg with { Abilities = Merge(reg.Abilities, patched) }, s.DungeonId, s.KeyLevel, lobby.ClassId, withBots: false);
        var kit = patched.Keys.ToDictionary(id => id, id => setup.Content.Abilities[id], StringComparer.Ordinal);
        return new HumanLoadout(new Dictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>> { [setup.HumanActorId] = kit }, talentIds, facets);
    }

    private static Keyrun.Content.ValueMap<Keyrun.Content.AbilityDef> Merge(
        IReadOnlyDictionary<string, Keyrun.Content.AbilityDef> abilities, IReadOnlyDictionary<string, Keyrun.Content.AbilityDef> patched) =>
        new(abilities.Select(kv => patched.TryGetValue(kv.Key, out var p) ? new KeyValuePair<string, Keyrun.Content.AbilityDef>(kv.Key, p) : kv));

    private static int AppliedCount(IReadOnlyDictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>? loadouts) =>
        loadouts?.Values.Sum(kit => kit.Count) ?? 0;

    // One line for the talent count the request hands the run, then one per talented ability: the queued value next to
    // the base the run would otherwise play (read from the request's own RunOptions, the object SimDriver passes to
    // CreateRun). An ability a Facet also patched shows both layers.
    private void ProofQueuedTalents(QueueRequest request, IReadOnlySet<string>? talentIds)
    {
        var loadouts = request.ToRunOptions().Loadouts;
        var talents = loadouts?.Values.Sum(kit => kit.Keys.Count(id => talentIds?.Contains(id) == true)) ?? 0;
        Proof(FormattableString.Invariant($"SCREEN queue talents applied={talents}"));
        if (loadouts is null)
        {
            return;
        }
        var baseKit = _driver?.Setup?.Content.Abilities;
        foreach (var (actor, kit) in loadouts.OrderBy(kv => kv.Key))
        {
            foreach (var (id, def) in kit.Where(kv => talentIds?.Contains(kv.Key) == true))
            {
                var b = baseKit is not null && baseKit.TryGetValue(id, out var bd) ? bd : null;
                Proof($"SCREEN queue talent actor={actor.ToString(System.Globalization.CultureInfo.InvariantCulture)} ability={id} {Stats(def)} base {Stats(b)}");
            }
        }
    }

    // BIBLE s7: one line for the Facets the request hands the run (and how many equipped Facets were skipped), then one
    // per applied Facet with its ability's queued value next to the base (read from the options the host will create the
    // run with: HostRunSetup.StartFor, the call SimDriver.StartQueuedRun makes, since the Facets are layered there and
    // not on the request), and one per skipped Facet with the reason.
    private void ProofQueuedFacets(QueueRequest request, Keyrun.Content.FacetResolution? facets)
    {
        Proof(FormattableString.Invariant($"SCREEN queue facets applied={facets?.Applied.Count ?? 0} skipped={facets?.Skipped.Count ?? 0}"));
        if (facets is null)
        {
            return;
        }
        var loadouts = _driver?.Setup is { } setup && _driver.Registry is { } reg
            ? setup.StartFor(request, _progression, reg, ClassId).Options?.Loadouts
            : request.ToRunOptions().Loadouts;
        var baseKit = _driver?.Setup?.Content.Abilities;
        foreach (var f in facets.Applied)
        {
            foreach (var (actor, kit) in (loadouts ?? new Dictionary<int, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>>()).OrderBy(kv => kv.Key))
            {
                var queued = kit.TryGetValue(f.AbilityId, out var q) ? q : null;
                var b = baseKit is not null && baseKit.TryGetValue(f.AbilityId, out var bd) ? bd : null;
                Proof($"SCREEN queue facet actor={actor.ToString(System.Globalization.CultureInfo.InvariantCulture)} {FacetKey(f)} {FacetStats(queued)} base {FacetStats(b)}");
            }
        }
        foreach (var k in facets.Skipped)
        {
            Proof($"SCREEN queue facet skipped slot={k.Slot} facet={k.FacetId} reason=\"{k.Reason}\"");
        }
    }

    // Only when Queued started a run (SimDriver.StartQueuedRun; plain --autorun does not): each applied Facet's ability
    // as the started run plays it (Run.Content, the merged ability map CreateRun built from the request's loadouts).
    private void ProofRunFacets(Keyrun.Sim.Runs.Run run, Keyrun.Content.FacetResolution? facets)
    {
        if (facets is null || _driver?.Setup is not { } setup)
        {
            return;
        }
        foreach (var f in facets.Applied)
        {
            var def = run.Content.Abilities.TryGetValue(f.AbilityId, out var d) ? d : null;
            Proof($"SCREEN run facet actor={setup.HumanActorId.ToString(System.Globalization.CultureInfo.InvariantCulture)} {FacetKey(f)} {FacetStats(def)}");
        }
    }

    private static string FacetKey(Keyrun.Content.AppliedFacet f) =>
        $"slot={Keyrun.Content.GearFacets.SlotId(f.Slot)} facet={f.FacetId} ability={f.AbilityId}";

    private static string FacetStats(Keyrun.Sim.Combat.AbilityDef? d) => d is null
        ? "shape=- range=- cast=- cooldown=- charges=- cost=- effects=- tags=-"
        : FormattableString.Invariant(
            $"shape={d.Shape.Kind}/{d.Shape.Radius}/{d.Shape.Angle1024}/{d.Shape.Length} range={d.Range} cast={d.CastTicks} cooldown={d.CooldownTicks} charges={d.Charges} cost={d.Cost.Amount} effects={d.Effects.Count}+{d.Variants?.Values.Sum(v => v.Count) ?? 0} tags={(d.Tags is { Count: > 0 } t ? string.Join(',', t) : "-")}");

    private static string Stats(Keyrun.Sim.Combat.AbilityDef? d) => d is null
        ? "shape=- cooldown=- cost=- damage=-"
        : FormattableString.Invariant(
            $"shape={d.Shape.Kind} cooldown={d.CooldownTicks} cost={d.Cost.Amount} damage={d.Effects.OfType<Keyrun.Sim.Combat.DamageOp>().FirstOrDefault()?.Amount ?? 0}");

    private bool Locked() => _lobby?.Locked == true;
}
