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
/// One floating nameplate per sim actor other than the Warden: the content display name ("Choir Cantor") only
/// (HUD-DECLUTTER: no health number over a head; enemy health is in UnitFrames), and a marker on the Warden's current target. It also gives every actor the run spawns a body: an Actor.tscn instance (ActorView)
/// under <see cref="ActorsPath"/>, coloured by role, unless the scene already holds a view for that id (the Warden's,
/// which CameraRig follows). Plates and bodies of actors that leave the state (a fresh run) are freed.
/// <para>
/// HUD polish (design/ART-PLAN.md section 8 lane 8): plates are drawn in screen space on a 2D layer under the HUD, not
/// as Label3D billboards. Each frame every plate's anchor (<see cref="PlateHeight"/> above its body) is projected with
/// <c>Camera3D.UnprojectPosition</c>, the plates are sorted nearest the camera first, and
/// <see cref="HudLayout"/>.Separate stacks them so no two plates (five Pair Warriors, a Twin beside the boss) and no
/// plate and the top bar or party frames share a pixel, and every plate stays inside the viewport.
/// The font size is fixed, so the boss and Twin plates read the same from the door
/// as up close.
/// </para>
/// </summary>
public partial class LabelView : Node3D
{
    private const int NameFont = 15;
    private const int PlatePad = 4;
    private const int OutlineSize = 4;

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

    /// <summary>The enemy unit frames (HUD-DECLUTTER), a HUD obstacle plates keep clear of.</summary>
    [Export]
    public NodePath UnitFramesPath { get; set; } = new("../Hud/HudRoot/UnitFrames");

    /// <summary>The over-head cast bars (CastBarView), whose placed blocks plates keep clear of.</summary>
    [Export]
    public NodePath CastBarsPath { get; set; } = new("../Hud/CastBars");

    /// <summary>The SPREAD/STACK labels and callouts (Pings), whose placed boxes plates keep clear of.</summary>
    [Export]
    public NodePath PingsPath { get; set; } = new("../Hud/HudRoot/Pings");

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
            if (!plate.Logged)
            {
                // HUD-DECLUTTER proof: the lines this plate draws ("name" only; health lives in the unit frames).
                plate.Logged = true;
                HudNodes.Proof(Driver, $"HUD plate actor={id} lines={string.Join(",", Plate.Lines)}");
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var t = Driver.Alpha;
            plate.Anchor = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), t) + new Vector3(0, PlateHeight(actor), 0);
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
            var w = (int)Math.Ceiling(HudNodes.Width(name, NameFont)) + (2 * PlatePad);
            var h = NameFont + (2 * PlatePad);
            var rect = new PixelRect((int)Math.Round(screen.X) - (w / 2), (int)Math.Round(screen.Y) - h, w, h);
            _shown.Add(new Shown(plate, name, rect, origin.DistanceSquaredTo(plate.Anchor)));
        }
        if (_shown.Count == 0)
        {
            return;
        }
        _shown.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        var offsets = HudLayout.Separate(_shown.Select(s => s.Rect).ToList(), width, height, Obstacles(width, height));
        var font = ThemeDB.FallbackFont;
        for (var i = 0; i < _shown.Count; i++)
        {
            var s = _shown[i];
            var r = s.Rect.Offset(offsets[i].Dx, offsets[i].Dy);
            var centre = r.X + (r.Width / 2f);
            var nameColor = s.Plate.Targeted ? TargetColor : Colors.White;
            Line(font, new Vector2(centre, r.Y + PlatePad + NameFont), s.Name, NameFont, nameColor);
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
    /// live or fading cast bar over a head where CastBarView placed it (<c>CastBarView.BarRects</c>) and every
    /// SPREAD/STACK world label and callout where Pings placed it (<c>Pings.LabelRects</c>), so a plate never draws
    /// under another HUD text. Plates are placed last, so they yield to the bars and labels, never the other way.
    /// </summary>
    private List<PixelRect> Obstacles(int width, int height)
    {
        var obstacles = new List<PixelRect> { HudLayout.TopBar(width, height).Band(width) };
        if (Driver is null)
        {
            return obstacles;
        }
        if (HudObstacles.PartyFrames(GetNodeOrNull<Control>(PartyFramesPath), Driver.State) is { } frames)
        {
            obstacles.Add(frames);
        }
        if (GetNodeOrNull<UnitFrames>(UnitFramesPath) is { } units)
        {
            obstacles.AddRange(units.FrameRects);
        }
        if (GetNodeOrNull<CastBarView>(CastBarsPath) is { } bars)
        {
            obstacles.AddRange(bars.BarRects);
        }
        if (GetNodeOrNull<Pings>(PingsPath) is { } pings)
        {
            obstacles.AddRange(pings.LabelRects);
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

    // The plate anchor above the feet, metres, per class (ActorBindingRow.PlateHeightMm via PlateRule: 250 mm over the
    // higher of the target height and the scaled model top, the boss capped at 5 m), so a plate sits just over its head.
    private static float PlateHeight(ActorState actor) => ActorView.Binding.For(actor.ClassId).PlateHeightMm * 0.001f;

    private sealed class Plate(string baseName)
    {
        /// <summary>The lines every plate draws: the name (HUD-DECLUTTER: no health number over a head).</summary>
        public static readonly string[] Lines = { "name" };

        public string BaseName { get; } = baseName;

        public bool Visible { get; set; }

        public bool Logged { get; set; }

        public Vector3 Anchor { get; set; }

        public bool Targeted { get; set; }
    }

    private readonly record struct Shown(Plate Plate, string Name, PixelRect Rect, float Depth);
}
