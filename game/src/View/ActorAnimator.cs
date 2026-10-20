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
/// flinch). A model without an AnimationPlayer gets no animator (<see cref="TryCreate"/> returns null), so it
/// keeps its static pose.
/// </summary>
public sealed class ActorAnimator
{
    /// <summary>Above this interpolated speed (m/s) the run clip plays; sim walk speed is 3.6 m/s.</summary>
    public const float MoveSpeedMps = 0.4f;

    private const float Blend = 0.15f;

    private readonly AnimationPlayer _player;
    private readonly string? _idle;
    private readonly string? _move;
    private readonly string? _cast;
    private readonly string? _act;
    private readonly string? _hit;
    private readonly string? _death;
    private readonly string? _spawn;
    private readonly float _actStartSeconds;
    private string? _oneShot;
    private bool _dead;
    private bool _spawning;

    private ActorAnimator(AnimationPlayer player, ActorClips clips)
    {
        _player = player;
        var names = player.GetAnimationList();
        _idle = Match(names, clips.Idle, loop: true);
        _move = Match(names, clips.Move, loop: true);
        _cast = Match(names, clips.Cast, loop: true);
        _act = Match(names, clips.Act, loop: false);
        _hit = Match(names, clips.Hit, loop: false);
        _death = Match(names, clips.Death, loop: false);
        _spawn = clips.Spawn is null ? null : Match(names, clips.Spawn, loop: false);
        // KF1: the swing starts just before its contact frame, so the blow lands on the sim's hit tick.
        _actStartSeconds = AnimRule.SwingStartMs(clips.Act) / 1000f;
    }

    /// <summary>The clip names this animator resolved, for the ACTOR log line ("idle=Idle move=Running_A ...").</summary>
    public string Matched =>
        $"idle={_idle ?? "-"} move={_move ?? "-"} cast={_cast ?? "-"} act={_act ?? "-"} hit={_hit ?? "-"} death={_death ?? "-"} spawn={_spawn ?? "-"}";

    /// <summary>
    /// Plays the row's spawn clip once from its first frame (the body rising where it spawned); a row without one, or
    /// whose clip the model lacks, starts in idle instead.
    /// </summary>
    public void PlaySpawn()
    {
        if (_spawn is null)
        {
            Play(_idle);
            return;
        }
        _spawning = true;
        _oneShot = null;
        _player.Play(_spawn);
        _player.Seek(0, true);
    }

    /// <summary>True while the death clip is (or has finished) playing: the view keeps the body visible.</summary>
    public bool HoldsDeath => _dead && _death is not null;

    /// <summary>An animator for the first AnimationPlayer under <paramref name="model"/>, or null when it has none.</summary>
    public static ActorAnimator? TryCreate(Node model, ActorClips clips)
    {
        foreach (var node in model.FindChildren("*", "AnimationPlayer", true, false))
        {
            if (node is AnimationPlayer player)
            {
                return new ActorAnimator(player, clips);
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
        IReadOnlyList<SimEvent>? events = null, int tick = -1)
    {
        if (!now.Alive)
        {
            if (!_dead)
            {
                _dead = true;
                _spawning = false;
                _oneShot = null;
                Play(_death);
            }
            return;
        }
        if (_dead)
        {
            _dead = false; // a fresh run reuses the static Warden's view
            _player.Stop();
        }

        // COMBAT-ANIM: the clip choice is AnimRule's (Keyrun.Content, pure); this only gathers the facts and plays.
        var playing = _oneShot is not null && _player.IsPlaying() && _player.CurrentAnimation == _oneShot
            ? (_oneShot == _act ? AnimClip.Act : AnimClip.Hit)
            : AnimClip.None;
        var facts = new AnimFacts(
            Alive: true,
            NewTick: newTick,
            Moving: speedMps > MoveSpeedMps,
            Casting: now.CastId is not null && _cast is not null,
            Acted: _act is not null && (AnimRule.Resolved(now.Id, Pairs(events)) || now.GcdReadyTick > before.GcdReadyTick),
            Hurt: _hit is not null && now.Health < before.Health,
            Staggered: _hit is not null && (StaggerLanded(before, now) || AnimRule.Kicked(now.Id, Targets(events))),
            Playing: playing);

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
                GD.Print(FormattableString.Invariant(
                    $"ANIM risecut actor={now.Id} tick={tick} at={_player.CurrentAnimationPosition:0.00} moving={(facts.Moving ? 1 : 0)} casting={(facts.Casting ? 1 : 0)} acted={(facts.Acted ? 1 : 0)} staggered={(facts.Staggered ? 1 : 0)}"));
                blend = HoldAnim.RiseCutBlendMs / 1000f;
            }
            _spawning = false;
        }

        var decision = AnimRule.Decide(facts);
        var clip = ClipFor(decision.Clip);
        if (decision.OneShot && clip is not null)
        {
            _oneShot = clip;
            if (decision.Restart)
            {
                var from = decision.Clip == AnimClip.Act ? _actStartSeconds : 0f;
                _player.Play(clip, blend);
                _player.Seek(from, true);
                // Proof line (like ACTOR): which one-shot started, on whom, on which tick, from where in the clip.
                GD.Print(FormattableString.Invariant(
                    $"ANIM oneshot actor={now.Id} tick={tick} clip={clip} kind={decision.Clip} from={from:0.00} moving={(facts.Moving ? 1 : 0)}"));
            }
            return;
        }
        _oneShot = null;
        Play(clip ?? _idle, blend, now.Id);
    }

    private string? ClipFor(AnimClip clip) => clip switch
    {
        AnimClip.Idle => _idle,
        AnimClip.Move => _move ?? _idle,
        AnimClip.Cast => _cast,
        AnimClip.Act => _act,
        AnimClip.Hit => _hit,
        AnimClip.Death => _death,
        _ => null,
    };

    // One step's events as (type, actorId) pairs for AnimRule.Resolved.
    private static IEnumerable<(string Type, int? ActorId)> Pairs(IReadOnlyList<SimEvent>? events)
    {
        if (events is null)
        {
            yield break;
        }
        foreach (var e in events)
        {
            yield return (e.Type, e.Get("actorId") is JsInt id ? (int)id.Value : null);
        }
    }

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

    private void Play(string? clip, float blend = Blend, int actorId = -1)
    {
        if (clip is null || (_player.CurrentAnimation == clip && _player.IsPlaying()))
        {
            return;
        }
        _player.Play(clip, blend);
        // KF1-SPAWN-HOLD: an idle loop starts at the actor's own phase (HoldAnim.IdlePhaseMs), so a pack bound on one
        // frame (every pack at run start under prespawn) does not breathe in lockstep while it holds.
        if (clip == _idle && actorId >= 0 && _player.GetAnimation(clip) is { } anim)
        {
            var phaseMs = HoldAnim.IdlePhaseMs(actorId, (int)(anim.Length * 1000f));
            _player.Seek(phaseMs / 1000f, true);
        }
    }

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
