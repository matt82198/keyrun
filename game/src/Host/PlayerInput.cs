using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.View;

namespace Keyrun.Host;

/// <summary>
/// Turns the human's input into sim commands for the Warden (BIBLE s2, the ten-keybind budget). WASD moves relative
/// to the camera's heading (one <c>move</c> command per tick while a key is held, facing the walk direction); the
/// mouse turns the CameraRig while captured; Esc frees the mouse and a left click captures it again. 1-5 cast the
/// rotational slots, Q the interrupt, E the defensive, R the utility, F the movement ability (the class kit's slots,
/// <see cref="HostRunSetup.ResolveSlots"/>); Tab cycles the hostile target and G pings. Keys come from
/// <see cref="KeyBinds.ClientDefaults"/>, the port of client/keybinds.json.
/// Under <c>--autorun</c> <see cref="ScriptedWarden"/> replaces the keyboard so headless runs and proof frames are
/// reproducible.
/// </summary>
public partial class PlayerInput : Node, ICommandSource
{
    /// <summary>Mouse look sensitivity, radians per pixel.</summary>
    private const float MouseRadPerPixel = 0.0035f;

    /// <summary>
    /// The keys <see cref="Sample"/> polls with <c>Input.IsPhysicalKeyPressed</c> each tick (WASD, SampleMove) rather
    /// than reading as events. <c>--record-input</c> diffs these every tick (lane 3E-b, <see cref="InputRecorder"/>).
    /// </summary>
    public static readonly IReadOnlyList<Key> PolledKeys = new[] { Key.W, Key.S, Key.D, Key.A };

    private readonly List<KeyAction> _pending = new();
    private readonly ScriptedWarden _script = new();
    private bool _scripted;

    /// <summary>The camera rig the mouse turns and WASD is relative to.</summary>
    [Export]
    public NodePath CameraRigPath { get; set; } = new("../CameraRig");

    /// <summary>The driver (its setup holds the class kit's slots).</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The key bindings in force (client/keybinds.json defaults).</summary>
    public KeyBinds Binds { get; } = KeyBinds.FromCodes(KeyBinds.ClientDefaults);

    /// <summary>Raised when the human pings (G): the current target, or null for a spot ping.</summary>
    public event Action<int?>? Pinged;

    private CameraRig? Rig { get; set; }

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Rig = GetNodeOrNull<CameraRig>(CameraRigPath);
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        // The Warden spawns at z 4000 facing the Choir (-z); the rig's default heading looks along +z.
        Rig?.Look(Mathf.Pi, 0);
        var args = OS.GetCmdlineUserArgs();
        _scripted = Array.IndexOf(args, "--autorun") >= 0 || Array.IndexOf(args, "--write-movie") >= 0;
        if (!_scripted && DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_scripted)
        {
            return;
        }
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
            case InputEventKey { Pressed: true, Echo: false } key when Binds.ActionFor(key.PhysicalKeycode) is { } action:
                _pending.Add(action);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Rig?.Look(-motion.Relative.X * MouseRadPerPixel, -motion.Relative.Y * MouseRadPerPixel);
                break;
        }
    }

    /// <inheritdoc />
    public void Sample(int tick, Run run, List<Command> into)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_scripted)
        {
            _script.Sample(tick, run, into);
            return;
        }
        SampleMove(tick, into);
        SampleActions(tick, run, into);
    }

    private void SampleMove(int tick, List<Command> into)
    {
        float forward = 0, right = 0;
        if (Input.IsPhysicalKeyPressed(Key.W)) forward += 1;
        if (Input.IsPhysicalKeyPressed(Key.S)) forward -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) right += 1;
        if (Input.IsPhysicalKeyPressed(Key.A)) right -= 1;
        if (forward == 0 && right == 0)
        {
            return;
        }

        // Camera heading on the floor. The rig looks along its -Z: forward = (-sin yaw, -cos yaw),
        // right = (cos yaw, -sin yaw) in world (x, z). Sim mm axes are Godot's x and z unchanged.
        var yaw = Rig?.Yaw ?? Mathf.Pi;
        var wx = (-Mathf.Sin(yaw) * forward) + (Mathf.Cos(yaw) * right);
        var wz = (-Mathf.Cos(yaw) * forward) - (Mathf.Sin(yaw) * right);
        var len = Mathf.Sqrt((wx * wx) + (wz * wz));
        wx /= len;
        wz /= len;
        var dir = new Dir2(Mathf.RoundToInt(wx * 1000), Mathf.RoundToInt(wz * 1000));
        into.Add(new MoveCommand(tick, SimDriver.WardenId, dir, ToYaw1024(wx, wz)));
    }

    // player-commands.ts computePlayerCommands order: target cycles, then casts (moves were added first).
    private void SampleActions(int tick, Run run, List<Command> into)
    {
        if (_pending.Count == 0 || !run.State.Actors.TryGetValue(SimDriver.WardenId, out var me))
        {
            _pending.Clear();
            return;
        }
        var cycles = _pending.Count(a => a == KeyAction.TargetCycle);
        if (cycles > 0)
        {
            var enemies = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy).ToList();
            var target = me.TargetId;
            for (var i = 0; i < cycles; i++)
            {
                target = Targeting.CycleTarget(enemies, target);
            }
            into.Add(new TargetCommand(tick, SimDriver.WardenId, target));
        }
        foreach (var action in _pending)
        {
            if (action == KeyAction.Ping)
            {
                Pinged?.Invoke(me.TargetId);
                continue;
            }
            if (SlotAbility(action) is { } abilityId)
            {
                into.Add(new CastCommand(tick, SimDriver.WardenId, abilityId, CastTargeting.For(run, SimDriver.WardenId, abilityId)));
            }
        }
        _pending.Clear();
    }

    // The class kit's ability for an ability key; null for Tab/G or before the driver's setup exists.
    private string? SlotAbility(KeyAction action) =>
        Driver?.Setup?.Slots.FirstOrDefault(s => s.Action == action)?.AbilityId;

    /// <summary>World XZ direction to sim yaw (1/1024 turn, 0 = +x, 256 = +z; the Facing.cs convention).</summary>
    /// <param name="x">World x component.</param>
    /// <param name="z">World z component.</param>
    /// <returns>The yaw.</returns>
    public static int ToYaw1024(float x, float z)
    {
        var turns = Mathf.Atan2(z, x) / Mathf.Tau;
        var yaw = Mathf.RoundToInt(turns * 1024) % 1024;
        return yaw < 0 ? yaw + 1024 : yaw;
    }
}

/// <summary>
/// The C# port of client/keybinds.json (the keybind action to a <c>KeyboardEvent.code</c>) as Godot physical keys.
/// Codes are physical positions, as in the browser, so the bar works on any keyboard layout.
/// </summary>
public sealed class KeyBinds
{
    /// <summary>client/keybinds.json, verbatim.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> ClientDefaults = new[]
    {
        KeyValuePair.Create("ability1", "Digit1"),
        KeyValuePair.Create("ability2", "Digit2"),
        KeyValuePair.Create("ability3", "Digit3"),
        KeyValuePair.Create("ability4", "Digit4"),
        KeyValuePair.Create("ability5", "Digit5"),
        KeyValuePair.Create("abilityQ", "KeyQ"),
        KeyValuePair.Create("abilityE", "KeyE"),
        KeyValuePair.Create("abilityR", "KeyR"),
        KeyValuePair.Create("abilityF", "KeyF"),
        KeyValuePair.Create("targetCycle", "Tab"),
        KeyValuePair.Create("ping", "KeyG"),
    };

    private static readonly IReadOnlyDictionary<string, KeyAction> Actions = new Dictionary<string, KeyAction>(StringComparer.Ordinal)
    {
        ["ability1"] = KeyAction.Ability1,
        ["ability2"] = KeyAction.Ability2,
        ["ability3"] = KeyAction.Ability3,
        ["ability4"] = KeyAction.Ability4,
        ["ability5"] = KeyAction.Ability5,
        ["abilityQ"] = KeyAction.AbilityQ,
        ["abilityE"] = KeyAction.AbilityE,
        ["abilityR"] = KeyAction.AbilityR,
        ["abilityF"] = KeyAction.AbilityF,
        ["targetCycle"] = KeyAction.TargetCycle,
        ["ping"] = KeyAction.Ping,
    };

    private readonly Dictionary<Key, KeyAction> _byKey = new();
    private readonly Dictionary<KeyAction, Key> _byAction = new();

    private KeyBinds()
    {
    }

    /// <summary>
    /// Builds the bindings from keybinds.json entries. Fails closed on an unknown action or code, a duplicate key, or
    /// an action left unbound (keybinds.ts validates the same way).
    /// </summary>
    /// <param name="entries">Action name to <c>KeyboardEvent.code</c>.</param>
    /// <returns>The bindings.</returns>
    /// <exception cref="ArgumentException">Any entry is invalid.</exception>
    public static KeyBinds FromCodes(IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var binds = new KeyBinds();
        foreach (var (name, code) in entries)
        {
            if (!Actions.TryGetValue(name, out var action))
            {
                throw new ArgumentException($"keybinds: unknown action \"{name}\"");
            }
            var key = ParseCode(code);
            if (!binds._byKey.TryAdd(key, action) || !binds._byAction.TryAdd(action, key))
            {
                throw new ArgumentException($"keybinds: \"{name}\" -> \"{code}\" duplicates another binding");
            }
        }
        foreach (var action in Actions.Values)
        {
            if (!binds._byAction.ContainsKey(action))
            {
                throw new ArgumentException($"keybinds: action {action} is unbound");
            }
        }
        return binds;
    }

    /// <summary>A <c>KeyboardEvent.code</c> (Digit0-9, KeyA-Z, Tab, Space) as a Godot physical key.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">A code this port does not know.</exception>
    public static Key ParseCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && code[5] is >= '0' and <= '9')
        {
            return Key.Key0 + (code[5] - '0');
        }
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z')
        {
            return Key.A + (code[3] - 'A');
        }
        return code switch
        {
            "Tab" => Key.Tab,
            "Space" => Key.Space,
            _ => throw new ArgumentException($"keybinds: unknown key code \"{code}\""),
        };
    }

    /// <summary>The action bound to a physical key, or null.</summary>
    /// <param name="physical">The physical key.</param>
    /// <returns>The action, or null.</returns>
    public KeyAction? ActionFor(Key physical) => _byKey.TryGetValue(physical, out var a) ? a : null;

    /// <summary>The key bound to an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The key.</returns>
    public Key KeyFor(KeyAction action) => _byAction[action];

    /// <summary>The label a key shows on the bar ("1", "Q", "Tab").</summary>
    /// <param name="key">The key.</param>
    /// <returns>The label.</returns>
    public static string Label(Key key) => key switch
    {
        >= Key.Key0 and <= Key.Key9 => ((char)('0' + (key - Key.Key0))).ToString(),
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        _ => key.ToString(),
    };
}

/// <summary>
/// player-commands.ts castTargetFor (D10 first-run fix), Godot-free: a hostile single-target ability with no live enemy
/// targeted goes at the nearest live enemy (squared XZ distance, lowest id on a tie); everything else keeps the current
/// target, else the caster.
/// </summary>
public static class CastTargeting
{
    /// <summary>The target a human cast of <paramref name="abilityId"/> is sent at.</summary>
    /// <param name="run">The run.</param>
    /// <param name="humanId">The caster.</param>
    /// <param name="abilityId">The ability.</param>
    /// <returns>The target id.</returns>
    public static int For(Run run, int humanId, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(run);
        var actors = run.State.Actors;
        if (!actors.TryGetValue(humanId, out var player))
        {
            return humanId;
        }
        var current = player.TargetId;
        if (!run.Content.Abilities.TryGetValue(abilityId, out var def) || def.Shape.Kind != ShapeKind.Target ||
            Affinities.Of(def, run.Content.Effects) != Affinity.Hostile)
        {
            return current ?? humanId;
        }
        if (current is { } c && actors.TryGetValue(c, out var cur) && cur.Alive && cur.Kind != player.Kind)
        {
            return cur.Id;
        }
        int? best = null;
        long bestD2 = 0;
        foreach (var a in actors.Values)
        {
            if (!a.Alive || a.Kind == player.Kind)
            {
                continue;
            }
            var dx = a.Pos.X - player.Pos.X;
            var dz = a.Pos.Z - player.Pos.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (best is null || d2 < bestD2 || (d2 == bestD2 && a.Id < best))
            {
                best = a.Id;
                bestD2 = d2;
            }
        }
        return best ?? current ?? humanId;
    }
}

/// <summary>
/// The autorun Warden (Godot-free, deterministic): walks at the Choir Cantor (the lieutenant) until his kick is in
/// range, kicks the Cantor's heal once it is half cast (so the cast bar is on screen for two seconds first), and spends
/// the GCD on his rotation against whatever is in reach: Reckoning at full Momentum, Vigil at 50, Guard Break, Strike,
/// and Brace below half health. Every command goes through the same <see cref="CastTargeting"/> rule as a key press.
/// On a layout run (lane NAV-CORE, STATE.md D18) with no living enemy he walks the route instead
/// (<see cref="Follow.WalkRoute"/>: each room's far doorway in turn), so he crosses the next room's trigger and its
/// pull spawns at the far side; then the combat script above.
/// </summary>
public sealed class ScriptedWarden
{
    /// <summary>Stop walking once the lieutenant is this close (Shield Bash range is 3000 mm).</summary>
    public const long ApproachMm = 2400;

    /// <summary>The lieutenant's class id.</summary>
    public const string LieutenantClass = "choir.lieutenant";

    private static readonly string[] Rotation = { "warden.reckoning", "warden.vigil", "warden.guardBreak", "warden.strike" };

    private const string Kick = "warden.shieldBash";
    private const string Defensive = "warden.brace";

    /// <summary>Appends the scripted commands for <paramref name="tick"/>.</summary>
    /// <param name="tick">The pre-step tick.</param>
    /// <param name="run">The run.</param>
    /// <param name="into">The command list.</param>
    public void Sample(int tick, Run run, List<Command> into)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(into);
        var state = run.State;
        if (!state.Actors.TryGetValue(SimDriver.WardenId, out var me) || !me.Alive)
        {
            return;
        }
        if (run.Content.Layout is { } layout && !Follow.AnyLivingEnemy(state))
        {
            if (Follow.WalkRoute(tick, me, layout) is { } walk)
            {
                into.Add(walk);
            }
            return;
        }
        var abilities = run.Content.Abilities;
        var lieutenant = state.Actors.Values.FirstOrDefault(a => a.Alive && a.Kind == ActorKind.Enemy && a.ClassId == LieutenantClass);

        if (lieutenant is { CastId: { } castId, CastStartTick: { } start } && abilities.TryGetValue(castId, out var cast) && cast.Interruptible &&
            tick >= start + (cast.CastTicks / 2) && Ready(me, abilities[Kick], tick) && Casting.RangeMm(me, lieutenant) <= abilities[Kick].Range)
        {
            into.Add(new CastCommand(tick, me.Id, Kick, lieutenant.Id));
        }

        if (lieutenant is not null && Casting.RangeMm(me, lieutenant) > ApproachMm)
        {
            var dx = lieutenant.Pos.X - me.Pos.X;
            var dz = lieutenant.Pos.Z - me.Pos.Z;
            var len = Movement.Isqrt((dx * dx) + (dz * dz));
            var dir = new Dir2(IntMath.TruncDiv(dx * 1000, len), IntMath.TruncDiv(dz * 1000, len));
            into.Add(new MoveCommand(tick, me.Id, dir, Atan.YawFromComponents(dir.X, dir.Z)));
        }

        if (!Cooldowns.IsGcdReady(me, tick))
        {
            return;
        }
        if (me.Health * 2 < me.MaxHealth && Ready(me, abilities[Defensive], tick))
        {
            into.Add(new CastCommand(tick, me.Id, Defensive, me.Id));
            return;
        }
        foreach (var id in Rotation)
        {
            var def = abilities[id];
            if (!Ready(me, def, tick))
            {
                continue;
            }
            var target = CastTargeting.For(run, me.Id, id);
            if (def.Shape.Kind == ShapeKind.Self || (state.Actors.TryGetValue(target, out var t) && t.Id != me.Id && Casting.RangeMm(me, t) <= def.Range))
            {
                into.Add(new CastCommand(tick, me.Id, id, target));
                return;
            }
        }
    }

    private static bool Ready(ActorState me, AbilityDef def, int tick) =>
        Cooldowns.IsAbilityReady(me, def, tick) && (def.Cost.Resource == "none" || me.Resource >= def.Cost.Amount);
}
