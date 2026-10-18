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
    private readonly Dictionary<string, ClassKit> _kitCache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private IReadOnlyList<string>? _priorityInterruptIds;

    private KitRegistry(ContentRegistry? registry) => _registry = registry;

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
        return kit is not null ? EffectiveKit.FromClassKit(kit) : EffectiveKit.FromFallback(fallback);
    }
}
