using Keyrun.Content;

namespace Keyrun.Host;

/// <summary>Content display names for actors and abilities (Godot-free).</summary>
public static class Names
{
    /// <summary>An actor's enemy or class display name, else <paramref name="classId"/>.</summary>
    /// <param name="registry">The content, or null.</param>
    /// <param name="classId">The actor's class id.</param>
    /// <returns>The name.</returns>
    public static string Actor(ContentRegistry? registry, string classId)
    {
        if (registry is null)
        {
            return classId;
        }
        if (registry.Enemies.TryGetValue(classId, out var enemy))
        {
            return enemy.Name;
        }
        return registry.Classes.TryGetValue(classId, out var cls) ? cls.Name : classId;
    }

    /// <summary>
    /// The name a player reads for an ability, through the ONE resolver (<see cref="AbilityNames.Display"/>): a player kit
    /// ability's ability-text.json display name, else the content name (enemy casts), else <paramref name="abilityId"/>.
    /// </summary>
    /// <param name="registry">The content, or null.</param>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The name.</returns>
    public static string Ability(ContentRegistry? registry, string abilityId) => AbilityNames.Display(registry, abilityId);
}
