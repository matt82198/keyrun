using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>Where a cast's swing clip is posed on one render frame (<see cref="AbilityAnim.Windup"/>).</summary>
/// <param name="ClipMs">Position in the clip, ms: the view seeks here every frame of the cast.</param>
/// <param name="SpeedPermille">The rate the clip runs at between frames, 1/1000 of its own (at most 1000).</param>
public readonly record struct WindupPose(int ClipMs, int SpeedPermille);

/// <summary>
/// KF1-WINDUP (finding f-fa28e36e, Matt's KF1 verdict "combat animation reads badly"; BIBLE s3: cast bars are the
/// enemy's language). Pure, no Godot types; src/View/ActorAnimator.cs reads it.
/// <list type="bullet">
/// <item><see cref="ActClip"/>: the abilities that read differently play their own clip from the CC0 packs already in the
/// repo (D17) instead of the row's one swing: the Warden's Cleave sweeps (2H slice) where its Slam chops, the Cantor's
/// heal raises its staff where its attack shoots, the Seed's Slam leaps onto the floor where its Lash kicks, the
/// player Warden's Guard Break slices, Reckoning stabs and Shield Bash punches with the shield.</item>
/// <item><see cref="Windup"/>: a cast whose swing is a weapon or body strike (<see cref="IsStrike"/>) plays that strike's
/// own wind-up for its whole cast bar (never the row's Spellcasting loop), stretched to the bar down to
/// <see cref="MinWindupSpeedPermille"/>, so the strike reaches its contact frame on the frame the sim resolves the hit.
/// A spell keeps the row's cast loop and plays its release wind-up at full speed for the last contact-length of the
/// bar, so the release lands on the resolve tick instead of starting there.</item>
/// <item><see cref="ResolveStartMs"/>: on the resolve tick a wound-up swing continues from contact; an instant (no cast
/// bar, nothing to wind up in) keeps <see cref="AnimRule.SwingStartMs"/>.</item>
/// </list>
/// Timing frame: the sim resolves a cast in the step that processes tick <c>castEndsTick</c>, so the first state that
/// shows the hit has <c>Tick == castEndsTick + 1</c>; the first state that shows the cast has
/// <c>Tick == castStartTick + 1</c>. A render frame is that latest state's tick plus the render alpha into the next.
/// </summary>
public static class AbilityAnim
{
    /// <summary>One sim tick, ms.</summary>
    public const int TickMs = 50;

    /// <summary>
    /// The slowest a strike's wind-up is stretched to, 1/1000 of its own rate. A cast bar longer than contact / 0.4
    /// holds the clip's first frame (the readied stance) until the wind-up window opens.
    /// </summary>
    public const int MinWindupSpeedPermille = 400;

    // Ability id -> its swing clip, where it differs from the row's. Every clip here has a measured contact frame in
    // AnimRule.ContactMs and ships in the model of every actor that owns the ability (AbilityAnimTests).
    private static readonly Dictionary<string, string> ActByAbility = new(StringComparer.Ordinal)
    {
        ["hollowWarden.cleave"] = "2H_Melee_Attack_Slice",
        // LOCK-TELEGRAPH made the Lock a cast with a bar: it is the boss raising its shield, a spell, so without this
        // the row's swing would wind up a strike for the whole Lock bar (KF1-WINDUP: no spell winds up a punch).
        ["hollowWarden.lock"] = "Spellcast_Raise",
        ["choir.healCast"] = "Spellcast_Raise",
        ["remnant.healCast"] = "Spellcast_Raise",
        // The Pair's marks are spells: a skeleton minion's row swing is a punch, so without these a mark cast would
        // wind up a punch for its whole bar.
        ["pair.spreadMark"] = "Spellcast_Shoot",
        ["pair.stackCall"] = "Spellcast_Raise",
        // D66: the Sunken Bellfoundry's casts on punch rows that are spells, not strikes (a core stoked, a heal, a Lock).
        ["furnaceThrall.stokeCore"] = "Spellcast_Raise",
        // R-SORROW-1: the Thrall's Slag Burst and the Hound's Boiling Blood became casts on punch rows; both are spells.
        ["furnaceThrall.slagBurst"] = "Spellcast_Raise",
        ["scaldHound.boilingBlood"] = "Spellcast_Raise",
        ["ommet.temper"] = "Spellcast_Raise",
        ["greatBell.resonance"] = "Spellcast_Raise",
        // Bulwark is a cast shield wall, not a strike: the Block pose for the bar, then the raise as it lands.
        ["warden.bulwark"] = "Spellcast_Raise",
        ["seed.slam"] = "1H_Melee_Attack_Jump_Chop",
        ["warden.guardBreak"] = "2H_Melee_Attack_Slice",
        ["warden.reckoning"] = "1H_Melee_Attack_Stab",
        ["warden.shieldBash"] = "Block_Attack",
    };

    /// <summary>Every ability id with its own swing clip.</summary>
    public static IReadOnlyCollection<string> MappedAbilities => ActByAbility.Keys;

    /// <summary>
    /// The swing clip <paramref name="abilityId"/> plays: its own when it reads differently from the row's swing,
    /// otherwise <paramref name="rowAct"/> (also for a null or unknown id).
    /// </summary>
    public static string ActClip(string? abilityId, string rowAct)
    {
        ArgumentNullException.ThrowIfNull(rowAct);
        return abilityId is not null && ActByAbility.TryGetValue(abilityId, out var clip) ? clip : rowAct;
    }

    /// <summary>True for a weapon, fist, foot or shield strike (its cast shows the strike's wind-up, not a spell pose).</summary>
    public static bool IsStrike(string clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return clip.Contains("_Melee_Attack_", StringComparison.Ordinal) || clip == "Block_Attack";
    }

    /// <summary>
    /// The pose of <paramref name="clip"/> on a render frame of a cast from <paramref name="castStartTick"/> to
    /// <paramref name="castEndsTick"/> (the sim actor's <c>CastStartTick</c>/<c>CastEndsTick</c>), where
    /// <paramref name="stateTick"/> is the latest sim state's tick and <paramref name="alphaPermille"/> the render alpha
    /// into the next tick (0 to 999). Null means the row's cast loop: a spell before its release window, a clip with no
    /// measured contact frame, or a cast with no length. At <c>stateTick == castEndsTick + 1</c>, alpha 0 (the frame the
    /// hit resolves) the pose is the contact frame.
    /// </summary>
    public static WindupPose? Windup(string clip, int castStartTick, int castEndsTick, int stateTick, int alphaPermille)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (AnimRule.ContactMs(clip) is not int contact || castEndsTick <= castStartTick)
        {
            return null;
        }
        var castMs = (castEndsTick - castStartTick) * TickMs;
        var remaining = ((castEndsTick + 1 - stateTick) * TickMs) - (Math.Clamp(alphaPermille, 0, 999) * TickMs / 1000);
        remaining = Math.Clamp(remaining, 0, castMs);
        int speed;
        if (IsStrike(clip))
        {
            speed = Math.Clamp(contact * 1000 / castMs, MinWindupSpeedPermille, 1000);
        }
        else
        {
            if (remaining >= contact)
            {
                return null;
            }
            speed = 1000;
        }
        return new WindupPose(Math.Max(0, contact - (remaining * speed / 1000)), speed);
    }

    /// <summary>
    /// Where the swing one-shot starts on the tick its ability resolves, ms into <paramref name="clip"/>: the contact
    /// frame when the cast already wound it up (<paramref name="woundUp"/>), so the blow carries on without jumping back;
    /// otherwise <see cref="AnimRule.SwingStartMs"/> (an instant).
    /// </summary>
    public static int ResolveStartMs(string clip, bool woundUp) =>
        woundUp && AnimRule.ContactMs(clip) is int contact ? contact : AnimRule.SwingStartMs(clip);
}
