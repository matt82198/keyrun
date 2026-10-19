using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// One floating nameplate per sim actor other than the Warden: the content display name ("Choir Cantor"), a health
/// readout, and a marker on the Warden's current target. It also gives every actor the run spawns a body: an Actor.tscn instance (ActorView)
/// under <see cref="ActorsPath"/>, coloured by role, unless the scene already holds a view for that id (the Warden's,
/// which CameraRig follows). Plates and bodies of actors that leave the state (a fresh run) are freed.
/// <para>
/// HUD polish (design/ART-PLAN.md section 8 lane 8): plates are drawn in screen space on a 2D layer under the HUD, not
/// as Label3D billboards. Each frame every plate's anchor (<see cref="PlateHeight"/> above its body) is projected with
/// <c>Camera3D.UnprojectPosition</c>, the plates are sorted nearest the camera first, and
/// <see cref="HudLayout"/>.Separate stacks them so no two plates (five Pair Warriors, a Twin beside the boss) and no
/// plate and the top bar or party frames share a pixel, and every plate stays inside the viewport. The health line
/// stays under its title as one plate; the font size is fixed, so the boss and Twin plates read the same from the door
/// as up close.
/// </para>
/// </summary>
public partial class LabelView : Node3D
{
    private const float PlateHeight = 2.35f;
    private const int NameFont = 15;
    private const int HealthFont = 12;
    private const int PlateLineGap = 2;
    private const int PlatePad = 4;
    private const int OutlineSize = 4;

    // PartyFrames.cs draws one 68 px frame per seat with an 8 px gap (FrameHeight, Gap).
    private const int PartyFrameStride = 76;
    private const int PartyFrameGap = 8;

    // CastBarView.cs: a 150 x 16 bar at 3.0 m over the head, its 14 px label drawn 4 px above the bar.
    private const float CastBarHeightM = 3.0f;
    private const int CastBarSpan = 170;
    private const int CastBarHeight = 16;
    private const int CastBarLabel = 18;
    private const float CastFadeSeconds = 0.9f;

    // Pings.cs: SPREAD/STACK world labels in a 16 px font, 2.6 m over a marked actor or 0.3 m over a telegraph origin.
    private const float PingMarkHeightM = 2.6f;
    private const float PingFloorHeightM = 0.3f;
    private const int PingFont = 16;
    private const int CalloutTop = 100;
    private const int CalloutStride = 36;
    private const int CalloutWidth = 400;

    private static readonly Color MeleeColor = new(0.78f, 0.22f, 0.2f);
    private static readonly Color LieutenantColor = new(0.62f, 0.3f, 0.85f);
    private static readonly Color OtherEnemyColor = new(0.85f, 0.45f, 0.15f);
    private static readonly Color PlayerColor = new(0.2f, 0.4f, 0.9f);
    private static readonly Color TargetColor = new(1f, 0.92f, 0.35f);
    private static readonly Color Outline = new(0f, 0f, 0f, 0.85f);

    private readonly Dictionary<int, Plate> _plates = new();
    private readonly Dictionary<int, Node3D> _spawned = new();
    private readonly List<int> _gone = new();
    private readonly List<Shown> _shown = new();
    private readonly List<CastEnd> _castEnds = new();

    private Control? _canvas;

    /// <summary>The driver to read state from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The node actor bodies live under.</summary>
    [Export]
    public NodePath ActorsPath { get; set; } = new("../Actors");

    /// <summary>The per-actor body scene (Actor.tscn).</summary>
    [Export]
    public PackedScene? ActorScene { get; set; }

    /// <summary>The party frames, a HUD obstacle plates keep clear of (absent: no party-frame obstacle).</summary>
    [Export]
    public NodePath PartyFramesPath { get; set; } = new("../Hud/HudRoot/PartyFrames");

    private SimDriver? Driver { get; set; }

    private Node3D? Actors { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        Actors = GetNodeOrNull<Node3D>(ActorsPath);
        // Layer 0 draws over the 3D view and under the HUD CanvasLayer (layer 1).
        var layer = new CanvasLayer { Layer = 0, Name = "Plates" };
        AddChild(layer);
        _canvas = new Control { Name = "PlateCanvas", MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.Draw += DrawPlates;
        layer.AddChild(_canvas);
        if (Driver is not null)
        {
            Driver.Stepped += OnStepped;
            Driver.RunStarted += _castEnds.Clear;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Driver is not null)
        {
            Driver.Stepped -= OnStepped;
            Driver.RunStarted -= _castEnds.Clear;
        }
    }

    // The casts CastBarView keeps drawing while they fade (an interrupt on its target, a completed cast on its caster).
    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Type == "interrupt" && e.Get("targetId") is JsInt target)
            {
                _castEnds.Add(new CastEnd((int)target.Value));
            }
            else if (e.Type == "cast_complete" && e.Get("actorId") is JsInt actor)
            {
                _castEnds.Add(new CastEnd((int)actor.Value));
            }
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Driver is null)
        {
            return;
        }
        var state = Driver.State;
        var targetId = state.Actors.TryGetValue(SimDriver.WardenId, out var me) ? me.TargetId : null;
        foreach (var (id, actor) in state.Actors)
        {
            EnsureBody(id, actor);
            if (!_plates.TryGetValue(id, out var plate))
            {
                plate = new Plate(Driver.DisplayName(actor));
                _plates[id] = plate;
            }
            // The Warden's own name and health live on the ability bar's meters, not over his head.
            plate.Visible = actor.Alive && id != SimDriver.WardenId;
            if (!plate.Visible)
            {
                continue;
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var t = Driver.Alpha;
            plate.Anchor = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), t) + new Vector3(0, PlateHeight, 0);
            plate.Health = $"{actor.Health} / {actor.MaxHealth}";
            plate.HealthTint = HealthColor(actor.Health, actor.MaxHealth);
            plate.Targeted = id == targetId;
        }

        _gone.Clear();
        foreach (var id in _plates.Keys)
        {
            if (!state.Actors.ContainsKey(id))
            {
                _gone.Add(id);
            }
        }
        foreach (var id in _gone)
        {
            _plates.Remove(id);
            if (_spawned.Remove(id, out var body))
            {
                body.QueueFree();
            }
        }
        for (var i = _castEnds.Count - 1; i >= 0; i--)
        {
            _castEnds[i].Age += (float)delta;
            if (_castEnds[i].Age >= CastFadeSeconds)
            {
                _castEnds.RemoveAt(i);
            }
        }
        _canvas?.QueueRedraw();
    }

    private void DrawPlates()
    {
        var camera = GetViewport()?.GetCamera3D();
        if (_canvas is null || camera is null)
        {
            return;
        }
        var view = GetViewport().GetVisibleRect().Size;
        var width = (int)view.X;
        var height = (int)view.Y;

        // Project every visible plate whose anchor is in front of the camera and on screen; nearest first.
        _shown.Clear();
        var origin = camera.GlobalPosition;
        foreach (var plate in _plates.Values)
        {
            if (!plate.Visible || camera.IsPositionBehind(plate.Anchor))
            {
                continue;
            }
            var screen = camera.UnprojectPosition(plate.Anchor);
            if (screen.X < 0 || screen.X > width || screen.Y < 0 || screen.Y > height)
            {
                continue;
            }
            var name = plate.Targeted ? "> " + plate.BaseName + " <" : plate.BaseName;
            var w = (int)Math.Ceiling(Math.Max(HudNodes.Width(name, NameFont), HudNodes.Width(plate.Health, HealthFont))) + (2 * PlatePad);
            var h = NameFont + PlateLineGap + HealthFont + (2 * PlatePad);
            var rect = new PixelRect((int)Math.Round(screen.X) - (w / 2), (int)Math.Round(screen.Y) - h, w, h);
            _shown.Add(new Shown(plate, name, rect, origin.DistanceSquaredTo(plate.Anchor)));
        }
        if (_shown.Count == 0)
        {
            return;
        }
        _shown.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        var offsets = HudLayout.Separate(_shown.Select(s => s.Rect).ToList(), width, height, Obstacles(camera, width, height));
        var font = ThemeDB.FallbackFont;
        for (var i = 0; i < _shown.Count; i++)
        {
            var s = _shown[i];
            var r = s.Rect.Offset(offsets[i].Dx, offsets[i].Dy);
            var centre = r.X + (r.Width / 2f);
            var nameColor = s.Plate.Targeted ? TargetColor : Colors.White;
            Line(font, new Vector2(centre, r.Y + PlatePad + NameFont), s.Name, NameFont, nameColor);
            Line(font, new Vector2(centre, r.Y + PlatePad + NameFont + PlateLineGap + HealthFont), s.Plate.Health, HealthFont, s.Plate.HealthTint);
        }
    }

    private void Line(Font font, Vector2 baselineCentre, string text, int size, Color color)
    {
        var at = baselineCentre - new Vector2(HudNodes.Width(text, size) / 2f, 0);
        _canvas!.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, OutlineSize, Outline);
        _canvas.DrawString(font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>
    /// The HUD rectangles plates keep out of: the top bar band (help line, objective, Slack), the party frames, every
    /// live cast bar over a head (CastBarView) and every SPREAD/STACK world label (Pings), so a plate never draws
    /// under another HUD text.
    /// </summary>
    private List<PixelRect> Obstacles(Camera3D camera, int width, int height)
    {
        var obstacles = new List<PixelRect> { HudLayout.TopBar(width, height).Band(width) };
        if (Driver is null)
        {
            return obstacles;
        }
        var state = Driver.State;
        if (GetNodeOrNull<Control>(PartyFramesPath) is { Visible: true } frames)
        {
            var seats = state.Actors.Values.Count(a => a.Kind == ActorKind.Player);
            if (seats > 0)
            {
                var at = frames.GetGlobalRect().Position;
                obstacles.Add(new PixelRect((int)at.X, (int)at.Y, (int)frames.Size.X, (seats * PartyFrameStride) - PartyFrameGap));
            }
        }
        foreach (var (id, actor) in state.Actors)
        {
            // An active cast, or a finished or interrupted one still fading out (CastBarView keeps those 0.9 s).
            var fading = _castEnds.Exists(e => e.ActorId == id);
            if (!fading && (!actor.Alive || actor.CastId is null))
            {
                continue;
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var head = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha) + new Vector3(0, CastBarHeightM, 0);
            if (!camera.IsPositionBehind(head))
            {
                var p = camera.UnprojectPosition(head);
                // "Interrupted: <name>" can run wider than the bar; reserve the wider label width for every bar.
                obstacles.Add(new PixelRect((int)p.X - (CastBarSpan / 2), (int)p.Y - CastBarLabel, CastBarSpan, CastBarLabel + CastBarHeight));
            }
        }
        if (Driver.Run is { } run)
        {
            var calls = PingCalls.Calls(run);
            var callouts = calls.Select(c => c.Label).Distinct().Count();
            if (callouts > 0)
            {
                // Pings.cs callouts: one centred 22 px line per label from y 100, 36 px apart (about 400 px wide at most).
                obstacles.Add(new PixelRect((width / 2) - (CalloutWidth / 2), CalloutTop, CalloutWidth, callouts * CalloutStride));
            }
            foreach (var call in calls)
            {
                var world = ActorView.ToMetres(call.Pos) + new Vector3(0, call.Source == PingSource.Mark ? PingMarkHeightM : PingFloorHeightM, 0);
                if (!camera.IsPositionBehind(world))
                {
                    var p = camera.UnprojectPosition(world);
                    var w = (int)Math.Ceiling(HudNodes.Width(call.Text, PingFont));
                    obstacles.Add(new PixelRect((int)p.X - (w / 2), (int)p.Y - PingFont, w, PingFont + 5));
                }
            }
        }
        return obstacles;
    }

    private void EnsureBody(int id, ActorState actor)
    {
        if (_spawned.ContainsKey(id) || Actors is null || ActorScene is null || HasStaticView(id))
        {
            return;
        }
        var view = ActorScene.Instantiate<ActorView>();
        view.ActorId = id;
        view.BodyColor = BodyColor(actor);
        view.Name = $"Actor{id}";
        Actors.AddChild(view);
        _spawned[id] = view;
    }

    private bool HasStaticView(int id)
    {
        foreach (var child in Actors!.GetChildren())
        {
            if (child is ActorView v && v.ActorId == id && !_spawned.ContainsValue(v))
            {
                return true;
            }
        }
        return false;
    }

    private static Color BodyColor(ActorState actor) => actor.Kind switch
    {
        ActorKind.Player => PlayerColor,
        _ when actor.ClassId.EndsWith(".lieutenant", StringComparison.Ordinal) => LieutenantColor,
        _ when actor.ClassId.EndsWith(".melee", StringComparison.Ordinal) => MeleeColor,
        _ => OtherEnemyColor,
    };

    private static Color HealthColor(long hp, long max)
    {
        var f = max <= 0 ? 0f : Mathf.Clamp((float)hp / max, 0f, 1f);
        return new Color(1f - (0.7f * f), 0.35f + (0.6f * f), 0.3f);
    }

    private sealed class Plate(string baseName)
    {
        public string BaseName { get; } = baseName;

        public bool Visible { get; set; }

        public Vector3 Anchor { get; set; }

        public string Health { get; set; } = string.Empty;

        public Color HealthTint { get; set; } = Colors.White;

        public bool Targeted { get; set; }
    }

    private sealed class CastEnd(int actorId)
    {
        public int ActorId { get; } = actorId;

        public float Age { get; set; }
    }

    private readonly record struct Shown(Plate Plate, string Name, PixelRect Rect, float Depth);
}
