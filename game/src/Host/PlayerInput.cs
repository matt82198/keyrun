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
/// to the camera's heading (one <c>move</c> command per tick while a key is held, facing the walk direction). 1-5 cast the
/// rotational slots, Q the interrupt, E the defensive, R the utility, F the movement ability (the class kit's slots,
/// <see cref="HostRunSetup.ResolveSlots"/>); Tab cycles the hostile target and G pings. Keys come from
/// <see cref="KeyBinds.ClientDefaults"/>, the port of client/keybinds.json.
/// <para>
/// MOUSEOVER (Matt 2026-10-20, "like wow you should be able to hover and interrupt"), the WoW input model through
/// <see cref="Keyrun.Content.MouseoverRule"/>: the cursor is free by default; holding the right mouse button is mouselook
/// (the cursor is captured, the mouse turns the CameraRig and the Warden faces its heading), and releasing it frees the
/// cursor again. <see cref="Mouseover"/> is the live enemy whose body is under the cursor (none during mouselook), and Q
/// sends the class kick at it when there is one, else at the target <see cref="CastTargeting"/> picks; the kick is an
/// ordinary cast command, so the sim's intended-tick interrupt path resolves it unchanged. Under
/// <c>--replay-input</c> there is no mouseover and no mouselook facing (the input log records keys and the camera
/// heading only), so a replay sends what a keyboard-only recording sent.
/// </para>
/// <para>
/// STACKED-PLATES (ledger f-e933a2b7, "hover to interrupt like WoW"): a name plate is a hover target too. The live
/// enemy whose drawn plate block (<c>LabelView.PlateRects</c>, the name and its cast bar) is under the cursor is the
/// mouseover unit, ahead of any body (<see cref="Keyrun.Content.MouseoverRule"/>.Unit with plates), so Q kicks the
/// enemy whose plate you point at.
/// </para>
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

    /// <summary>The name plates (LabelView), whose drawn blocks are hover targets (absent: bodies only).</summary>
    [Export]
    public NodePath LabelsPath { get; set; } = new("../Labels");

    /// <summary>The key bindings in force (client/keybinds.json defaults).</summary>
    public KeyBinds Binds { get; } = KeyBinds.FromCodes(KeyBinds.ClientDefaults);

    /// <summary>Raised when the human pings (G): the current target, or null for a spot ping.</summary>
    public event Action<int?>? Pinged;

    /// <summary>
    /// The mouseover unit this frame (<see cref="Keyrun.Content.MouseoverRule.Unit"/>): the live enemy whose body is under
    /// the cursor, or null (none hovered, mouselook, autorun or replay).
    /// </summary>
    public int? Mouseover { get; private set; }

    private readonly List<Keyrun.Content.HoverBody> _bodies = new();
    private readonly List<Keyrun.Content.HoverPlate> _hoverPlates = new();

    // True while the right mouse button is held (mouselook), from its press and release events.
    private bool _rightHeld;

    // The unit a left click picked this frame (the mouseover at the click), sent as a target command on the next sample.
    private int? _clickTarget;

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
        // MOUSEOVER: the cursor starts free (WoW); only a held right button captures it.
        if (!_scripted && DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_scripted)
        {
            return;
        }
        // A release the window never delivered (focus lost mid-drag) still ends mouselook.
        if (_rightHeld && !Input.IsMouseButtonPressed(MouseButton.Right))
        {
            SetMouselook(false);
        }
        Mouseover = Replaying ? null : PickMouseover();
    }

    private bool Replaying => Driver?.Options.ReplayInput is not null;

    private void SetMouselook(bool held)
    {
        _rightHeld = held;
        if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Keyrun.Content.MouseoverRule.CursorCaptured(held) ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }
    }

    // This frame's hover pick: every actor's body box (feet to standing-height head, projected) under the cursor.
    private int? PickMouseover()
    {
        var camera = GetViewport()?.GetCamera3D();
        if (Driver is null || camera is null)
        {
            return null;
        }
        _bodies.Clear();
        var state = Driver.State;
        var human = state.Actors.TryGetValue(SimDriver.WardenId, out var me) ? me : null;
        foreach (var (id, actor) in state.Actors)
        {
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var feet = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha);
            var head = feet + new Vector3(0, ActorView.Binding.For(actor.ClassId).StandingHeightMm * 0.001f, 0);
            if (camera.IsPositionBehind(feet) || camera.IsPositionBehind(head))
            {
                continue;
            }
            var f = camera.UnprojectPosition(feet);
            var h = camera.UnprojectPosition(head);
            var rect = Keyrun.Content.MouseoverRule.BodyRect(
                new Keyrun.Content.PixelPoint((int)Math.Round(f.X), (int)Math.Round(f.Y)),
                new Keyrun.Content.PixelPoint((int)Math.Round(h.X), (int)Math.Round(h.Y)));
            var depth = (long)(camera.GlobalPosition.DistanceTo(feet) * 1000);
            var hostile = human is not null && actor.Kind != human.Kind;
            _bodies.Add(new Keyrun.Content.HoverBody(id, rect, depth, hostile, actor.Alive));
        }
        // STACKED-PLATES: every drawn plate block, in draw order, stands for its leader actor (last frame's layout:
        // LabelView lays out after this node in the Arena tree, one frame behind at most).
        _hoverPlates.Clear();
        if (GetNodeOrNull<LabelView>(LabelsPath) is { } labels)
        {
            foreach (var plate in labels.PlateRects)
            {
                if (plate.ActorIds.Count == 0 || !state.Actors.TryGetValue(plate.ActorIds[0], out var lead))
                {
                    continue;
                }
                var hostile = human is not null && lead.Kind != human.Kind;
                _hoverPlates.Add(new Keyrun.Content.HoverPlate(plate.ActorIds[0], plate.Rect, hostile, lead.Alive));
            }
        }
        var cursor = GetViewport().GetMousePosition();
        return Keyrun.Content.MouseoverRule.Unit(
            new Keyrun.Content.PixelPoint((int)Math.Round(cursor.X), (int)Math.Round(cursor.Y)), _rightHeld, _bodies, _hoverPlates);
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
            case InputEventKey { Pressed: true, Echo: false } key when Binds.ActionFor(key.PhysicalKeycode) is { } action:
                _pending.Add(action);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when Mouseover is { } clicked:
                // HUMAN-PLAY (WoW): left click on a unit (its body or its plate) makes it the target.
                _clickTarget = clicked;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } button:
                // MOUSEOVER: hold right mouse to look, release to free the cursor.
                SetMouselook(button.Pressed);
                break;
            case InputEventMouseMotion motion when _rightHeld:
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
            // f-3dcc702c: the autorun Warden counts the Enemy Forces bar to 100% before the boss (forces.json via the HUD's table).
            _script.Forces ??= ForcesPlan.Of(Driver?.Registry);
            _script.Sample(tick, run, into, Driver?.OpenSeats);
            return;
        }
        SampleMove(tick, run, into);
        SampleActions(tick, run, into);
    }

    private void SampleMove(int tick, Run run, List<Command> into)
    {
        float forward = 0, right = 0;
        if (Input.IsPhysicalKeyPressed(Key.W)) forward += 1;
        if (Input.IsPhysicalKeyPressed(Key.S)) forward -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) right += 1;
        if (Input.IsPhysicalKeyPressed(Key.A)) right -= 1;
        var moving = forward != 0 || right != 0;

        // Camera heading on the floor. The rig looks along its -Z: forward = (-sin yaw, -cos yaw),
        // right = (cos yaw, -sin yaw) in world (x, z). Sim mm axes are Godot's x and z unchanged.
        var yaw = Rig?.Yaw ?? Mathf.Pi;
        var dir = new Dir2(0, 0);
        var walkYaw = 0;
        if (moving)
        {
            var wx = (-Mathf.Sin(yaw) * forward) + (Mathf.Cos(yaw) * right);
            var wz = (-Mathf.Cos(yaw) * forward) - (Mathf.Sin(yaw) * right);
            var len = Mathf.Sqrt((wx * wx) + (wz * wz));
            wx /= len;
            wz /= len;
            dir = new Dir2(Mathf.RoundToInt(wx * 1000), Mathf.RoundToInt(wz * 1000));
            walkYaw = ToYaw1024(wx, wz);
        }
        // MOUSEOVER mouselook: while right mouse is held the Warden faces the camera heading (a zero-length move turns
        // him in place); otherwise he faces the walk direction. No mouselook facing under replay (not in the log).
        var actorYaw = run.State.Actors.TryGetValue(SimDriver.WardenId, out var me) ? me.Yaw : 0;
        var face = Keyrun.Content.MouseoverRule.FacingYaw(_rightHeld && !Replaying, moving, walkYaw,
            ToYaw1024(-Mathf.Sin(yaw), -Mathf.Cos(yaw)), actorYaw);
        if (face is { } f)
        {
            into.Add(new MoveCommand(tick, SimDriver.WardenId, dir, f));
        }
    }

    // player-commands.ts computePlayerCommands order: target cycles, then casts (moves were added first).
    private void SampleActions(int tick, Run run, List<Command> into)
    {
        if (_clickTarget is { } clicked)
        {
            _clickTarget = null;
            if (run.State.Actors.TryGetValue(SimDriver.WardenId, out var self) && run.State.Actors.TryGetValue(clicked, out var unit) && unit.Alive)
            {
                into.Add(new TargetCommand(tick, self.Id, clicked));
            }
        }
        if (_pending.Count == 0 || !run.State.Actors.TryGetValue(SimDriver.WardenId, out var me))
        {
            _pending.Clear();
            return;
        }
        var cycles = _pending.Count(a => a == KeyAction.TargetCycle);
        if (cycles > 0)
        {
            var enemies = TabTargeting.Candidates(run.State, me);
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
                // MOUSEOVER: the kick (Q) goes at the hovered live enemy when there is one, else where it always went.
                var hovered = Mouseover is { } m && run.State.Actors.TryGetValue(m, out var mo) && mo.Alive && mo.Kind != me.Kind ? m : (int?)null;
                var target = Keyrun.Content.MouseoverRule.CastTarget(action == KeyAction.AbilityQ, hovered,
                    CastTargeting.For(run, SimDriver.WardenId, abilityId));
                into.Add(new CastCommand(tick, SimDriver.WardenId, abilityId, target));
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
