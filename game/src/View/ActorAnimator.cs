using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Drives a model's own AnimationPlayer from sim state the view already reads (design/ART-PLAN.md section 6): a death
/// clip once when <c>Alive</c> goes false (its last frame held), a cast loop while <c>CastId</c> is set, a one-shot
/// when the global cooldown restarts (an instant ability) or health drops (a hit), and idle or run from the
/// interpolated speed. Read-only: it never writes sim state. A model without an AnimationPlayer gets no animator
/// (<see cref="TryCreate"/> returns null), so it keeps its static pose.
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
    private string? _oneShot;
    private bool _dead;

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
    }

    /// <summary>The clip names this animator resolved, for the ACTOR log line ("idle=Idle move=Running_A ...").</summary>
    public string Matched =>
        $"idle={_idle ?? "-"} move={_move ?? "-"} cast={_cast ?? "-"} act={_act ?? "-"} hit={_hit ?? "-"} death={_death ?? "-"}";

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
    /// <paramref name="before"/> and <paramref name="now"/> are that step's two states.
    /// </summary>
    public void Update(ActorState before, ActorState now, bool newTick, float speedMps)
    {
        if (!now.Alive)
        {
            if (!_dead)
            {
                _dead = true;
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

        var moving = speedMps > MoveSpeedMps;
        if (newTick && !moving && now.CastId is null)
        {
            if (now.GcdReadyTick > before.GcdReadyTick && _act is not null)
            {
                _oneShot = _act;
                _player.Play(_act, Blend);
                _player.Seek(0, true);
            }
            else if (now.Health < before.Health && _hit is not null && _oneShot is null)
            {
                _oneShot = _hit;
                _player.Play(_hit, Blend);
                _player.Seek(0, true);
            }
        }

        if (now.CastId is not null && _cast is not null)
        {
            _oneShot = null;
            Play(_cast);
            return;
        }
        if (_oneShot is not null)
        {
            if (!moving && _player.IsPlaying() && _player.CurrentAnimation == _oneShot)
            {
                return;
            }
            _oneShot = null;
        }
        Play(moving ? _move ?? _idle : _idle);
    }

    private void Play(string? clip)
    {
        if (clip is null || (_player.CurrentAnimation == clip && _player.IsPlaying()))
        {
            return;
        }
        _player.Play(clip, Blend);
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
