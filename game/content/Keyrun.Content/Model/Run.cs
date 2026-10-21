namespace Keyrun.Content;

/// <summary>
/// Run-level movement rules and arena bounds (game/content/data/run.json, logical path
/// <c>run.json</c>). The frozen TS client kept these as constants in
/// client/src/sim-host/run-setup.ts (BASE_SPEED_MM_PER_TICK :53, ACTOR_RADIUS_MM :54,
/// DUNGEON_BOUNDS :57); content/data had no field for them. Every member is required.
/// </summary>
public sealed record RunConfig
{
    /// <summary>Walk speed and actor radius shared by every actor.</summary>
    public required RunMovement Movement { get; init; }

    /// <summary>The XZ box every actor is clamped into, mm.</summary>
    public required ArenaBounds ArenaBounds { get; init; }

    /// <summary>The Cinder Heat/Overheat rule's Chill (BIBLE section 5); not in the frozen content/data.</summary>
    public required HeatOverheatConfig HeatOverheat { get; init; }

    /// <summary>The Pair's spread and stack consequences (BIBLE section 9.6 item 2); not in the frozen content/data.</summary>
    public required PairMarksConfig PairMarks { get; init; }

    /// <summary>The key's Fortified/Tyrannical affixes and the Clause's starting key (BIBLE s8, plan M5).</summary>
    public required AffixesConfig Affixes { get; init; }

    /// <summary>
    /// STUN-STOPS-EVERY-CAST (finding f-c63b43bf, ruling "defer to WoW"): the enemy ids immune to stuns (the boss). A
    /// stun stops every cast of every other actor, the uninterruptible ones included, since "uninterruptible" (an
    /// ability's <c>interruptible: false</c>) means kick-immune only; stun immunity is this separate flag. Kept here,
    /// not on the enemy's entry in the frozen content/data (STATE.md D12), like the other C#-only rules in this file.
    /// Every id names a known enemy.
    /// </summary>
    public required ValueList<string> StunImmuneEnemies { get; init; }
}

/// <summary>
/// BIBLE s8 (Matt 2026-10-20, "follow current WoW"): from <see cref="OneAffixKey"/> one of Fortified or Tyrannical is
/// on and rotates, from <see cref="BothAffixKey"/> both are on, and a key-set Clause runs from
/// <see cref="ClauseStartKey"/>. The bp values are WoW-derived pending Matt, so they live here as data.
/// </summary>
public sealed record AffixesConfig
{
    /// <summary>First key with one rotating affix (positive).</summary>
    public required int OneAffixKey { get; init; }

    /// <summary>First key with both affixes (greater than <see cref="OneAffixKey"/>).</summary>
    public required int BothAffixKey { get; init; }

    /// <summary>First key that carries a Clause (positive).</summary>
    public required int ClauseStartKey { get; init; }

    /// <summary>Non-boss enemies' extra health and damage.</summary>
    public required AffixScaleConfig Fortified { get; init; }

    /// <summary>The boss's and its adds' extra health and damage.</summary>
    public required AffixScaleConfig Tyrannical { get; init; }
}

/// <summary>One affix's extra health and damage, basis points (non-negative; 2000 = +20%).</summary>
public sealed record AffixScaleConfig
{
    /// <summary>Extra health, bp.</summary>
    public required int HealthBp { get; init; }

    /// <summary>Extra damage dealt, bp.</summary>
    public required int DamageBp { get; init; }
}

/// <summary>
/// BIBLE section 5 (Cinder): "At 100 the next cast is a free Overheat that dumps Heat to 0 and applies Chill (slower
/// casts for 3 s)." The threshold is the heat-overheat class loop max (classes/cinder.json loop.max); this names the
/// Chill the Overheat applies to the caster.
/// </summary>
public sealed record HeatOverheatConfig
{
    /// <summary>The effect id the Overheat applies (must exist in the effects; "chill", hasteBp -3000).</summary>
    public required string ChillEffectId { get; init; }

    /// <summary>Chill duration, ticks (positive; 60 = 3 s at 20 Hz).</summary>
    public required int ChillTicks { get; init; }
}

/// <summary>
/// BIBLE section 9.6 item 2 (the Pair): "every 20 s one player is marked to spread (a ring: stand apart), ten seconds
/// later the pack calls a stack (a circle: stand together, damage split)." The content effects spreadMarked and
/// stackMarked carry no modifiers; these values give them consequences when they run out (Keyrun.Sim PairMarks). The
/// BIBLE gives no numbers; the shipped ones are conservative against the party's 250/400 health (HostRunSetup Roster):
/// a spread detonation deals 80 to each other player within the spread ring (3000 mm, the telegraph's radius); a stack
/// hit is 150 split by the headcount within the stack circle (1500 mm, the telegraph's radius), so alone 150, two 75,
/// three 50 each.
/// </summary>
public sealed record PairMarksConfig
{
    /// <summary>The spread mark's effect id (must exist in the effects; "spreadMarked").</summary>
    public required string SpreadEffectId { get; init; }

    /// <summary>Detonation radius around a spread-marked player, mm (positive).</summary>
    public required int SpreadRadiusMm { get; init; }

    /// <summary>Raw damage the detonation deals each other player in the radius (positive).</summary>
    public required int SpreadDamage { get; init; }

    /// <summary>The stack mark's effect id (must exist in the effects; "stackMarked").</summary>
    public required string StackEffectId { get; init; }

    /// <summary>Radius around a stack-marked player that counts as standing together, mm (positive).</summary>
    public required int StackRadiusMm { get; init; }

    /// <summary>Raw damage a stack-marked player takes alone, split by headcount when others stand with it (positive).</summary>
    public required int StackDamage { get; init; }
}

/// <summary>Shared actor movement values.</summary>
public sealed record RunMovement
{
    /// <summary>Base walk speed, mm/tick (positive).</summary>
    public required int SpeedMmPerTick { get; init; }

    /// <summary>Actor cylinder radius for push-out, mm (positive).</summary>
    public required int RadiusMm { get; init; }
}

/// <summary>Arena bounds, mm; MinX &lt; MaxX and MinZ &lt; MaxZ.</summary>
public sealed record ArenaBounds
{
    /// <summary>Lowest x.</summary>
    public required int MinX { get; init; }

    /// <summary>Highest x.</summary>
    public required int MaxX { get; init; }

    /// <summary>Lowest z.</summary>
    public required int MinZ { get; init; }

    /// <summary>Highest z.</summary>
    public required int MaxZ { get; init; }
}
