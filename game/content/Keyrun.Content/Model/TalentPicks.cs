using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// One character's talent build (BIBLE s6 "Talents: forks on the five"): at most one option picked on each of the
/// class's forks, the options of a fork mutually exclusive, and <see cref="Respec"/> free and always allowed ("Respec
/// is free on the screen, always"). Pure and immutable: every change returns a new value, equality is structural.
/// The Screen's talent panel renders it; <see cref="Resolve"/> is the talent layer of the TS
/// <c>content/apply.ts applyLoadout</c> (choices are ability id to option id, an unpicked fork leaves its ability at
/// the base definition, and each option's patch is applied by <see cref="ApplyPatch"/>, the
/// <c>content/apply-patch.ts</c> direct-field replacement). Gear Facets are a later layer and not this type's concern.
/// </summary>
public sealed record TalentPicks
{
    private TalentPicks(string classId, ValueList<TalentFork> forks, ValueMap<string> choices)
    {
        ClassId = classId;
        Forks = forks;
        Choices = choices;
    }

    /// <summary>The class the build belongs to.</summary>
    public string ClassId { get; }

    /// <summary>The class's forks, in content order (one per forked rotational ability).</summary>
    public ValueList<TalentFork> Forks { get; }

    /// <summary>Forked ability id to picked option id, in fork order; an unpicked fork has no entry.</summary>
    public ValueMap<string> Choices { get; }

    /// <summary>Number of forks.</summary>
    public int ForkCount => Forks.Count;

    /// <summary>Number of forks with a pick.</summary>
    public int Count => Choices.Count;

    /// <summary>A build with nothing picked: every ability at its base definition.</summary>
    /// <param name="cls">The class.</param>
    /// <returns>The empty build.</returns>
    public static TalentPicks Start(ClassDef cls)
    {
        ArgumentNullException.ThrowIfNull(cls);
        return new TalentPicks(cls.Id, cls.Talents, new ValueMap<string>());
    }

    /// <summary>
    /// Picks option <paramref name="option"/> of fork <paramref name="fork"/> (both 0-based), replacing any earlier
    /// pick on that fork. Picks are never locked: this is also how a player changes one fork without a full respec.
    /// </summary>
    /// <param name="fork">The fork index.</param>
    /// <param name="option">The option index within the fork.</param>
    /// <returns>The new build.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either index is outside the content (fail closed).</exception>
    public TalentPicks Pick(int fork, int option)
    {
        if (fork < 0 || fork >= Forks.Count)
            throw new ArgumentOutOfRangeException(nameof(fork), fork, "class \"" + ClassId + "\" has " + Forks.Count + " forks");
        var options = Forks[fork].Options;
        if (option < 0 || option >= options.Count)
            throw new ArgumentOutOfRangeException(nameof(option), option, "fork " + fork + " has " + options.Count + " options");
        var chosen = new Dictionary<string, string>(Choices, StringComparer.Ordinal) { [Forks[fork].AbilityId] = options[option].Id };
        return new TalentPicks(ClassId, Forks, Canonical(chosen));
    }

    /// <summary>Clears every pick. Free: it costs nothing and is allowed whenever the screen is up.</summary>
    /// <returns>The empty build.</returns>
    public TalentPicks Respec() => new(ClassId, Forks, new ValueMap<string>());

    /// <summary>The picked option id on fork <paramref name="fork"/>, or null when that fork is unpicked.</summary>
    /// <param name="fork">The fork index.</param>
    /// <returns>The option id or null.</returns>
    public string? PickedOption(int fork) =>
        fork >= 0 && fork < Forks.Count && Choices.TryGetValue(Forks[fork].AbilityId, out var id) ? id : null;

    /// <summary>The index of the picked option on fork <paramref name="fork"/>, or -1 when unpicked.</summary>
    /// <param name="fork">The fork index.</param>
    /// <returns>The option index or -1.</returns>
    public int PickedIndex(int fork)
    {
        var id = PickedOption(fork);
        if (id is null) return -1;
        var options = Forks[fork].Options;
        for (var i = 0; i < options.Count; i++)
            if (options[i].Id == id) return i;
        return -1;
    }

    /// <summary>
    /// The talented abilities: for each picked fork, the base ability from <paramref name="abilities"/> with the picked
    /// option's patch applied, keyed by ability id in fork order. Unpicked forks are absent (their ability is unchanged).
    /// </summary>
    /// <param name="abilities">The content abilities by id.</param>
    /// <returns>Patched abilities by id.</returns>
    /// <exception cref="InvalidOperationException">A forked ability or a picked option is not in the content.</exception>
    public ValueMap<AbilityDef> Resolve(IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        var result = new List<KeyValuePair<string, AbilityDef>>();
        foreach (var fork in Forks)
        {
            if (!Choices.TryGetValue(fork.AbilityId, out var optionId)) continue;
            if (!abilities.TryGetValue(fork.AbilityId, out var baseDef))
                throw new InvalidOperationException("talents: class \"" + ClassId + "\" fork ability \"" + fork.AbilityId + "\" is not in the content");
            var option = fork.Options.FirstOrDefault(o => o.Id == optionId)
                ?? throw new InvalidOperationException("talents: fork \"" + fork.AbilityId + "\" has no option \"" + optionId + "\"");
            result.Add(new KeyValuePair<string, AbilityDef>(fork.AbilityId, ApplyPatch(baseDef, option.Patch)));
        }
        return new ValueMap<AbilityDef>(result);
    }

    /// <summary>
    /// content/apply-patch.ts applyPatch for a talent patch: every field the patch sets replaces the base field whole;
    /// every field it leaves null keeps the base value. The id never changes (a patch has no id member).
    /// </summary>
    /// <param name="ability">The base ability.</param>
    /// <param name="patch">A talent option's patch.</param>
    /// <returns>The patched ability.</returns>
    /// <exception cref="ArgumentException">The patch is a <see cref="FacetPatch"/> (its merge keys belong to the gear layer).</exception>
    public static AbilityDef ApplyPatch(AbilityDef ability, AbilityPatch patch)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(patch);
        if (patch is FacetPatch)
            throw new ArgumentException("a facet patch is applied by the gear layer, not the talent layer", nameof(patch));
        return ability with
        {
            Name = patch.Name ?? ability.Name,
            CastTicks = patch.CastTicks ?? ability.CastTicks,
            GcdTicks = patch.GcdTicks ?? ability.GcdTicks,
            CooldownTicks = patch.CooldownTicks ?? ability.CooldownTicks,
            Charges = patch.Charges ?? ability.Charges,
            Cost = patch.Cost ?? ability.Cost,
            Range = patch.Range ?? ability.Range,
            Shape = patch.Shape ?? ability.Shape,
            Interruptible = patch.Interruptible ?? ability.Interruptible,
            Tags = patch.Tags ?? ability.Tags,
            Effects = patch.Effects ?? ability.Effects,
            Variants = patch.Variants ?? ability.Variants,
        };
    }

    // Choices in fork order, so two builds with the same picks are equal however they were reached.
    private ValueMap<string> Canonical(IReadOnlyDictionary<string, string> chosen) =>
        new(Forks.Where(f => chosen.ContainsKey(f.AbilityId)).Select(f => new KeyValuePair<string, string>(f.AbilityId, chosen[f.AbilityId])));
}
