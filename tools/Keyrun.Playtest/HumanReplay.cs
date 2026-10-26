using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>A headless replay of one input log (<see cref="HumanReplay"/>).</summary>
/// <param name="ClassId">The class the screen keys picked for seat 1 (the queued run's human class).</param>
/// <param name="QueueFrame">The screen frame the Enter queued on (the input-log clock offset of the run, R-CLOCK-1).</param>
/// <param name="Setup">The run's setup.</param>
/// <param name="Run">The run after the last step.</param>
/// <param name="Events">Every sim event of the run, in step order.</param>
/// <param name="Pings">The human's pings the bots accepted (each one's run tick).</param>
public sealed record HumanReplayResult(string ClassId, int QueueFrame, HostRunSetup Setup, Run Run, IReadOnlyList<SimEvent> Events, IReadOnlyList<int> Pings);

/// <summary>
/// HUMAN-PLAY (D65/D66): plays a <c>keyrun.input-log/1</c> or <c>/2</c> file headlessly, the way the interactive host
/// plays it under <c>--replay-input</c>, so a per-class input log is proven without launching the game. Before the run
/// the keys drive the pre-run screen's Character tab (Up/Down cycle the class, <see cref="Lobby.ClassIds"/> order) and
/// Enter queues it: <see cref="Lobby.Queue"/>, <see cref="Lobby.ClassFor"/>, <see cref="HostRunSetup.Build"/> with the bot
/// seats and <see cref="HostRunFactory.Create"/>, as SimDriver.StartRun does; a log without recorded ready answers is
/// answered ready at the pop (<see cref="QueuedRunGate.HumanReadyAtPop"/>), so the run starts on the Enter's frame S and a
/// key at clock S + t acts on run tick t (<see cref="ReplayClock"/>). In the run, each tick: seat 1's commands as
/// PlayerInput.Sample builds them (WASD relative to the camera heading, the log's startHeading and camera entries; Tab,
/// the five buttons, Q/E/R/F through <see cref="CastTargeting"/>; G a ping to the bots, routed as SimDriver.RoutePing),
/// then the bot seats' (<see cref="BotCommandSource"/>), stepped through <see cref="Backfill.Step"/>. The float heading
/// math follows PlayerInput's (Godot's float Mathf is MathF), so moves match the host's.
/// Fails closed on a key the screen or the run does not read.
/// </summary>
public static class HumanReplay
{
    private static readonly IReadOnlyDictionary<string, KeyAction> RunKeys = new Dictionary<string, KeyAction>(StringComparer.Ordinal)
    {
        ["Key1"] = KeyAction.Ability1,
        ["Key2"] = KeyAction.Ability2,
        ["Key3"] = KeyAction.Ability3,
        ["Key4"] = KeyAction.Ability4,
        ["Key5"] = KeyAction.Ability5,
        ["Q"] = KeyAction.AbilityQ,
        ["E"] = KeyAction.AbilityE,
        ["R"] = KeyAction.AbilityR,
        ["F"] = KeyAction.AbilityF,
        ["Tab"] = KeyAction.TargetCycle,
        ["G"] = KeyAction.Ping,
    };

    private static readonly string[] Polled = { "W", "S", "D", "A" };

    /// <summary>Loads and plays <paramref name="path"/>.</summary>
    /// <param name="path">The input log.</param>
    /// <param name="ticks">Run ticks to play.</param>
    /// <param name="registry">The content (default: the embedded content).</param>
    /// <param name="observe">Test seam: called after each step with the run, or null.</param>
    /// <returns>The result.</returns>
    public static HumanReplayResult Play(string path, int ticks, C.ContentRegistry? registry = null, Action<Run>? observe = null) =>
        Play(InputLogDocument.Parse(File.ReadAllText(path), path), ticks, registry, observe);

    /// <summary>Plays <paramref name="log"/> for <paramref name="ticks"/> run ticks.</summary>
    /// <param name="log">The input log.</param>
    /// <param name="ticks">Run ticks to play.</param>
    /// <param name="registry">The content (default: the embedded content).</param>
    /// <param name="observe">Test seam: called after each step with the run, or null.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">The log never queues, records ready answers, or holds a key this replay does not read.</exception>
    public static HumanReplayResult Play(InputLogDocument log, int ticks, C.ContentRegistry? registry = null, Action<Run>? observe = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        var reg = registry ?? C.ContentLoader.Load();
        if (log.ReadyKeys)
        {
            throw new ArgumentException("a log with recorded ready answers (readyKeys) is not replayed headlessly");
        }
        var rotation = log.RotationIndex ?? 0;
        var progression = Progression.Start(log.Seed);
        var lobby = Lobby.Open(reg, progression, SimDriver_DungeonId, reg.Clauses.Keys.ToList(), log.KeyLevel, rotation, reg.Run.Affixes.ClauseStartKey);
        var events = log.Events.ToList();
        var next = 0;
        QueueRequest? request = null;
        var queueFrame = -1;
        for (; next < events.Count && request is null; next++)
        {
            var e = events[next];
            if (!e.Pressed)
            {
                continue;
            }
            switch (e.Key)
            {
                case "Down":
                    lobby = Cycle(reg, progression, lobby, 1);
                    break;
                case "Up":
                    lobby = Cycle(reg, progression, lobby, -1);
                    break;
                case "Enter":
                    var (locked, req) = lobby.Queue(reg, progression);
                    lobby = locked;
                    request = req;
                    queueFrame = e.Tick;
                    break;
                default:
                    throw new ArgumentException($"screen key \"{e.Key}\" at frame {e.Tick} is not read by this replay (Up, Down, Enter)");
            }
        }
        if (request is null)
        {
            throw new ArgumentException("the log never queues (no Enter on the screen)");
        }
        var classId = Lobby.ClassFor(request, lobby);
        var setup = HostRunSetup.Build(reg, request.DungeonId, request.KeyLevel, classId, withBots: true, rotationIndex: rotation);
        var run = HostRunFactory.Create(setup, new RunRequest(log.Seed, reg, classId) { Queued = request, Progression = progression }).Run;
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(reg), log.Seed, BotCommandSource.DefaultSkillDial);
        var held = new HashSet<string>(StringComparer.Ordinal);
        var pending = new List<KeyAction>();
        var yaw = log.StartHeading ?? MathF.PI;
        var camera = log.Camera.ToList();
        var nextCam = 0;
        var backfill = BackfillState.Empty;
        var all = new List<SimEvent>();
        var pings = new List<int>();
        var commands = new List<Command>();
        for (var t = 0; t < ticks && run.RunEnd.Result is null; t++)
        {
            var clock = queueFrame + t;
            for (; next < events.Count && events[next].Tick <= clock; next++)
            {
                var e = events[next];
                if (Polled.Contains(e.Key))
                {
                    if (e.Pressed) held.Add(e.Key); else held.Remove(e.Key);
                }
                else if (RunKeys.TryGetValue(e.Key, out var action))
                {
                    if (e.Pressed) pending.Add(action);
                }
                else if (e.Key != "Enter")
                {
                    throw new ArgumentException($"run key \"{e.Key}\" at clock {e.Tick} is not read by this replay");
                }
            }
            for (; nextCam < camera.Count && camera[nextCam].Tick <= clock; nextCam++)
            {
                yaw = camera[nextCam].Yaw;
            }
            var preTick = run.State.Tick;
            commands.Clear();
            Move(preTick, run, held, yaw, commands);
            Actions(preTick, run, setup, pending, commands, target =>
            {
                var me = run.State.Actors[HostRunSetup.HumanId];
                var ping = target is { } id
                    ? new PingEvent(preTick, HostRunSetup.HumanId, PingKind.Target) { TargetId = id }
                    : new PingEvent(preTick, HostRunSetup.HumanId, PingKind.Spot) { Point = new PointMm(me.Pos.X, me.Pos.Z) };
                if (bots.Ping(ping))
                {
                    pings.Add(preTick);
                }
            });
            pending.Clear();
            bots.Sample(preTick, run, commands);
            _ = bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            observe?.Invoke(run);
            all.AddRange(step.Step.Events);
        }
        return new HumanReplayResult(classId, queueFrame, setup, run, all, pings);
    }

    // The host's dungeon (SimDriver.DungeonId; SimDriver is Godot, so its constant is restated here).
    private const string SimDriver_DungeonId = "hollow-vault";

    // Screen.CycleClass: the next class in Lobby.ClassIds order, picked through Lobby.PickClass.
    private static Lobby Cycle(C.ContentRegistry reg, Progression progression, Lobby lobby, int delta)
    {
        var ids = Lobby.ClassIds(reg);
        var i = Math.Max(0, ids.ToList().IndexOf(lobby.ClassId));
        return lobby.PickClass(reg, progression, ids[(((i + delta) % ids.Count) + ids.Count) % ids.Count]);
    }

    // PlayerInput.SampleMove without mouselook (a replay has none).
    private static void Move(int tick, Run run, HashSet<string> held, float yaw, List<Command> into)
    {
        float forward = 0, right = 0;
        if (held.Contains("W")) forward += 1;
        if (held.Contains("S")) forward -= 1;
        if (held.Contains("D")) right += 1;
        if (held.Contains("A")) right -= 1;
        var moving = forward != 0 || right != 0;
        var dir = new Dir2(0, 0);
        var walkYaw = 0;
        if (moving)
        {
            var wx = (-Sin(yaw) * forward) + (Cos(yaw) * right);
            var wz = (-Cos(yaw) * forward) - (Sin(yaw) * right);
            var len = MathF.Sqrt((wx * wx) + (wz * wz));
            wx /= len;
            wz /= len;
            dir = new Dir2(RoundToInt(wx * 1000), RoundToInt(wz * 1000));
            walkYaw = ToYaw1024(wx, wz);
        }
        var actorYaw = run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var me) ? me.Yaw : 0;
        var face = C.MouseoverRule.FacingYaw(false, moving, walkYaw, ToYaw1024(-Sin(yaw), -Cos(yaw)), actorYaw);
        // A null facing means "facing unchanged": the move still carries the walk direction, with the actor's own yaw.
        if (moving || face is not null)
        {
            into.Add(new MoveCommand(tick, HostRunSetup.HumanId, dir, face ?? actorYaw));
        }
    }

    // PlayerInput.SampleActions without mouseover (a replay has none).
    private static void Actions(int tick, Run run, HostRunSetup setup, List<KeyAction> pending, List<Command> into, Action<int?> ping)
    {
        if (pending.Count == 0 || !run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var me))
        {
            return;
        }
        var cycles = pending.Count(a => a == KeyAction.TargetCycle);
        if (cycles > 0)
        {
            var enemies = TabTargeting.Candidates(run.State, me);
            var target = me.TargetId;
            for (var i = 0; i < cycles; i++)
            {
                target = Targeting.CycleTarget(enemies, target);
            }
            into.Add(new TargetCommand(tick, HostRunSetup.HumanId, target));
        }
        foreach (var action in pending)
        {
            if (action == KeyAction.Ping)
            {
                ping(me.TargetId);
                continue;
            }
            if (setup.Slots.FirstOrDefault(s => s.Action == action)?.AbilityId is { } abilityId)
            {
                var target = C.MouseoverRule.CastTarget(action == KeyAction.AbilityQ, null, CastTargeting.For(run, HostRunSetup.HumanId, abilityId));
                into.Add(new CastCommand(tick, HostRunSetup.HumanId, abilityId, target));
            }
        }
    }

    // Godot 4's float Mathf overloads are MathF calls.
    private static float Sin(float x) => MathF.Sin(x);

    private static float Cos(float x) => MathF.Cos(x);

    private static int RoundToInt(float x) => (int)MathF.Round(x);

    // PlayerInput.ToYaw1024.
    private static int ToYaw1024(float x, float z)
    {
        var turns = MathF.Atan2(z, x) / (MathF.PI * 2f);
        var yaw = RoundToInt(turns * 1024) % 1024;
        return yaw < 0 ? yaw + 1024 : yaw;
    }
}
