using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Drives a model's own AnimationPlayer from sim state the view already reads (design/ART-PLAN.md section 6): a death
/// clip once when <c>Alive</c> goes false (its last frame held), a cast loop while <c>CastId</c> is set, a one-shot
/// when an ability resolves (the sim's <c>cast_complete</c> for this actor, or the global cooldown restarting) or health
/// drops (a hit), and idle or run from the interpolated speed. COMBAT-ANIM: the choice is <see cref="AnimRule"/>'s, and
/// a one-shot plays to its end whether the actor moves or not, then returns to run or idle. Lane 4: a spawn clip once when the body is first bound (<see cref="PlaySpawn"/>; skeletons
/// rise out of the floor; KF1-SPAWN-HOLD: held only while <see cref="HoldAnim.KeepsRise"/>, so moving, casting, a swing,
/// a stagger or death cuts it with a <see cref="HoldAnim.RiseCutBlendMs"/> blend, and a fresh idle loop starts at the
/// actor's own <see cref="HoldAnim.IdlePhaseMs"/>), and the hit clip as the stagger reaction when a
/// fresh <see cref="ActorBinding.StaggerEffectId"/> effect lands (the Twins' break bar emptying). Read-only: it never
/// writes sim state. KF1: the swing one-shot starts at <see cref="AnimRule.SwingStartMs"/> (its blow lands on the hit
/// tick, not 300 to 800 ms after it), and a caster the sim's <c>interrupt</c> event targets plays the hit clip (a kick
/// flinch). KF1-WINDUP (<see cref="AbilityAnim"/>): a cast plays its swing's wind-up so the contact frame lands on the
/// resolve frame (a strike for the whole bar instead of the Spellcasting loop), and an ability that reads differently
/// swings its own clip. KF1-READABLE: a cast's own <c>cast_complete</c> plays the release over the next cast it chains
/// into (for <see cref="AnimRule.ReleaseEndsMs"/>, then the cast pose resumes), and every clip start prints a
/// <see cref="AnimRule.ViewLine"/> (<c>VIEW anim actor=N clip=X tick=T</c>). A model without an AnimationPlayer gets no
/// animator (<see cref="TryCreate"/> returns null), so it
/// keeps its static pose. ABILITY-ANIMS: every content ability has its own release and cast loop
/// (<see cref="AbilityClipTable"/> via <see cref="AbilityClipPlayer"/>), and each ability, flinch or death clip start
/// writes <c>ANIM actor=N clip=X ability=Y at=T</c> to the run log. ANIMS-READABLE-2 (<see cref="ReleaseHold"/>): a
/// release shows for at least 300 ms before a chained cast's pose or wind-up takes the body back, a clip with less left
/// is slowed, and every ability one step resolved plays in turn.
/// </summary>
public sealed class ActorAnimator
{
    /// <summary>Above this interpolated speed (m/s) the run clip plays; sim walk speed is 3.6 m/s.</summary>
    public const float MoveSpeedMps = 0.4f;

    private const float Blend = 0.15f;

    private readonly AnimationPlayer _player;
    // R-IDLEANIM-1: the run-log sink the VIEW proof lines go to (null: console only, as AbilityClipPlayer falls back).
    private readonly Action<string>? _log;
    // R-IDLEANIM-1: the locomotion clip chosen on the last frame that chose one, so LocoLine prints only on a change.
    private string? _lastLoco;
    private readonly string? _idle;
    private readonly string? _move;
    private readonly string? _cast;
    private readonly string? _act;
    private readonly string? _hit;
    private readonly string? _death;
    private readonly string? _spawn;
    private readonly string _rowAct;
    private readonly Dictionary<string, string?> _abilityClips = new(StringComparer.Ordinal);
    private string? _oneShot;
    private AnimClip _oneShotKind;
    private string? _windup;
    private bool _dead;
    private bool _spawning;
    private readonly string _moveClip;
    private readonly int _scalePermille;
    // ABILITY-ANIMS: every ability's own release and cast loop (AbilityClipTable) and the ANIM run-log line per start.
    private readonly AbilityClipPlayer _abilities;
    private readonly Dictionary<string, string?> _castLoops = new(StringComparer.Ordinal);
    private string? _castAbility;
    // ANIMS-READABLE-2: where the playing release started (ms into its clip; its window is ReleaseHold.EndsMs from here)
    // and the abilities one step resolved after the first, played in turn once each release has shown.
    private int _releaseStartMs;
    private readonly Queue<string> _queued = new();
    // MOVE-HOLD: the tick of the last sim position change (-1: never moved), read through MoveHold.Holds.
    private int _lastMovedTick = -1;
    // MOVE-HOLD: the interpolated speed (m/s) of the last frame that read above MoveSpeedMps, the gait rate during a hold.
    private float _keptMoveSpeedMps;

    private ActorAnimator(AnimationPlayer player, ActorClips clips, int scalePermille, Action<string>? log)
    {
        _player = player;
        _log = log;
        _abilities = new AbilityClipPlayer(log);
        _moveClip = clips.Move;
        _scalePermille = scalePermille;
        var names = player.GetAnimationList();
        _idle = Match(names, clips.Idle, loop: true);
        _move = Match(names, clips.Move, loop: true);
        _cast = Match(names, clips.Cast, loop: true);
        _act = Match(names, clips.Act, loop: false);
        _hit = Match(names, clips.Hit, loop: false);
        _death = Match(names, clips.Death, loop: false);
        _spawn = clips.Spawn is null ? null : Match(names, clips.Spawn, loop: false);
        _rowAct = clips.Act;
        _oneShotAct = clips.Act;
        if (_act is not null)
        {
            _abilityClips[clips.Act] = _act;
        }
    }

    // KF1-READABLE: the swing clip name of the playing Act one-shot (the row's, or the resolved ability's own from
    // AbilityAnim.ActClip); AnimRule's contact table is keyed by it, not by the matched library path.
    private string _oneShotAct;

    /// <summary>The clip names this animator resolved, for the ACTOR log line ("idle=Idle move=Running_A ...").</summary>
    public string Matched =>
        $"idle={_idle ?? "-"} move={_move ?? "-"} cast={_cast ?? "-"} act={_act ?? "-"} hit={_hit ?? "-"} death={_death ?? "-"} spawn={_spawn ?? "-"}";

    /// <summary>
    /// Plays the row's spawn clip once from its first frame (the body rising where it spawned); a row without one, or
    /// whose clip the model lacks, starts in idle instead. The idle start passes the actor id and bind tick, so its one
    /// ANIM line names the actor (an unengaged boss would otherwise log nothing until its first loop change).
    /// </summary>
    public void PlaySpawn(int actorId = -1, int tick = -1)
    {
        if (_spawn is null)
        {
            Play(_idle, actorId: actorId, tick: tick);
            return;
        }
        _spawning = true;
        _oneShot = null;
        _player.Play(_spawn);
        _player.Seek(0, true);
    }

    /// <summary>True while the death clip is (or has finished) playing: the view keeps the body visible.</summary>
    public bool HoldsDeath => _dead && _death is not null;

    /// <summary>
    /// An animator for the first AnimationPlayer under <paramref name="model"/>, or null when it has none.
    /// <paramref name="scalePermille"/> is the scale the model is drawn at (KF1-GAIT: its stride scales with it);
    /// <paramref name="log"/> is the run-log sink the ANIM lines go to (null: console only).
    /// </summary>
    public static ActorAnimator? TryCreate(Node model, ActorClips clips, int scalePermille = 1000, Action<string>? log = null)
    {
        foreach (var node in model.FindChildren("*", "AnimationPlayer", true, false))
        {
            if (node is AnimationPlayer player)
            {
                return new ActorAnimator(player, clips, scalePermille, log);
            }
        }
        return null;
    }

    /// <summary>
    /// One frame. <paramref name="newTick"/> is true on the first frame after a sim step, when
    /// <paramref name="before"/> and <paramref name="now"/> are that step's two states and <paramref name="events"/> its
    /// events (the <c>cast_complete</c> for this actor is the swing signal, <see cref="AnimRule.Resolved"/>).
    /// </summary>
    public void Update(ActorState before, ActorState now, bool newTick, float speedMps,
        IReadOnlyList<SimEvent>? events = null, int tick = -1, int alphaPermille = 0)
    {
        if (!now.Alive)
        {
            if (!_dead)
            {
                _dead = true;
                _spawning = false;
                _oneShot = null;
                _player.SpeedScale = 1f;
                Play(_death);
                if (_death is not null)
                {
                    Emit(AnimRule.ViewLine(now.Id, _death, tick) + " kind=Death");
                    _abilities.Started(now.Id, _death, null, tick);
                }
            }
            _queued.Clear();
            return;
        }
        if (_dead)
        {
            _dead = false; // a fresh run reuses the static Warden's view
            _player.Stop();
        }

        // MOVE-HOLD: the sim steps a body's position every few ticks, so the interpolated speed reads zero between steps.
        // The tick of the last position change keeps the Move clip through those gaps (MoveHold.HoldTicks).
        if (newTick && !before.Pos.Equals(now.Pos))
        {
            _lastMovedTick = tick;
        }

        // ANIMS-READABLE-2: every ability this step resolved for this actor, in order. The first releases now; the rest
        // queue and release in turn once the playing release has shown for ReleaseHold.MinReleaseMs (a cast and the
        // instant fired on its resolve, Flare then Cold Snap, used to draw only the first).
        string? resolvedId = null;
        var queuedStart = false;
        if (newTick && AbilityClipPlayer.Resolved(now.Id, events) is { Count: > 0 } resolvedAll)
        {
            _queued.Clear();
            resolvedId = resolvedAll[0];
            for (var i = 1; i < resolvedAll.Count; i++)
            {
                _queued.Enqueue(resolvedAll[i]);
            }
        }
        else if (ReleaseHold.NextQueuedNow(_queued.Count, PlayingKind(), InReleaseNow()))
        {
            resolvedId = _queued.Dequeue();
            queuedStart = true;
        }
        // A release still inside its window holds the body over a cast that started meanwhile (the next cast's pose or
        // its strike wind-up waits; the wind-up is posed from sim time, so it resumes at the right point).
        if (resolvedId is null && now.CastId is not null && ReleaseHold.HoldsOffWindup(PlayingKind(), InReleaseNow()))
        {
            _lastLoco = null; // R-IDLEANIM-1: a held release over a cast is a cast frame, not a locomotion frame
            return;
        }

        // KF1-WINDUP: a cast whose swing has a measured contact frame plays that swing's wind-up (a strike for its whole
        // bar, a spell for its release window), posed from sim time each frame so contact lands on the resolve frame.
        // ANIMS-READABLE-2: not on a frame that releases an ability (the release plays first).
        if (resolvedId is null && now.CastId is string castId && now.CastStartTick is int castStart && now.CastEndsTick is int castEnds)
        {
            var name = AbilityClipPlayer.ReleaseName(castId, _rowAct);
            var windClip = AbilityClip(name);
            if (windClip is not null && AbilityAnim.WindupFor(castId, _rowAct, castStart, castEnds, tick, alphaPermille) is WindupPose pose)
            {
                if (_windup != windClip || _player.CurrentAnimation != windClip)
                {
                    // KF1-SPAWN-HOLD: a cast cuts the spawn rise (HoldAnim.KeepsRise is false while casting) with the
                    // longer rise-cut blend, so a body cut mid-rise into its wind-up comes up out of the floor.
                    var windBlend = Blend;
                    if (_spawning && _spawn is not null && _player.IsPlaying() && _player.CurrentAnimation == _spawn)
                    {
                        Emit(FormattableString.Invariant(
                            $"ANIM risecut actor={now.Id} tick={tick} at={_player.CurrentAnimationPosition:0.00} moving={(speedMps > MoveSpeedMps ? 1 : 0)} casting=1 acted=0 staggered=0"));
                        windBlend = HoldAnim.RiseCutBlendMs / 1000f;
                    }
                    _spawning = false;
                    _player.Play(windClip, windBlend);
                    Emit(FormattableString.Invariant(
                        $"ANIM windup actor={now.Id} tick={tick} clip={windClip} ability={castId} ends={castEnds} from={pose.ClipMs} speed={pose.SpeedPermille}"));
                    _abilities.Started(now.Id, windClip, castId, tick);
                }
                _windup = windClip;
                _oneShot = null;
                _lastLoco = null; // R-IDLEANIM-1: a wind-up is a cast frame; the return to idle or move after it prints
                _player.SpeedScale = pose.SpeedPermille / 1000f;
                _player.Seek(pose.ClipMs / 1000f, true);
                return;
            }
        }
        // A wind-up the cast no longer shows: it resolved this step (the swing carries on from contact) or was cut (a
        // kick flinches, a stun or cancel returns to run or idle below).
        var woundUp = _windup;
        _windup = null;

        // COMBAT-ANIM: the clip choice is AnimRule's (Keyrun.Content, pure); this only gathers the facts and plays.
        var playing = PlayingKind();
        // KF1-WINDUP, ABILITY-ANIMS: the swing is the resolved ability's own clip (AbilityClipTable), else the row's.
        var actName = AbilityClipPlayer.ReleaseName(resolvedId, _rowAct);
        var actClip = AbilityClip(actName);
        var released = actClip is not null && resolvedId is not null;
        // ABILITY-ANIMS: the cast bar shows the ability's own loop (a spell's Spellcasting, Bulwark's Block), else the row's.
        _castAbility = now.CastId;
        // MOVE-HOLD: a moving body keeps its Move clip (and the speed it walked at) through the gaps between sim steps.
        var holding = speedMps <= MoveSpeedMps && MoveHold.Holds(tick, _lastMovedTick);
        if (speedMps > MoveSpeedMps)
        {
            _keptMoveSpeedMps = speedMps;
        }
        var facts = new AnimFacts(
            Alive: true,
            NewTick: newTick || queuedStart,
            Moving: speedMps > MoveSpeedMps || holding,
            Casting: now.CastId is not null && CastLoop() is not null,
            Acted: actClip is not null && (released || now.GcdReadyTick > before.GcdReadyTick),
            Hurt: _hit is not null && now.Health < before.Health,
            Staggered: _hit is not null && (StaggerLanded(before, now) || AnimRule.Kicked(now.Id, Targets(events))),
            Playing: playing,
            // KF1-READABLE: a release plays over the cast it chains into, for its window, then the cast pose resumes.
            Released: released,
            // ANIMS-READABLE-2: the window runs at least ReleaseHold.MinReleaseMs from where the release started.
            InRelease: playing == AnimClip.Act && InReleaseNow());

        // KF1-SPAWN-HOLD: the spawn rise holds only while the body stands still with nothing else to show
        // (HoldAnim.KeepsRise); a chaser, caster or swinger cuts it with a longer blend instead of sliding a half-buried
        // body across the floor for the whole 3.6 s rise.
        var blend = Blend;
        if (_spawning && _spawn is not null)
        {
            if (_player.IsPlaying() && _player.CurrentAnimation == _spawn)
            {
                if (HoldAnim.KeepsRise(facts))
                {
                    return;
                }
                Emit(FormattableString.Invariant(
                    $"ANIM risecut actor={now.Id} tick={tick} at={_player.CurrentAnimationPosition:0.00} moving={(facts.Moving ? 1 : 0)} casting={(facts.Casting ? 1 : 0)} acted={(facts.Acted ? 1 : 0)} staggered={(facts.Staggered ? 1 : 0)}"));
                blend = HoldAnim.RiseCutBlendMs / 1000f;
            }
            _spawning = false;
        }

        var decision = AnimRule.Decide(facts);
        var clip = decision.Clip == AnimClip.Act ? actClip : ClipFor(decision.Clip);
        if (decision.OneShot && clip is not null)
        {
            _lastLoco = null; // R-IDLEANIM-1: a one-shot resets the loco change check, so the settle back to idle or move prints
            _oneShot = clip;
            _oneShotKind = decision.Clip;
            if (decision.Restart)
            {
                // R-ANIM-2 (b): an instant's swing starts at frame 0 on the resolve tick (AnimRule.SwingStartMs); the hit
                // presentation waits for contact (ActorView). KF1-WINDUP: a swing the cast already wound up carries on from contact.
                var from = decision.Clip == AnimClip.Act
                    ? AbilityAnim.ResolveStartMs(actName, woundUp is not null && woundUp == clip) / 1000f
                    : 0f;
                var speedPermille = 1000; // a one-shot's contact frame is timed at its own rate (AnimRule.SwingStartMs)
                if (decision.Clip == AnimClip.Act)
                {
                    _oneShotAct = actName;
                    // ANIMS-READABLE-2: the release window counts from here, and a clip with less than
                    // ReleaseHold.MinReleaseMs left is slowed to last that long (never cut).
                    _releaseStartMs = (int)(from * 1000f);
                    if (_player.GetAnimation(clip) is { } releaseAnim)
                    {
                        speedPermille = ReleaseHold.SpeedPermille(_releaseStartMs, (int)(releaseAnim.Length * 1000f));
                    }
                }
                _player.SpeedScale = speedPermille / 1000f;
                _player.Play(clip, blend);
                _player.Seek(from, true);
                // Proof line (like ACTOR): which one-shot started, on whom, on which tick, from where in the clip.
                Emit(AnimRule.ViewLine(now.Id, clip, tick) + FormattableString.Invariant(
                    $" kind={decision.Clip} from={from:0.00} moving={(facts.Moving ? 1 : 0)} casting={(facts.Casting ? 1 : 0)}"));
                // ABILITY-ANIMS proof: a swing names the ability it resolves, a flinch the cast its kick cut.
                _abilities.Started(now.Id, clip, decision.Clip == AnimClip.Act
                    ? resolvedId
                    : AbilityClipPlayer.Kicked(now.Id, events), tick);
            }
            return;
        }
        _oneShot = null;
        var next = clip ?? _idle;
        if (decision.Clip == AnimClip.Cast && next is not null && !(_player.CurrentAnimation == next && _player.IsPlaying()))
        {
            Emit(AnimRule.ViewLine(now.Id, next, tick) + " kind=Cast");
            _abilities.Started(now.Id, next, now.CastId, tick);
        }
        // R-IDLEANIM-1: an idle or move frame logs its clip only when the chosen locomotion clip changes, not every frame.
        // The kind names the clip actually played (a move with no move clip plays idle), and moving is the flag Decide read.
        if (decision.Clip is AnimClip.Idle or AnimClip.Move && next is not null && next != _lastLoco)
        {
            _lastLoco = next;
            Emit(AnimRule.LocoLine(now.Id, next == _move ? AnimClip.Move : AnimClip.Idle, tick, facts.Moving));
        }
        else if (decision.Clip == AnimClip.Cast)
        {
            _lastLoco = null; // R-IDLEANIM-1: a cast frame is not a locomotion frame; the settle back to idle or move prints
        }
        // KF1-GAIT: the run or walk loop steps at the body's speed, so the planted foot stays still on the floor.
        // MOVE-HOLD: inside the hold window the raw speed reads zero between steps, so the loop keeps the last moving speed
        // instead of freezing mid-stride at rate 0.
        var gaitSpeedMps = holding ? _keptMoveSpeedMps : speedMps;
        _player.SpeedScale = next is not null && next == _move
            ? GaitRule.PlaybackPermille(_moveClip, (int)(gaitSpeedMps * 1000f), _scalePermille) / 1000f
            : 1f;
        Play(next, blend, now.Id, tick);
    }

    private string? ClipFor(AnimClip clip) => clip switch
    {
        AnimClip.Idle => _idle,
        AnimClip.Move => _move ?? _idle,
        AnimClip.Cast => CastLoop(),
        AnimClip.Act => _act,
        AnimClip.Hit => _hit,
        AnimClip.Death => _death,
        _ => null,
    };

    // ABILITY-ANIMS: the loop for the current cast's bar, the ability's own (AbilityClipTable.CastClip) when the model
    // ships it, else the row's. Matched once per name, looped like the row's cast clip.
    private string? CastLoop()
    {
        if (AbilityClipPlayer.CastName(_castAbility) is not string name)
        {
            return _cast;
        }
        if (!_castLoops.TryGetValue(name, out var clip))
        {
            clip = Match(_player.GetAnimationList(), name, loop: true);
            _castLoops[name] = clip;
        }
        return clip ?? _cast;
    }

    // The model's animation for a swing clip name (the row's, or an ability's own from AbilityAnim.ActClip), or null
    // when the model lacks it. Matched once per name.
    private string? AbilityClip(string name)
    {
        if (!_abilityClips.TryGetValue(name, out var clip))
        {
            clip = Match(_player.GetAnimationList(), name, loop: false);
            _abilityClips[name] = clip;
        }
        return clip;
    }

    // The one-shot still playing from an earlier frame, or None.
    private AnimClip PlayingKind() =>
        _oneShot is not null && _player.IsPlaying() && _player.CurrentAnimation == _oneShot ? _oneShotKind : AnimClip.None;

    // ANIMS-READABLE-2: true while the playing release is inside its window (ReleaseHold.EndsMs from where it started).
    private bool InReleaseNow() =>
        PlayingKind() == AnimClip.Act &&
        ReleaseHold.InRelease(_oneShotAct, _releaseStartMs, (int)(_player.CurrentAnimationPosition * 1000.0));

    // One step's events as (type, targetId) pairs for AnimRule.Kicked.
    private static IEnumerable<(string Type, int? TargetId)> Targets(IReadOnlyList<SimEvent>? events)
    {
        if (events is null)
        {
            yield break;
        }
        foreach (var e in events)
        {
            yield return (e.Type, e.Get("targetId") is JsInt id ? (int)id.Value : null);
        }
    }

    // True on the step a stagger effect is applied or refreshed: its latest end tick moved later.
    private static bool StaggerLanded(ActorState before, ActorState now) =>
        StaggerEnds(now) > StaggerEnds(before);

    private static int StaggerEnds(ActorState actor)
    {
        var ends = int.MinValue;
        foreach (var e in actor.Effects)
        {
            if (e.EffectId == ActorBinding.StaggerEffectId && e.EndsTick > ends)
            {
                ends = e.EndsTick;
            }
        }
        return ends;
    }

    // Writes one proof line through the run-log sink, or to the console when there is none (AbilityClipPlayer's fallback).
    private void Emit(string line)
    {
        if (_log is null)
        {
            GD.Print(line);
            return;
        }
        _log(line);
    }

    private void Play(string? clip, float blend = Blend, int actorId = -1, int tick = -1)
    {
        if (clip is null || (_player.CurrentAnimation == clip && _player.IsPlaying()))
        {
            return;
        }
        // ANIM run log: the looping clip this start changes from, read before the player moves on.
        var previous = _player.IsPlaying() ? LoopKindOf(_player.CurrentAnimation) : AnimClip.None;
        _player.Play(clip, blend);
        // KF1-SPAWN-HOLD: an idle loop starts at the actor's own phase (HoldAnim.IdlePhaseMs), so a pack bound on one
        // frame (every pack at run start under prespawn) does not breathe in lockstep while it holds.
        if (clip == _idle && actorId >= 0 && _player.GetAnimation(clip) is { } anim)
        {
            var phaseMs = HoldAnim.IdlePhaseMs(actorId, (int)(anim.Length * 1000f));
            _player.Seek(phaseMs / 1000f, true);
        }
        // ANIM run log: a change to the idle or move loop writes one proof line, through the same sink as AbilityClipPlayer.
        var kind = LoopKindOf(clip);
        var line = actorId >= 0 && kind != AnimClip.None ? AnimRunLog.Line(actorId, previous, kind, tick) : null;
        if (line is not null)
        {
            Emit(line);
        }
    }

    // ANIM run log: which looping clip a clip name is (Idle or Move), or None for any other clip.
    private AnimClip LoopKindOf(string? name) =>
        name is null ? AnimClip.None : name == _idle ? AnimClip.Idle : name == _move ? AnimClip.Move : AnimClip.None;

    // Exact name first, then a case-insensitive substring; loops the looping states on the shared animation resource.
    private string? Match(IReadOnlyList<string> names, string wanted, bool loop)
    {
        string? hit = null;
        foreach (var n in names)
        {
            if (n == wanted || n.EndsWith("/" + wanted, StringComparison.Ordinal))
            {
                hit = n;
                break;
            }
        }
        if (hit is null)
        {
            foreach (var n in names)
            {
                if (n.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    hit = n;
                    break;
                }
            }
        }
        if (hit is not null && loop)
        {
            var anim = _player.GetAnimation(hit);
            if (anim is not null)
            {
                anim.LoopMode = Animation.LoopModeEnum.Linear;
            }
        }
        return hit;
    }
}
