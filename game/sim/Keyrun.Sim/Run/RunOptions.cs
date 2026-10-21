using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// createRun's optional per-run knobs (sim/run-options.ts): the Clause pick and per-actor loadout overrides.
/// </summary>
/// <remarks>
/// <para>CLAUSE (BIBLE s8): <see cref="ClauseId"/> is looked up in <see cref="RunContent.Clauses"/> and folded onto the
/// base rules with <see cref="Clauses.ApplyClause"/> (which throws <see cref="ClauseAlreadyActiveException"/> if the base
/// already carries one). An unknown id throws <see cref="UnknownClauseException"/>, never a silent no-Clause run.</para>
/// <para>LOADOUT (BIBLE s6/s7): the host resolves talents, gear and facets (including a facet-appended Window op with
/// its setupClass) into plain AbilityDefs and passes them here; the sim never sees a ClassDef. This is the facet patch
/// layer of the run root: <see cref="MergeLoadoutAbilities"/> merges them onto the run's one global ability map, so
/// <see cref="RulesConfig.Abilities"/> carries the patched kit. Two members patching the same id: the later party
/// member wins (deterministic, as the TS).</para>
/// </remarks>
public sealed record RunOptions
{
    /// <summary>A <see cref="RunContent.Clauses"/> key, or null for no Clause.</summary>
    public string? ClauseId { get; init; }

    /// <summary>Actor id to that actor's already-resolved abilities (by ability id), or null.</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>>? Loadouts { get; init; }

    /// <summary>
    /// Actor id to that actor's summed gear stats (BIBLE s7, <see cref="GearPower.Sum"/>), or null. C#-only, no TS
    /// counterpart: CreateRun puts them on the party actor; an id not in the party is refused.
    /// </summary>
    public IReadOnlyDictionary<int, GearStats>? Gear { get; init; }

    /// <summary>
    /// The host's affix rotation index (BIBLE s8: "the rotation index comes with the key from the host (the ISO week),
    /// so the sim stays deterministic"). Its parity picks Fortified or Tyrannical on keys 5-9
    /// (<see cref="Affixes.AffixesFor"/>) and it selects the key-set Clause (<see cref="ClauseRotation.ForKey"/>).
    /// </summary>
    public int RotationIndex { get; init; }

    /// <summary>
    /// The options a key carries with no pre-queue pick (BIBLE s8): the rotation index and the key-set Clause, or no
    /// Clause below <paramref name="clauseStartKey"/>. Exactly one Clause or none, never two.
    /// </summary>
    /// <param name="clauseIds">Every Clause id content defines.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="rotationIndex">The host's rotation index.</param>
    /// <param name="clauseStartKey">The first key that carries a Clause.</param>
    /// <returns>The options.</returns>
    public static RunOptions ForKey(IReadOnlyList<string> clauseIds, int keyLevel, int rotationIndex, int clauseStartKey) => new()
    {
        ClauseId = ClauseRotation.ForKey(clauseIds, keyLevel, rotationIndex, clauseStartKey),
        RotationIndex = rotationIndex,
    };

    /// <summary>
    /// run-options.ts:92 resolveEncounterRules: <paramref name="clauseId"/> folded onto
    /// <c>content.EncounterRules ?? DefaultEncounterRules(keyLevel)</c>, or that base unchanged when no id is given.
    /// </summary>
    /// <param name="content">The run content.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="clauseId">The Clause pick, or null.</param>
    /// <returns>The run's starting rules.</returns>
    /// <exception cref="UnknownClauseException">The id is not in <see cref="RunContent.Clauses"/>.</exception>
    public static EncounterRules ResolveEncounterRules(RunContent content, int keyLevel, string? clauseId)
    {
        ArgumentNullException.ThrowIfNull(content);
        var baseRules = content.EncounterRules ?? Clauses.DefaultEncounterRules(keyLevel);
        if (clauseId is null)
        {
            return baseRules;
        }
        if (content.Clauses is null || !content.Clauses.TryGetValue(clauseId, out var clause))
        {
            throw new UnknownClauseException(clauseId);
        }
        return Clauses.ApplyClause(baseRules, clause);
    }

    /// <summary>
    /// run-options.ts:107 mergeLoadoutAbilities. Returns <paramref name="baseAbilities"/> itself (same reference) when
    /// there is nothing to merge. Validates every entry before merging any, then applies the loadouts in party order.
    /// </summary>
    /// <param name="baseAbilities">content.Abilities.</param>
    /// <param name="party">The party.</param>
    /// <param name="loadouts">The loadouts, or null.</param>
    /// <returns>The merged ability map.</returns>
    /// <exception cref="UnknownLoadoutActorException">A loadout for an actor not in the party.</exception>
    /// <exception cref="UnknownLoadoutAbilityException">A loadout patching an ability not in the base map.</exception>
    public static IReadOnlyDictionary<string, AbilityDef> MergeLoadoutAbilities(
        IReadOnlyDictionary<string, AbilityDef> baseAbilities,
        IReadOnlyList<PartyMemberSpec> party,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>>? loadouts)
    {
        ArgumentNullException.ThrowIfNull(baseAbilities);
        ArgumentNullException.ThrowIfNull(party);
        if (loadouts is null || loadouts.Count == 0)
        {
            return baseAbilities;
        }

        // JS Object.keys on an integer-keyed record iterates ascending, so the first bad entry reported is the lowest id.
        var partyIds = new HashSet<int>(party.Select(m => m.Id));
        foreach (var actorId in loadouts.Keys.OrderBy(k => k))
        {
            if (!partyIds.Contains(actorId))
            {
                throw new UnknownLoadoutActorException(actorId);
            }
            foreach (var abilityId in loadouts[actorId].Keys)
            {
                if (!baseAbilities.ContainsKey(abilityId))
                {
                    throw new UnknownLoadoutAbilityException(actorId, abilityId);
                }
            }
        }

        var merged = new Dictionary<string, AbilityDef>(baseAbilities, StringComparer.Ordinal);
        foreach (var member in party)
        {
            if (loadouts.TryGetValue(member.Id, out var loadout))
            {
                foreach (var kv in loadout)
                {
                    merged[kv.Key] = kv.Value;
                }
            }
        }
        return merged;
    }
}

/// <summary>run-options.ts:61 UnknownClauseError: <see cref="RunOptions.ClauseId"/> is not in content.Clauses.</summary>
public sealed class UnknownClauseException : ArgumentException
{
    /// <summary>Builds the TS message.</summary>
    /// <param name="clauseId">The unknown id.</param>
    public UnknownClauseException(string clauseId)
        : base($"createRun: options.clauseId \"{clauseId}\" is not in content.clauses")
    {
        ClauseId = clauseId;
    }

    /// <summary>The unknown id.</summary>
    public string ClauseId { get; }
}

/// <summary>run-options.ts:68 UnknownLoadoutActorError: a loadout keyed to an actor not in the party.</summary>
public sealed class UnknownLoadoutActorException : ArgumentException
{
    /// <summary>Builds the TS message.</summary>
    /// <param name="actorId">The actor id.</param>
    public UnknownLoadoutActorException(int actorId)
        : base(FormattableString.Invariant($"createRun: options.loadouts has an entry for actorId {actorId}, which is not in party"))
    {
        ActorId = actorId;
    }

    /// <summary>The actor id.</summary>
    public int ActorId { get; }
}

/// <summary>run-options.ts:75 UnknownLoadoutAbilityError: a loadout patching an ability not in content.Abilities.</summary>
public sealed class UnknownLoadoutAbilityException : ArgumentException
{
    /// <summary>Builds the TS message.</summary>
    /// <param name="actorId">The actor id.</param>
    /// <param name="abilityId">The ability id.</param>
    public UnknownLoadoutAbilityException(int actorId, string abilityId)
        : base(FormattableString.Invariant($"createRun: options.loadouts[{actorId}] patches ability \"{abilityId}\", which is not in content.abilities"))
    {
        ActorId = actorId;
        AbilityId = abilityId;
    }

    /// <summary>The actor id.</summary>
    public int ActorId { get; }

    /// <summary>The ability id.</summary>
    public string AbilityId { get; }
}
