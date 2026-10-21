using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Draws one sim actor, placed each frame by interpolating the actor's mm position and yaw between SimDriver's previous
/// and current state. mm become metres as floats here and only here (game/CLAUDE.md purity rule). The body is the
/// actor class's CC0 model from <see cref="ActorBinding"/> (design/ART-PLAN.md lane 3: Knight, Mage, Rogue_Hooded;
/// lane 4: the KayKit Skeletons and the 1.9x Knight boss), loaded from its row's pack folder, scaled and tinted per its
/// row with its feet at the root and facing local +x, animated by <see cref="ActorAnimator"/>; a class
/// with no row, or a row whose file is missing, keeps the capsule (with a small nose showing facing) and logs
/// <c>ACTOR fallback capsule id=N class=C</c> once. Either way it writes the gate line
/// <c>ACTOR model=&lt;res path|capsule&gt; fallback=&lt;0|1&gt; id=N</c> to the run log (finding f-30dcbb08). KF1: a health drop flashes the bound model for
/// <see cref="AnimRule.FlashTicks"/> sim ticks (<see cref="AnimRule.FlashPermille"/>), logged as <c>ANIM flash</c>.
/// f-84d213a5: a party bot between the camera and the player or the player's target (or pressed against the lens) fades
/// to <see cref="PartyFade.FadedPermille"/> and eases back when it moves (<see cref="PartyFade"/>), logged as
/// <c>FADE actor=N alpha=A tick=T</c> on each change; the player's own model and enemies never fade by that rule.
/// CAMERA-FG: on top of it, any actor body within <see cref="CameraNearFade.FadeDistanceMm"/> of the camera fades by
/// distance (<see cref="CameraNearFade"/>), the Warden's own model included; the lower alpha of the two rules wins.
/// </summary>
public partial class ActorView : Node3D
{
    private const float MmToM = 0.001f;
    private const float RadPerYaw = Mathf.Tau / 1024f;
    private const int MeasureAfterFrames = 10;

    /// <summary>The sim actor id this view follows.</summary>
    [Export]
    public int ActorId { get; set; } = SimDriver.WardenId;

    /// <summary>Draw a box instead of a capsule.</summary>
    [Export]
    public bool BoxShape { get; set; }

    /// <summary>Body colour.</summary>
    [Export]
    public Color BodyColor { get; set; } = new(0.2f, 0.4f, 0.9f);

    /// <summary>The driver to read state from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>The model table; lane 4 extends <see cref="ActorBinding.Default"/> with its enemy rows.</summary>
    public static ActorBinding Binding { get; set; } = ActorBinding.Default;

    private SimDriver? Driver { get; set; }

    private bool _bound;
    private Node3D? _model;
    private ActorAnimator? _animator;
    private SimState? _seenState;
    private SimState? _facingState;
    private FacingHold? _hold;
    private FacingHold? _holdBefore;
    private Vector3 _lastPosition;
    private bool _hasLastPosition;
    private int _measureIn = -1;
    private string _classId = string.Empty;
    private bool _shadows = true;
    private StandardMaterial3D? _flash;
    private SimState? _flashSeen;
    private int _hurtTick = int.MinValue / 2;
    private bool _flashOn;

    private readonly System.Collections.Generic.List<(int X, int Y, int Z)> _foci = new();
    private System.Collections.Generic.List<FadeSurface>? _fadeSurfaces;
    private float _fade = 1f;
    private int _fadeWant = PartyFade.OpaquePermille;
    private bool _fadeOn;

    /// <summary>KF1 hit flash colour (warm white, added over the body) at full strength.</summary>
    private static readonly Color FlashColor = new(1f, 0.92f, 0.85f, 0.45f);

    /// <summary>How fast a party body eases toward its <see cref="PartyFade"/> alpha, per second.</summary>
    [Export]
    public float FadeRate { get; set; } = 12f;

    // One model surface the fade drives: the override it had before (restored when opaque again), its faded copy, and
    // the copy's own albedo alpha at full strength.
    private sealed record FadeSurface(MeshInstance3D Mesh, int Surface, Material? Before, BaseMaterial3D Faded, float BaseAlpha);

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        var body = GetNode<MeshInstance3D>("Body");
        var nose = GetNode<MeshInstance3D>("Nose");
        var material = new StandardMaterial3D { AlbedoColor = BodyColor, Roughness = 0.7f };
        if (BoxShape)
        {
            body.Mesh = new BoxMesh { Size = new Vector3(1.4f, 1.4f, 1.4f) };
            body.Position = new Vector3(0, 0.7f, 0);
            nose.Visible = false;
        }
        else
        {
            body.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
            body.Position = new Vector3(0, 0.9f, 0);
            nose.Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.15f, 0.2f) };
            nose.Position = new Vector3(0.42f, 1.45f, 0); // local +x = facing (see Place)
            nose.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.9f, 1f) };
        }
        body.MaterialOverride = material;
        TryBind();
        Place();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        TryBind();
        Place();
        Animate((float)delta);
        Flash();
        Fade((float)delta);
        if (_measureIn > 0 && --_measureIn == 0)
        {
            LogModelHeight();
        }
    }

    // Binds the body once the actor exists in the sim state (the static Warden's view is ready before any run).
    private void TryBind()
    {
        if (_bound || Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var actor))
        {
            return;
        }
        _bound = true;
        _classId = actor.ClassId;
        var row = Binding.For(actor.ClassId);
        var path = row.ResPath; // res://assets/<pack folder>/<file>: kaykit-adventurers or kaykit-skeletons
        // An unimported .glb (fresh worktree, finding f-30dcbb08) can exist yet fail to load: that is a fallback too.
        var scene = path is not null && ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
        if (scene is null)
        {
            GD.Print($"ACTOR fallback capsule id={ActorId} class={actor.ClassId}");
            LogModelLine(null);
            return;
        }
        var model = scene.Instantiate<Node3D>();
        model.Name = "Model";
        var s = row.ScalePermille / 1000f;
        model.Scale = new Vector3(s, s, s);
        model.Rotation = new Vector3(0, Mathf.Pi / 2f, 0); // the pack faces +z; views face local +x (see Place)
        AddChild(model);
        foreach (var hidden in row.HiddenNodes)
        {
            if (model.FindChild(hidden, true, false) is Node3D n)
            {
                n.Visible = false;
            }
        }
        if (row.TintRgb is int rgb)
        {
            Tint(model, new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f));
        }
        GetNode<MeshInstance3D>("Body").Visible = false;
        GetNode<MeshInstance3D>("Nose").Visible = false;
        _model = model;
        _animator = ActorAnimator.TryCreate(model, row.Clips, row.ScalePermille);
        // A body bound while its actor is alive rises with its spawn clip (enemies; party rows have none and idle).
        if (actor.Alive)
        {
            _animator?.PlaySpawn();
        }
        GD.Print($"ACTOR model id={ActorId} class={actor.ClassId} file={row.ModelFile} scale={s:0.000} " +
                 (_animator is null ? "clips=static" : _animator.Matched));
        LogModelLine(path);
        _measureIn = MeasureAfterFrames;
    }

    /// <summary>
    /// Writes the model gate line <c>ACTOR model=&lt;res path|capsule&gt; fallback=&lt;0|1&gt; id=N</c> to the run log
    /// (console and <c>--log</c> file) once per bound actor. tools/godot/screens.ps1 counts the fallback=1 lines and exits 9
    /// when any actor rendered the capsule (finding f-30dcbb08); SimDriverSceneTest.ps1 pins fallback=0 in CI.
    /// </summary>
    /// <param name="path">The loaded model's resource path, or null for the capsule fallback.</param>
    private void LogModelLine(string? path)
    {
        var line = System.FormattableString.Invariant(
            $"ACTOR model={path ?? "capsule"} fallback={(path is null ? 1 : 0)} id={ActorId}");
        if (Driver is null)
        {
            GD.Print(line);
            return;
        }
        Driver.HostLine(line);
    }

    private static void Tint(Node model, Color tint)
    {
        var copies = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mi || mi.Mesh is null)
            {
                continue;
            }
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if (mi.GetActiveMaterial(i) is not BaseMaterial3D source)
                {
                    continue;
                }
                if (!copies.TryGetValue(source, out var copy))
                {
                    var dup = (BaseMaterial3D)source.Duplicate();
                    dup.AlbedoColor = source.AlbedoColor * tint;
                    copy = dup;
                    copies[source] = copy;
                }
                mi.SetSurfaceOverrideMaterial(i, copy);
            }
        }
    }

    // Visible height of the placed model in metres (root space), printed once so frames and logs can be checked
    // against the 1.8 m head line LabelView's plates assume.
    private void LogModelHeight()
    {
        if (_model is null)
        {
            return;
        }
        var inverse = GlobalTransform.AffineInverse();
        var top = float.MinValue;
        var bottom = float.MaxValue;
        foreach (var node in _model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mi || !mi.IsVisibleInTree())
            {
                continue;
            }
            var box = inverse * mi.GlobalTransform * mi.GetAabb();
            top = Mathf.Max(top, box.End.Y);
            bottom = Mathf.Min(bottom, box.Position.Y);
        }
        GD.Print($"ACTOR height id={ActorId} class={_classId} top={top:0.000} bottom={bottom:0.000}");
    }

    private void Animate(float delta)
    {
        if (_animator is null || Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        // KF1-READABLE: diff and read events over every step this frame ran, not only the latest (a slow frame runs
        // several, and a swing resolved on an earlier one used to vanish).
        var before = Driver.FrameStartState.Actors.TryGetValue(ActorId, out var prev) ? prev : now;
        var newTick = !ReferenceEquals(Driver.State, _seenState);
        _seenState = Driver.State;
        var speed = 0f;
        if (_hasLastPosition && delta > 0f)
        {
            speed = (Position - _lastPosition).Length() / delta;
        }
        _lastPosition = Position;
        _hasLastPosition = true;
        _animator.Update(before, now, newTick, speed, Driver.FrameEvents, Driver.State.Tick, (int)(Driver.Alpha * 1000f));
    }

    // KF1: every health drop lights the body for AnimRule.FlashTicks sim ticks (an additive overlay on each mesh), so a
    // hit reads on the victim even while a swing one-shot hides its hit clip. The hurt tick is the sim's; the fade
    // reads the render alpha into the current tick. The overlay is only attached while it shows (no extra draw pass
    // otherwise, under the 600 draw-call budget).
    private void Flash()
    {
        if (_model is null || Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        if (!ReferenceEquals(Driver.State, _flashSeen))
        {
            // The first state this view sees is a baseline, not a step (no flash on the run's opening frame).
            var stepped = _flashSeen is not null;
            _flashSeen = Driver.State;
            var before = Driver.FrameStartState.Actors.TryGetValue(ActorId, out var prev) ? prev : now;
            if (stepped && now.Health < before.Health)
            {
                _hurtTick = Driver.State.Tick;
            }
        }
        var strength = AnimRule.FlashPermille(Driver.State.Tick - _hurtTick, (int)(Driver.Alpha * 1000f));
        if (strength <= 0)
        {
            SetFlashOverlay(false);
            return;
        }
        _flash ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            NoDepthTest = false,
        };
        _flash.AlbedoColor = new Color(FlashColor, FlashColor.A * strength / 1000f);
        SetFlashOverlay(true);
    }

    private void SetFlashOverlay(bool on)
    {
        if (_model is null || on == _flashOn)
        {
            return;
        }
        _flashOn = on;
        foreach (var node in _model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mi)
            {
                mi.MaterialOverlay = on ? _flash : null;
            }
        }
        if (on)
        {
            GD.Print(System.FormattableString.Invariant($"ANIM flash actor={ActorId} tick={Driver?.State.Tick ?? -1}"));
        }
    }

    // f-84d213a5 (bible section 3): a party bot standing between the camera and the player's body or the player's target,
    // or pressed against the lens, fades to PartyFade.FadedPermille so cast bars and floor shapes stay readable, and eases
    // back when it moves. The rule is PartyFade's (pure, tested); this only feeds it positions and drives the materials.
    // The player's own model (the Warden the camera follows) and enemies are rated opaque by the rule.
    private void Fade(float delta)
    {
        if (Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        var role = ActorId == SimDriver.WardenId ? FadeRole.Self
            : now.Kind == ActorKind.Player ? FadeRole.Ally
            : FadeRole.Enemy;
        var want = PartyFade.OpaquePermille;
        var camera = GetViewport()?.GetCamera3D();
        if (role == FadeRole.Ally && camera is not null)
        {
            _foci.Clear();
            if (Driver.State.Actors.TryGetValue(SimDriver.WardenId, out var me))
            {
                _foci.Add(ChestMm(SimDriver.WardenId, me));
                if (me.TargetId is int targetId && targetId != ActorId &&
                    Driver.State.Actors.TryGetValue(targetId, out var target) && target.Alive)
                {
                    _foci.Add(ChestMm(targetId, target));
                }
            }
            var centre = ToMm3(GlobalPosition + new Vector3(0, PartyFade.BodyCentreMm * MmToM, 0));
            want = PartyFade.AlphaPermille(ToMm3(camera.GlobalPosition), _foci, new FadeBody(role, centre, PartyFade.BodyRadiusMm));
        }
        // CAMERA-FG: every actor body near the lens fades by distance (CameraNearFade), the Warden's own model included.
        if (camera is not null)
        {
            var chest = GlobalPosition + new Vector3(0, PartyFade.BodyCentreMm * MmToM, 0);
            var distanceMm = Mathf.RoundToInt(camera.GlobalPosition.DistanceTo(chest) / MmToM);
            want = Mathf.Min(want, CameraNearFade.AlphaPermille(distanceMm));
        }
        if (want != _fadeWant)
        {
            _fadeWant = want;
            GD.Print(System.FormattableString.Invariant($"FADE actor={ActorId} alpha={want} tick={Driver.State.Tick}"));
        }
        var goal = want / 1000f;
        _fade += (goal - _fade) * (1f - Mathf.Exp(-delta * FadeRate));
        if (Mathf.Abs(goal - _fade) < 0.01f)
        {
            _fade = goal;
        }
        ApplyFade(_fade);
    }

    // The body's chest on the interpolated position the views draw it at, mm (a sight-line focus for PartyFade).
    private (int X, int Y, int Z) ChestMm(int id, ActorState now)
    {
        var before = Driver!.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : now;
        var feet = ToMetres(before.Pos).Lerp(ToMetres(now.Pos), Driver.Alpha);
        return ToMm3(feet + new Vector3(0, PartyFade.BodyCentreMm * MmToM, 0));
    }

    // Drives the fade on the bound CC0 model (per-view duplicates of its active materials, swapped in only while faded so
    // an opaque body keeps its original, non-sorted materials) or, with no model, on the capsule's own materials.
    private void ApplyFade(float alpha)
    {
        var faded = alpha < 0.999f;
        if (!faded && !_fadeOn)
        {
            return;
        }
        if (_model is null)
        {
            FadeCapsulePart("Body", alpha, faded);
            FadeCapsulePart("Nose", alpha, faded);
            _fadeOn = faded;
            return;
        }
        _fadeSurfaces ??= BuildFadeSurfaces(_model);
        foreach (var s in _fadeSurfaces)
        {
            if (faded)
            {
                var c = s.Faded.AlbedoColor;
                s.Faded.AlbedoColor = new Color(c.R, c.G, c.B, s.BaseAlpha * alpha);
                if (!_fadeOn)
                {
                    s.Mesh.SetSurfaceOverrideMaterial(s.Surface, s.Faded);
                }
            }
            else
            {
                s.Mesh.SetSurfaceOverrideMaterial(s.Surface, s.Before);
            }
        }
        _fadeOn = faded;
    }

    private static System.Collections.Generic.List<FadeSurface> BuildFadeSurfaces(Node model)
    {
        var list = new System.Collections.Generic.List<FadeSurface>();
        var copies = new System.Collections.Generic.Dictionary<Material, BaseMaterial3D>();
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mi || mi.Mesh is null)
            {
                continue;
            }
            for (var i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if (mi.GetActiveMaterial(i) is not BaseMaterial3D source)
                {
                    continue;
                }
                if (!copies.TryGetValue(source, out var copy))
                {
                    copy = (BaseMaterial3D)source.Duplicate();
                    // Depth pre-pass: the faded body still sorts against itself (no inside-out limbs).
                    copy.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
                    copies[source] = copy;
                }
                list.Add(new FadeSurface(mi, i, mi.GetSurfaceOverrideMaterial(i), copy, source.AlbedoColor.A));
            }
        }
        return list;
    }

    private void FadeCapsulePart(string name, float alpha, bool faded)
    {
        if (GetNodeOrNull<MeshInstance3D>(name)?.MaterialOverride is not BaseMaterial3D m)
        {
            return;
        }
        m.Transparency = faded ? BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass : BaseMaterial3D.TransparencyEnum.Disabled;
        var c = m.AlbedoColor;
        m.AlbedoColor = new Color(c.R, c.G, c.B, faded ? alpha : 1f);
    }

    private static (int X, int Y, int Z) ToMm3(Vector3 v) =>
        (Mathf.RoundToInt(v.X / MmToM), Mathf.RoundToInt(v.Y / MmToM), Mathf.RoundToInt(v.Z / MmToM));

    private void Place()
    {
        if (Driver is null ||
            !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        var before = Driver.PreviousState.Actors.TryGetValue(ActorId, out var prev) ? prev : now;
        var t = Driver.Alpha;
        var x = Mathf.Lerp((float)before.Pos.X, (float)now.Pos.X, t) * MmToM;
        var y = Mathf.Lerp((float)before.Pos.Y, (float)now.Pos.Y, t) * MmToM;
        var z = Mathf.Lerp((float)before.Pos.Z, (float)now.Pos.Z, t) * MmToM;
        Position = new Vector3(x, y, z);
        Visible = now.Alive || (_animator?.HoldsDeath ?? false); // a model plays its death clip instead of vanishing
        SetShadows(now.Alive);
        // CAST-FACING: a caster is drawn facing its cast target (CastFacing.RenderYaw), never its sim Yaw alone.
        // KF1-FACING: an instant swing resolves inside one step, so the swinger is held facing whom it struck
        // (SwingFacing) until it moves, the target dies, or it resolves a cone, line or self ability.
        var abilities = Driver.Run?.Content.Abilities;
        if (!ReferenceEquals(Driver.State, _facingState))
        {
            _facingState = Driver.State;
            _holdBefore = _hold;
            _hold = SwingFacing.After(Driver.State, now, Driver.LastEvents, abilities, _hold);
        }
        var fromYaw = SwingFacing.RenderYaw(Driver.PreviousState, before, abilities, _holdBefore);
        var toYaw = SwingFacing.RenderYaw(Driver.State, now, abilities, _hold);
        Rotation = new Vector3(0, -YawRadians(fromYaw, toYaw, t), 0);
    }

    // A dead body lies on the floor as a bone pile or a fallen hero: it stops casting shadows, which take most of a
    // skinned body's draw calls (every cascade redraws it), so the corpses of earlier pulls do not push a later pull
    // over the 600 draw-call budget. Restored if the actor is alive again (a fresh run reuses the static Warden's view).
    private void SetShadows(bool on)
    {
        if (_model is null || on == _shadows)
        {
            return;
        }
        _shadows = on;
        var mode = on ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var node in _model.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mi)
            {
                mi.CastShadow = mode;
            }
        }
    }

    // Sim yaw theta faces (cos theta, sin theta) in (x, z). A Y rotation of -theta turns local +x onto
    // that direction, so views model "forward" as local +x. Interpolates along the shorter arc.
    private static float YawRadians(int from, int to, float t)
    {
        var d = ((to - from) % 1024 + 1024 + 512) % 1024 - 512;
        return (from + (d * t)) * RadPerYaw;
    }

    /// <summary>Sim mm position to a Godot position on the floor plane, metres.</summary>
    public static Vector3 ToMetres(Vec3 mm) => new(mm.X * MmToM, mm.Y * MmToM, mm.Z * MmToM);
}
