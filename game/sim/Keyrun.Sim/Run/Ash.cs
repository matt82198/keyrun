using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// Ash banked per run (BIBLE s7 "Every run banks Ash, win or lose"; the frozen TS screen/src/progression.ts
/// ASH_TIMED/ASH_DEPLETED/ASH_ABANDONED). Ash buys only cosmetics and titles, so it never feeds power.
/// </summary>
public static class Ash
{
    /// <summary>Ash for a timed clear (progression.ts ASH_TIMED).</summary>
    public const int Timed = 50;

    /// <summary>Ash for a depleted key (progression.ts ASH_DEPLETED).</summary>
    public const int Depleted = 30;

    /// <summary>
    /// Ash for an abandoned run: none (STATE.md D45 "a player who leaves mid-run gets nothing"; the frozen TS
    /// progression.ts ASH_ABANDONED was 10, kept as <see cref="LegacyAbandoned"/> so older saves still load).
    /// </summary>
    public const int Abandoned = 0;

    /// <summary>What a save written before D45 recorded for an abandoned run (progression.ts ASH_ABANDONED).</summary>
    public const int LegacyAbandoned = 10;

    /// <summary>Ash a run with <paramref name="result"/> banks: every finished run banks some, a left run none (D45).</summary>
    /// <param name="result">How the run ended.</param>
    /// <returns>The Ash.</returns>
    public static int ForResult(RunResult result) => result switch
    {
        RunResult.Timed => Timed,
        RunResult.Depleted => Depleted,
        RunResult.Abandoned => Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "unknown run result"),
    };
}

/// <summary>One reroll attempt.</summary>
/// <param name="Ok">True when a charge was spent and the Facet redrawn; false when refused.</param>
/// <param name="Item">The item after the attempt (the input item itself when refused).</param>
/// <param name="Prng">The crafting PRNG after the attempt (unchanged when refused).</param>
/// <param name="Refusal">Why it was refused, else null.</param>
public sealed record RerollResult(bool Ok, Item Item, PrngState Prng, string? Refusal);

/// <summary>
/// The BIBLE s7 CRAFTING REROLL ("two charges per item: reroll the Facet from that slot's listed pool, twice, then it
/// is what it is"). Pure: the PRNG state is passed in and the advanced state returned, so the same seed gives the same
/// reroll sequence.
/// </summary>
public static class Crafting
{
    /// <summary>
    /// Spends one charge and redraws the Facet uniformly from <paramref name="slotPool"/>. Refused (nothing spent,
    /// the PRNG not advanced) when the item has no charges left or the pool is empty.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="slotPool">That slot's listed Facets for the character's class.</param>
    /// <param name="prng">The crafting PRNG.</param>
    /// <returns>The outcome.</returns>
    public static RerollResult Reroll(Item item, IReadOnlyList<Facet> slotPool, PrngState prng)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(slotPool);
        if (item.Charges <= 0)
        {
            return new RerollResult(false, item, prng, "no crafting charges left");
        }
        if (slotPool.Count == 0)
        {
            return new RerollResult(false, item, prng, $"no Facets listed for slot \"{item.Slot}\"");
        }
        var (index, next) = Prng.NextInt(prng, slotPool.Count);
        return new RerollResult(true, item with { Facet = slotPool[(int)index], Charges = item.Charges - 1 }, next, null);
    }
}

/// <summary>What the last banked run changed, for the screen's "since last run" line (BIBLE s10).</summary>
/// <param name="Result">How it ended.</param>
/// <param name="AshGained">Ash it banked.</param>
/// <param name="Taken">The end-of-run reward taken after it, or null.</param>
/// <param name="PreviousTier">The tier that slot held before the reward, or null when the slot was empty.</param>
public sealed record RunDelta(RunResult Result, int AshGained, Item? Taken, int? PreviousTier)
{
    /// <summary>"+30 Ash, weapon tier 3 (was 1)", "+50 Ash, weapon tier 2 (new)" or "+10 Ash".</summary>
    /// <returns>The line.</returns>
    public string Text() => Result == RunResult.Abandoned && AshGained == 0 && Taken is null
        ? EndOfRun.NothingLine
        : Taken is not { } t
        ? FormattableString.Invariant($"+{AshGained} Ash")
        : FormattableString.Invariant($"+{AshGained} Ash, {t.Slot} tier {t.Tier} ({(PreviousTier is { } p ? FormattableString.Invariant($"was {p}") : "new")})");
}

/// <summary>
/// The character's bounded progression (BIBLE s7): Ash banked every run, the five gear slots filled from the
/// end-of-run choice of three with Tier capped at 10, and crafting rerolls on the equipped items. Immutable.
/// </summary>
public sealed record Progression
{
    /// <summary>Tier is power and it is capped at 10 (BIBLE s7 "Ten is the end").</summary>
    public const int MaxTier = 10;

    /// <summary>Ash banked so far.</summary>
    public long AshTotal { get; init; }

    /// <summary>Runs banked so far.</summary>
    public int Runs { get; init; }

    /// <summary>Equipped items by slot (<see cref="Rewards.GearSlots"/>).</summary>
    public IReadOnlyDictionary<string, Item> Equipped { get; init; } = new Dictionary<string, Item>(StringComparer.Ordinal);

    /// <summary>The crafting PRNG (in state, as the sim's; never System.Random).</summary>
    public PrngState Prng { get; init; }

    /// <summary>The last banked run, or null before the first.</summary>
    public RunDelta? LastRun { get; init; }

    /// <summary>
    /// The talent build picked on the screen (BIBLE s6; f-83c5e369): forked ability id to picked option id, in ordinal
    /// key order, empty when nothing is picked. The sim keeps only the ids; the content layer (Keyrun.Content
    /// TalentPicks.Restore) checks them against the class's forks.
    /// </summary>
    public IReadOnlyDictionary<string, string> Talents { get; init; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// This progression with the screen's talent build replaced by <paramref name="choices"/> (a free respec is an
    /// empty set). Stored in ordinal key order, so the save text does not depend on pick order.
    /// </summary>
    /// <param name="choices">Ability id to option id.</param>
    /// <returns>The progression.</returns>
    /// <exception cref="ArgumentException">An id is empty or an ability is picked twice.</exception>
    public Progression WithTalents(IEnumerable<KeyValuePair<string, string>> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var talents = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (ability, option) in choices)
        {
            if (string.IsNullOrEmpty(ability) || string.IsNullOrEmpty(option))
            {
                throw new ArgumentException("a talent pick needs an ability id and an option id", nameof(choices));
            }
            if (!talents.TryAdd(ability, option))
            {
                throw new ArgumentException($"ability \"{ability}\" is picked twice", nameof(choices));
            }
        }
        return this with { Talents = talents };
    }

    /// <summary>A fresh character whose crafting PRNG is seeded from <paramref name="seed"/>.</summary>
    /// <param name="seed">The seed.</param>
    /// <returns>The progression.</returns>
    public static Progression Start(long seed) => new() { Prng = Core.Prng.SeedState(seed) };

    /// <summary>Banks a run: its Ash (win or lose) and a new "since last run" delta.</summary>
    /// <param name="result">How it ended.</param>
    /// <returns>The progression.</returns>
    public Progression BankRun(RunResult result)
    {
        var ash = Ash.ForResult(result);
        return this with { AshTotal = AshTotal + ash, Runs = Runs + 1, LastRun = new RunDelta(result, ash, null, null) };
    }

    /// <summary>
    /// Equips the taken reward in its slot, replacing what was there, with its tier capped at <see cref="MaxTier"/>;
    /// the last run's delta records it and the tier it replaced.
    /// </summary>
    /// <param name="item">The reward.</param>
    /// <returns>The progression.</returns>
    /// <exception cref="ArgumentException">The item's slot is not one of the five gear slots.</exception>
    /// <exception cref="InvalidOperationException">The last banked run was abandoned (D45: a leaver gets nothing).</exception>
    public Progression Take(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (LastRun?.Result == RunResult.Abandoned)
        {
            throw new InvalidOperationException("a run the player left offers no reward (STATE.md D45)");
        }
        if (!Rewards.GearSlots.Contains(item.Slot, StringComparer.Ordinal))
        {
            throw new ArgumentException($"unknown gear slot \"{item.Slot}\"", nameof(item));
        }
        var capped = item with { Tier = Math.Min(MaxTier, Math.Max(1, item.Tier)) };
        int? previous = Equipped.TryGetValue(item.Slot, out var old) ? old.Tier : null;
        return this with
        {
            Equipped = With(item.Slot, capped),
            LastRun = LastRun is { } last ? last with { Taken = capped, PreviousTier = previous } : null,
        };
    }

    /// <summary>Rerolls the item equipped in <paramref name="slot"/> (<see cref="Crafting.Reroll"/>).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="slotPool">That slot's listed Facets.</param>
    /// <returns>The progression (this same instance when refused) and the outcome, or a null outcome when the slot
    /// is empty.</returns>
    public (Progression Progression, RerollResult? Result) Reroll(string slot, IReadOnlyList<Facet> slotPool)
    {
        if (!Equipped.TryGetValue(slot, out var item))
        {
            return (this, null);
        }
        var result = Crafting.Reroll(item, slotPool, Prng);
        return result.Ok ? (this with { Equipped = With(slot, result.Item), Prng = result.Prng }, result) : (this, result);
    }

    private Dictionary<string, Item> With(string slot, Item item) =>
        new(Equipped, StringComparer.Ordinal) { [slot] = item };
}

/// <summary>
/// One Facet's ability patch in sim types (content/schema/facet.ts FacetPatch, applied as the frozen TS
/// content/apply-patch.ts applyPatch): each non-null direct field replaces the ability's, then <see cref="TagAdd"/>
/// merges into the tags (no duplicates, base order kept), <see cref="TagRemove"/> drops tags, and
/// <see cref="EffectsAppend"/> appends after the base ops. The host converts the content patch (HostRunSetup.GearPatches);
/// the sim never reads Keyrun.Content. No facet in content/data sets both a direct and a merged form of the same field,
/// so the direct-then-merged order is the TS key order for every shipped facet.
/// </summary>
public sealed record GearPatch
{
    /// <summary>Replacement cast time, ticks.</summary>
    public int? CastTicks { get; init; }

    /// <summary>Replacement GCD, ticks.</summary>
    public int? GcdTicks { get; init; }

    /// <summary>Replacement cooldown, ticks.</summary>
    public int? CooldownTicks { get; init; }

    /// <summary>Replacement charges.</summary>
    public long? Charges { get; init; }

    /// <summary>Replacement cost.</summary>
    public AbilityCost? Cost { get; init; }

    /// <summary>Replacement range, mm.</summary>
    public long? Range { get; init; }

    /// <summary>Replacement shape.</summary>
    public AbilityShape? Shape { get; init; }

    /// <summary>Replacement interruptible flag.</summary>
    public bool? Interruptible { get; init; }

    /// <summary>Replacement tags.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Replacement ops.</summary>
    public IReadOnlyList<EffectOp>? Effects { get; init; }

    /// <summary>Replacement verse variants.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EffectOp>>? Variants { get; init; }

    /// <summary>Tags merged into the ability's tags.</summary>
    public IReadOnlyList<string>? TagAdd { get; init; }

    /// <summary>Tags removed from the ability's tags.</summary>
    public IReadOnlyList<string>? TagRemove { get; init; }

    /// <summary>Ops appended after the ability's ops.</summary>
    public IReadOnlyList<EffectOp>? EffectsAppend { get; init; }

    /// <summary>The patched ability; <paramref name="ability"/> itself is never changed and its id is kept.</summary>
    /// <param name="ability">The base (or already patched) ability.</param>
    /// <returns>The patched ability.</returns>
    public AbilityDef ApplyTo(AbilityDef ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        var next = ability with
        {
            CastTicks = CastTicks ?? ability.CastTicks,
            GcdTicks = GcdTicks ?? ability.GcdTicks,
            CooldownTicks = CooldownTicks ?? ability.CooldownTicks,
            Charges = Charges ?? ability.Charges,
            Cost = Cost ?? ability.Cost,
            Range = Range ?? ability.Range,
            Shape = Shape ?? ability.Shape,
            Interruptible = Interruptible ?? ability.Interruptible,
            Tags = Tags ?? ability.Tags,
            Effects = Effects ?? ability.Effects,
            Variants = Variants ?? ability.Variants,
        };
        if (TagAdd is not null)
        {
            next = next with { Tags = (next.Tags ?? Array.Empty<string>()).Concat(TagAdd).Distinct(StringComparer.Ordinal).ToList() };
        }
        if (TagRemove is not null)
        {
            var drop = new HashSet<string>(TagRemove, StringComparer.Ordinal);
            next = next with { Tags = (next.Tags ?? Array.Empty<string>()).Where(t => !drop.Contains(t)).ToList() };
        }
        if (EffectsAppend is not null)
        {
            next = next with { Effects = next.Effects.Concat(EffectsAppend).ToList() };
        }
        return next;
    }
}

/// <summary>
/// The gear a character brings into a run (BIBLE s7: an item is a Tier plus one Facet). <see cref="Gear"/> is every
/// equipped item in <see cref="Rewards.GearSlots"/> order, so the Facets are carried and visible whether or not they
/// change a rule; <see cref="Abilities"/> is the wearer's kit after each Facet's <see cref="GearPatch"/> (slot order, so
/// a later slot patching the same ability applies on top). <see cref="ToRunOptions"/> hands it to createRun through
/// <see cref="RunOptions.Loadouts"/>, the run root's facet patch layer. Tier carries no rule here (no sim mechanism
/// reads it yet).
/// </summary>
/// <param name="ActorId">The wearer's actor id.</param>
/// <param name="Gear">Equipped items, slot order.</param>
/// <param name="Abilities">Patched abilities by id (only those a Facet changed).</param>
/// <param name="UnappliedFacetIds">Facets carried without a rule change: no patch was supplied for them, or their
/// ability is not in the base kit.</param>
public sealed record GearLoadout(
    int ActorId,
    IReadOnlyList<Item> Gear,
    IReadOnlyDictionary<string, AbilityDef> Abilities,
    IReadOnlyList<string> UnappliedFacetIds)
{
    /// <summary>The equipped Facets, slot order.</summary>
    public IReadOnlyList<Facet> Facets => Gear.Select(i => i.Facet).ToList();

    /// <summary>Builds the wearer's loadout from the equipped gear.</summary>
    /// <param name="progression">The character's progression.</param>
    /// <param name="actorId">The wearer's actor id.</param>
    /// <param name="baseAbilities">The run's base ability map (RunContent.Abilities).</param>
    /// <param name="patchesByFacetId">Each Facet's patch, by Facet id; a Facet with no entry is carried unapplied.</param>
    /// <returns>The loadout.</returns>
    public static GearLoadout Of(
        Progression progression,
        int actorId,
        IReadOnlyDictionary<string, AbilityDef> baseAbilities,
        IReadOnlyDictionary<string, GearPatch> patchesByFacetId)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(baseAbilities);
        ArgumentNullException.ThrowIfNull(patchesByFacetId);
        var gear = new List<Item>();
        var patched = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        var unapplied = new List<string>();
        foreach (var slot in Rewards.GearSlots)
        {
            if (!progression.Equipped.TryGetValue(slot, out var item))
            {
                continue;
            }
            gear.Add(item);
            var facet = item.Facet;
            if (!patchesByFacetId.TryGetValue(facet.Id, out var patch) || !baseAbilities.TryGetValue(facet.AbilityId, out var baseDef))
            {
                unapplied.Add(facet.Id);
                continue;
            }
            patched[facet.AbilityId] = patch.ApplyTo(patched.TryGetValue(facet.AbilityId, out var current) ? current : baseDef);
        }
        return new GearLoadout(actorId, gear, patched, unapplied);
    }

    /// <summary>
    /// <paramref name="options"/> with the patched kit as the wearer's loadout (the Clause and every other seat's
    /// loadout kept; a gear patch overrides the wearer's own entry for the same ability). Returns
    /// <paramref name="options"/> itself (same reference, possibly null) when no Facet changed an ability, so a run
    /// without gear is created exactly as before.
    /// </summary>
    /// <param name="options">The run options, or null.</param>
    /// <returns>The options.</returns>
    public RunOptions? ToRunOptions(RunOptions? options)
    {
        if (Abilities.Count == 0)
        {
            return options;
        }
        var loadouts = options?.Loadouts is { } existing
            ? new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>(existing)
            : new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();
        var mine = loadouts.TryGetValue(ActorId, out var own)
            ? new Dictionary<string, AbilityDef>(own, StringComparer.Ordinal)
            : new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var kv in Abilities)
        {
            mine[kv.Key] = kv.Value;
        }
        loadouts[ActorId] = mine;
        return (options ?? new RunOptions()) with { Loadouts = loadouts };
    }
}
