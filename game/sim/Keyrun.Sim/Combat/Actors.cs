using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>Port of sim/combat/actors.ts: the actor factory and small state helpers.</summary>
public static class Actors
{
    /// <summary>
    /// sim/combat/actors.ts:19-41 createActor: full health and resource, alive, no target, not casting, no
    /// cooldowns, GCD ready at tick 0, no effects, empty threat. <paramref name="yaw"/> defaults to 0 (:26).
    /// </summary>
    /// <param name="id">Actor id.</param>
    /// <param name="kind">Faction.</param>
    /// <param name="classId">Class or enemy id.</param>
    /// <param name="role">Role tag.</param>
    /// <param name="pos">Position, mm.</param>
    /// <param name="maxHealth">Maximum (and starting) health.</param>
    /// <param name="maxResource">Maximum (and starting) resource.</param>
    /// <param name="yaw">Facing, 1/1024 turn.</param>
    /// <returns>A new actor.</returns>
    public static ActorState Create(int id, ActorKind kind, string classId, ActorRole role, Vec3 pos, long maxHealth, long maxResource, int yaw = 0) => new()
    {
        Id = id,
        Kind = kind,
        ClassId = classId,
        Role = role,
        Pos = pos,
        Yaw = yaw,
        Health = maxHealth,
        MaxHealth = maxHealth,
        Resource = maxResource,
        MaxResource = maxResource,
        Alive = true,
        TargetId = null,
        CastId = null,
        CastEndsTick = null,
        CastStartTick = null,
        GcdReadyTick = 0,
    };

    /// <summary>sim/combat/actors.ts:43-45 isCasting: a cast id is set.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>True while casting.</returns>
    public static bool IsCasting(ActorState actor) => actor.CastId is not null;

    /// <summary>
    /// The Chanter's verse (BIBLE section 5: "always in Mend or Ward verse"). It is the class loop resource
    /// (content/data/classes/chanter.json loop "verse", max 1): 1 is Mend, 0 is Ward, the same reading the healer bot
    /// uses (Keyrun.Bots HealerRole MendVerseValue). An actor starts at full resource, so a Chanter opens in Mend.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns><see cref="Abilities.MendVerse"/> or <see cref="Abilities.WardVerse"/>.</returns>
    public static string Verse(ActorState actor) => actor.Resource >= 1 ? Abilities.MendVerse : Abilities.WardVerse;

    /// <summary>sim/combat/actors.ts:47-50 reviveActor: alive again at <paramref name="health"/>, or full health.</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="health">Health to revive at; null = maxHealth.</param>
    public static void Revive(ActorState actor, long? health = null)
    {
        actor.Alive = true;
        actor.Health = health ?? actor.MaxHealth;
    }
}
