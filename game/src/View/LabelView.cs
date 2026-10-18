using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// One floating nameplate per sim actor other than the Warden: the content display name ("Choir Cantor"), a health
/// readout, and a marker on the Warden's current target. It also gives every actor the run spawns a body: an Actor.tscn instance (ActorView)
/// under <see cref="ActorsPath"/>, coloured by role, unless the scene already holds a view for that id (the Warden's,
/// which CameraRig follows). Plates and bodies of actors that leave the state (a fresh run) are freed.
/// </summary>
public partial class LabelView : Node3D
{
    private const float PlateHeight = 2.35f;

    private static readonly Color MeleeColor = new(0.78f, 0.22f, 0.2f);
    private static readonly Color LieutenantColor = new(0.62f, 0.3f, 0.85f);
    private static readonly Color OtherEnemyColor = new(0.85f, 0.45f, 0.15f);
    private static readonly Color PlayerColor = new(0.2f, 0.4f, 0.9f);

    private readonly Dictionary<int, Plate> _plates = new();
    private readonly Dictionary<int, Node3D> _spawned = new();
    private readonly List<int> _gone = new();

    /// <summary>The driver to read state from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The node actor bodies live under.</summary>
    [Export]
    public NodePath ActorsPath { get; set; } = new("../Actors");

    /// <summary>The per-actor body scene (Actor.tscn).</summary>
    [Export]
    public PackedScene? ActorScene { get; set; }

    private SimDriver? Driver { get; set; }

    private Node3D? Actors { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        Actors = GetNodeOrNull<Node3D>(ActorsPath);
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
                plate = NewPlate(Driver.DisplayName(actor));
                _plates[id] = plate;
            }
            // The Warden's own name and health live on the ability bar's meters, not over his head.
            plate.Root.Visible = actor.Alive && id != SimDriver.WardenId;
            if (!plate.Root.Visible)
            {
                continue;
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var t = Driver.Alpha;
            plate.Root.Position = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), t) + new Vector3(0, PlateHeight, 0);
            plate.Health.Text = $"{actor.Health} / {actor.MaxHealth}";
            plate.Health.Modulate = HealthColor(actor.Health, actor.MaxHealth);
            plate.Name.Modulate = id == targetId ? new Color(1f, 0.92f, 0.35f) : Colors.White;
            plate.Name.Text = id == targetId ? "> " + plate.BaseName + " <" : plate.BaseName;
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
            _plates[id].Root.QueueFree();
            _plates.Remove(id);
            if (_spawned.Remove(id, out var body))
            {
                body.QueueFree();
            }
        }
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
        _ when actor.ClassId.EndsWith(".lieutenant", System.StringComparison.Ordinal) => LieutenantColor,
        _ when actor.ClassId.EndsWith(".melee", System.StringComparison.Ordinal) => MeleeColor,
        _ => OtherEnemyColor,
    };

    private static Color HealthColor(long hp, long max)
    {
        var f = max <= 0 ? 0f : Mathf.Clamp((float)hp / max, 0f, 1f);
        return new Color(1f - (0.7f * f), 0.35f + (0.6f * f), 0.3f);
    }

    private Plate NewPlate(string name)
    {
        var root = new Node3D();
        AddChild(root);
        var title = NewLabel(name, 64, new Vector3(0, 0.22f, 0));
        var health = NewLabel(string.Empty, 48, Vector3.Zero);
        root.AddChild(title);
        root.AddChild(health);
        return new Plate(root, title, health, name);
    }

    private static Label3D NewLabel(string text, int fontSize, Vector3 offset) => new()
    {
        Text = text,
        FontSize = fontSize,
        PixelSize = 0.004f,
        Position = offset,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        OutlineSize = 10,
        OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
    };

    private sealed record Plate(Node3D Root, Label3D Name, Label3D Health, string BaseName);
}
