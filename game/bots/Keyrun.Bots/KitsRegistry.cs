using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;

namespace Keyrun.Bots;

/// <summary>
/// Port of bots/src/kit-registry.ts (getClassKit, priorityInterruptAbilityIds) and kit-resolve.ts (resolveKit)
/// over one content registry. The TS keeps a module-level registry and kit cache set by
/// <c>setContentRegistry()</c>; here each instance owns its registry and memo cache, and a
/// <see cref="BotView"/> carries the instance (see <see cref="BotView.Kits"/>). <see cref="Empty"/> is the TS
/// "no registry injected" state.
/// </summary>
public sealed class KitRegistry
{
    private readonly ContentRegistry? _registry;
    private readonly Dictionary<string, ClassKit> _kitCache;
    private readonly object _gate;
    private readonly KitRegistry? _root;
    private readonly IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef>? _runAbilities;
    private readonly Dictionary<string, IReadOnlyList<string>> _runOffGcd = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _priorityInterruptIds;
    private KitRegistry? _lastRun;

    private KitRegistry(ContentRegistry? registry)
    {
        _registry = registry;
        _kitCache = new(StringComparer.Ordinal);
        _gate = new();
    }

    private KitRegistry(KitRegistry root, IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef> runAbilities)
    {
        _registry = root._registry;
        _kitCache = root._kitCache;
        _gate = root._gate;
        _root = root;
        _runAbilities = runAbilities;
    }

    /// <summary>No registry: every <see cref="GetClassKit"/> is null and the priority-cast list is empty.</summary>
    public static KitRegistry Empty { get; } = new(null);

    /// <summary>kit-registry.ts setContentRegistry(reg): a registry over <paramref name="registry"/>.</summary>
    /// <param name="registry">Loaded content (<c>ContentLoader.Load()</c>).</param>
    /// <returns>The kit registry.</returns>
    public static KitRegistry From(ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new KitRegistry(registry);
    }

    /// <summary>
    /// This registry over a run's own ability map (RunContent.Abilities: content merged with each seat's loadout, the
    /// Facet patches of GearLoadout included). Every kit is still content's, but its
    /// <see cref="EffectiveKit.OffGcdAbilityIds"/> are judged by the run's defs, so a Facet that sets an ability's
    /// gcdTicks to 0 is off the GCD for bot readiness exactly as it is for the sim (and one that raises it is not). An
    /// id the run map lacks keeps content's answer. The last run map's registry is memoised (same reference, same
    /// registry), and every derived registry shares this one's content kit cache.
    /// </summary>
    /// <param name="runAbilities">The run's abilities by id.</param>
    /// <returns>The run-aware registry.</returns>
    public KitRegistry ForRun(IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef> runAbilities)
    {
        ArgumentNullException.ThrowIfNull(runAbilities);
        var root = _root ?? this;
        lock (root._gate)
        {
            if (root._lastRun is { } last && ReferenceEquals(last._runAbilities, runAbilities))
            {
                return last;
            }
            var next = new KitRegistry(root, runAbilities);
            root._lastRun = next;
            return next;
        }
    }

    /// <summary>kit-registry.ts getContentRegistry: the content registry, or null for <see cref="Empty"/>.</summary>
    public ContentRegistry? Content => _registry;

    /// <summary>
    /// kit-registry.ts getClassKit: the memoised ClassKit for <paramref name="classId"/>, or null when there is no
    /// registry or the class is not in content (a scripted actor's placeholder classId).
    /// </summary>
    /// <param name="classId">Class id.</param>
    /// <returns>The kit (the same instance on every call), or null.</returns>
    public ClassKit? GetClassKit(string classId)
    {
        ArgumentNullException.ThrowIfNull(classId);
        if (_registry is null)
        {
            return null;
        }
        lock (_gate)
        {
            if (_kitCache.TryGetValue(classId, out var cached))
            {
                return cached;
            }
            if (!_registry.Classes.TryGetValue(classId, out var cls))
            {
                return null;
            }
            var kit = Kits.BuildClassKit(cls, _registry.Abilities, _registry.Effects);
            _kitCache[classId] = kit;
            return kit;
        }
    }

    /// <summary>
    /// kit-registry.ts priorityInterruptAbilityIds: every ability tagged "healer", sorted (UTF-16 ordinal, as JS
    /// <c>Array.prototype.sort</c> orders strings). Empty with no registry.
    /// </summary>
    /// <returns>The priority-cast list.</returns>
    public IReadOnlyList<string> PriorityInterruptAbilityIds()
    {
        if (_registry is null)
        {
            return Array.Empty<string>();
        }
        lock (_gate)
        {
            _priorityInterruptIds ??= _registry.Abilities.Values
                .Where(a => KitPriority.HasTag(a, Kits.PriorityInterruptTag))
                .Select(a => a.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            return _priorityInterruptIds;
        }
    }

    /// <summary>
    /// kit-resolve.ts resolveKit: content's ClassKit when <paramref name="classId"/> is a content class, otherwise a
    /// thin kit from the view's own <paramref name="fallback"/> (its single heal treated as ally-targeted).
    /// </summary>
    /// <param name="classId">Class id.</param>
    /// <param name="fallback">The view's classKit.</param>
    /// <returns>The effective kit.</returns>
    public EffectiveKit ResolveKit(string classId, ClassKitView fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var kit = GetClassKit(classId);
        if (kit is null)
        {
            return EffectiveKit.FromFallback(fallback);
        }
        var effective = EffectiveKit.FromClassKit(kit);
        return _runAbilities is null ? effective : effective with { OffGcdAbilityIds = RunOffGcdIds(classId, kit) };
    }

    // The kit's slotted off-GCD ids judged by the run's defs (ForRun); memoised per class.
    private IReadOnlyList<string> RunOffGcdIds(string classId, ClassKit kit)
    {
        lock (_gate)
        {
            if (_runOffGcd.TryGetValue(classId, out var cached))
            {
                return cached;
            }
            var run = _runAbilities!;
            var slots = _registry!.Classes[classId].Abilities;
            var ids = Kits.OffGcdIds(slots, id =>
                run.TryGetValue(id, out var def) ? def.GcdTicks <= 0 : kit.OffGcdAbilityIds.Contains(id, StringComparer.Ordinal));
            _runOffGcd[classId] = ids;
            return ids;
        }
    }
}
