using System.Collections.Generic;
using System.Linq;
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
/// <see cref="AnimRule.FlashTicks"/> sim ticks (<see cref="AnimRule.FlashPermilleFor"/>, party bodies only: an enemy never
/// flashes, ENEMY-BODY-STABLE), logged as <c>ANIM flash</c> and, through the host, <c>FLASH actor=N tick=T strength=S</c>.
/// PARTY-SOLID (Matt 2026-10-21, "i want to be able to see my party"): the drawn alpha is
/// <see cref="CameraNearFade.AppliedPermille"/>, which is opaque for every body: the seated player's own model, every party
/// bot and every enemy. The old party fades (f-84d213a5's sight-line fade, CAMERA-FG's distance, occlusion and
/// screen-height rules) no longer lower it; the CAMERA line below still reports what those rules rate. A change of the
/// drawn alpha is logged as <c>FADE actor=N alpha=A tick=T</c>. ENEMY-BODY-STABLE: no enemy (the boss and the twins) is
/// flashed on a hit, so its colour and alpha stay stable.
/// Each 60 sim ticks in a pull, every living body (party and enemies) logs a
/// <c>CAMERA tick=T actor=N dist=D near=A applied=A occl=0|1 sh=S cap=C</c> line.
/// U6 GEAR: the Warden's equipped items draw as one primitive mark per slot (<see cref="GearVisual"/>, pure), tinted by
/// tier, read live from the between-runs screen. A change to the equipment rebuilds the marks and logs
/// <c>GEAR attach actor=N slot=S tier=T shape=X</c> (or <c>GEAR none actor=N</c>). Only the player's own body wears gear;
/// an enemy, and a bot ally, never gets a mark.
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
    // KF1 strike cue: the melee ability set, loaded once from the C#-only melee-abilities.json (the sim RunContent carries no
    // melee data and the view holds no ContentRegistry). Lazy so the JSON parse happens on the first strike, not per frame.
    private static readonly System.Lazy<System.Collections.Generic.IReadOnlySet<string>> MeleeSet =
        new(() => StrikeCueRule.MeleeIds(ContentLoader.Load().MeleeAbilities));

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
    private bool _hitSparkPending; // R-ANIM-2 (b): a health drop whose spark waits for the attack's contact frame
    private bool _flashOn;
    private int _flashLoggedTick = int.MinValue;

    private System.Collections.Generic.List<FadeSurface>? _fadeSurfaces;
    private float _fade = 1f;
    private int _fadeWant = PartyFade.OpaquePermille;
    private bool _fadeOn;
    private int _cameraTick = int.MinValue;

    // U6 GEAR: the marks last drawn (null until the first look, so the first frame logs its state), their root under this
    // node (in the root frame, so they turn with the body and are not scaled by the model), and their meshes.
    private System.Collections.Generic.IReadOnlyList<GearMark>? _gearPlan;
    private Node3D? _gearRoot;
    private readonly System.Collections.Generic.List<MeshInstance3D> _gearMeshes = new();
    private readonly System.Collections.Generic.List<(BoneAttachment3D Attach, GearMountPlacement Placement)> _gearBones = new();
    private bool _gearFaded;

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
        Gear();
        if (_measureIn > 0 && --_measureIn == 0)
        {
            LogModelHeight();
        }
    }

    // Binds the body once the actor exists in the sim state (the static Warden's view is ready before any run).
    // HUMAN-PLAY (D65/D66): a view outlives its run (the static seat-1 view, and a spawned body whose actor id the next
    // run reuses), so when the screen's class pick moves a class to another seat (a Chanter run after a Warden run puts
    // the Warden on seat 2) the body is rebuilt for the actor's new class instead of keeping the old model.
    private void TryBind()
    {
        if (Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var actor))
        {
            return;
        }
        if (_bound && string.Equals(actor.ClassId, _classId, System.StringComparison.Ordinal))
        {
            return;
        }
        if (_bound)
        {
            Unbind(actor.ClassId);
        }
        _bound = true;
        _classId = actor.ClassId;
        var row = Binding.For(actor.ClassId);
        var path = row.ResPath; // res://assets/<pack folder>/<file>: kaykit-adventurers or kaykit-skeletons
        // An unimported .glb (fresh worktree, finding f-30dcbb08) can exist yet fail to load: that is a fallback too.
        // --capsule-class (ArenaOptions.Capsules, a scene-test seam): a listed class takes the capsule branch even with a row.
        var forcedCapsule = Driver.Options.Capsules.Contains(actor.ClassId);
        var scene = !forcedCapsule && path is not null && ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
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
        _animator = ActorAnimator.TryCreate(model, row.Clips, row.ScalePermille, Driver.HostLine); // ANIM lines to the run log
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

    // Drops the bound model (TryBind rebinds on the next line), back to the bare capsule the view started as.
    private void Unbind(string newClassId)
    {
        GD.Print($"ACTOR rebind id={ActorId} from={_classId} to={newClassId}");
        if (_model is not null)
        {
            RemoveChild(_model);
            _model.QueueFree();
            _model = null;
        }
        _animator = null;
        _fadeSurfaces = null;
        _fadeOn = false;
        _flashOn = false;
        _shadows = true;
        _measureIn = -1;
        GetNode<MeshInstance3D>("Body").Visible = true;
        GetNode<MeshInstance3D>("Nose").Visible = !BoxShape;
        _bound = false;
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
        if (newTick)
        {
            PlayStrikeCues(now);
        }
    }

    // COMBAT-STRIKE-CUE (KF1, view-only; Godot code, not compiled or run by the tests): on each new sim tick a melee
    // strike that lands damage (StrikeCueRule over the frame's events) draws a blade at hand height that sweeps across
    // the struck target for StrikeCueRule.DurationMs (500 ms, the WoW swing window, f-e20614cc), then frees it. Every
    // frame of the sweep is StrikeCueRule.Pose (height, aim caster -> target, sweep offset); the pivot is top-level so the
    // aim is world-space, not the body's Rotation. The hit already landed on the cue's tick: this never moves hit timing.
    private void PlayStrikeCues(ActorState now)
    {
        var abilities = Driver?.Run?.Content.Abilities;
        if (abilities is null || Driver is null)
        {
            return;
        }
        var facts = new List<StrikeEvent>();
        foreach (var e in Driver.FrameEvents)
        {
            if (e.Type == StrikeCueRule.DamageEvent && e.Get("sourceId") is JsInt src)
            {
                int? target = e.Get("targetId") is JsInt tgt ? (int)tgt.Value : null;
                facts.Add(new StrikeEvent(e.Type, e.Tick, (int)src.Value, null, target));
            }
            else if (e.Type == StrikeCueRule.ResolveEvent && e.Get("actorId") is JsInt actor && e.Get("abilityId") is JsString ability)
            {
                facts.Add(new StrikeEvent(e.Type, e.Tick, (int)actor.Value, ability.Value));
            }
        }
        var melee = MeleeSet.Value;
        var yaw = SwingFacing.RenderYaw(Driver.State, now, abilities, _hold);
        foreach (var cue in StrikeCueRule.Cues(facts, melee, _ => yaw))
        {
            if (cue.ActorId != ActorId)
            {
                continue;
            }
            var casterX = (int)now.Pos.X;
            var casterZ = (int)now.Pos.Z;
            var (targetX, targetZ) = (casterX, casterZ);
            if (cue.TargetId is int tid && tid != ActorId && Driver.State.Actors.TryGetValue(tid, out var struck))
            {
                (targetX, targetZ) = ((int)struck.Pos.X, (int)struck.Pos.Z);
            }
            var reach = StrikeCueRule.ReachMm * MmToM;
            // A long thin blade, flat in the swing plane, running from the hand out along the pivot's local +x (forward).
            var blade = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(reach, 0.04f, 0.14f) },
                Position = new Vector3(reach / 2f, 0f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.85f, 0.4f) },
            };
            var pivot = new Node3D { TopLevel = true };
            pivot.AddChild(blade);
            AddChild(pivot);
            var fallbackYaw = cue.Yaw;
            void Apply(int ms)
            {
                var pose = StrikeCueRule.Pose(casterX, casterZ, targetX, targetZ, ms);
                var baseRad = pose.HasAim ? Mathf.Atan2(pose.AimZMm, pose.AimXMm) : fallbackYaw * RadPerYaw;
                // Sim yaw theta faces (cos, sin) in (x, z); a Y rotation of -theta turns local +x onto it (see YawRadians).
                pivot.GlobalPosition = GlobalPosition + new Vector3(0f, pose.HeightMm * MmToM, 0f);
                pivot.GlobalRotation = new Vector3(0f, -(baseRad + (pose.SweepYaw * RadPerYaw)), 0f);
                pivot.Visible = pose.Visible;
            }
            Apply(0);
            var tween = pivot.CreateTween();
            tween.TweenMethod(Callable.From<int>(Apply), 0, cue.DurationMs - 1, cue.DurationMs / 1000.0);
            tween.TweenCallback(Callable.From(pivot.QueueFree));
        }
    }

    // KF1: every health drop lights the body for AnimRule.FlashTicks sim ticks (an additive overlay on each mesh), so a
    // hit reads on the victim even while a swing one-shot hides its hit clip. The hurt tick is the sim's; the fade
    // reads the render alpha into the current tick. The overlay is only attached while it shows (no extra draw pass
    // otherwise, under the 600 draw-call budget).
    private void Flash()
    {
        if (Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var now))
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
                // R-ANIM-2 (b): the hit presentation (flash, spark) waits for the attack's contact frame. The sim damage tick
                // is unchanged; only the presentation starts later, so the flash's hurt tick is offset by that delay.
                _hurtTick = Driver.State.Tick + HitDelayTicks();
                _hitSparkPending = true;
            }
        }
        if (_hitSparkPending && Driver.State.Tick >= _hurtTick)
        {
            // R-ANIM-1: one hit spark and VIEW hit line per drop. Spawned on the view root (this node), before the model
            // check, so an actor on the fallback capsule (no bound model, finding f-8a001150) sparks too.
            _hitSparkPending = false;
            HitSparkView.Spawn(this, ActorId, Driver.State.Tick);
        }
        if (_model is null)
        {
            return; // the flash overlay needs a bound model; the capsule has none
        }
        // ENEMY-BODY-STABLE: an enemy never flashes (its strength is 0 at every tick), so no enemy mesh gets the overlay.
        var strength = AnimRule.FlashPermilleFor(RoleOf(now), Driver.State.Tick - _hurtTick, (int)(Driver.Alpha * 1000f));
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
        // ENEMY-BODY-STABLE receipt: one FLASH line per sim tick the overlay is shown, through Driver.HostLine (the run log).
        if (Driver.State.Tick != _flashLoggedTick)
        {
            _flashLoggedTick = Driver.State.Tick;
            Driver.HostLine(AnimRule.FlashLine(ActorId, _flashLoggedTick, strength));
        }
    }

    /// <summary>R-ANIM-2 (b): the ticks this actor's hit presentation waits after its health drop (see <see cref="HitPresentation.DelayTicks"/>).</summary>
    private int HitDelayTicks() => HitPresentation.DelayTicks(Driver?.FrameEvents, ActorId);

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

    // PARTY-SOLID (Matt 2026-10-21): the drawn alpha comes from one entry point, CameraNearFade.AppliedPermille, which is
    // opaque for every body (party and enemies). The f-84d213a5 sight-line fade (PartyFade.AlphaPermille) is no longer
    // applied here. The material path below still eases toward the wanted alpha, so a body never stays translucent.
    private void Fade(float delta)
    {
        if (Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        var role = RoleOf(now);
        var want = PartyFade.OpaquePermille;
        var camera = GetViewport()?.GetCamera3D();
        // CAMERA-FG inputs (distance to the lens, occlusion of the pivot, screen height) are still measured for the CAMERA
        // log line; AppliedPermille rates every body opaque whatever they read. The camera is the viewport's current
        // camera; the pivot is its rig's position.
        if (camera is not null)
        {
            var chest = GlobalPosition + new Vector3(0, PartyFade.BodyCentreMm * MmToM, 0);
            var distanceMm = Mathf.RoundToInt(camera.GlobalPosition.DistanceTo(chest) / MmToM);
            var pivot = camera.GetParent() is Node3D rig ? rig.GlobalPosition : camera.GlobalPosition;
            var occludes = CameraNearFade.Occludes(role, ToMm3(camera.GlobalPosition), ToMm3(pivot), ToMm3(chest));
            // CAMERA-FG screen height: a party body drawn taller than a third of the viewport is capped (CameraNearFade);
            // the cap is 1000 for Self and enemies, so the boss and the twins are never held by it.
            var screenHeight = ScreenHeight(camera, GlobalPosition, GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero);
            var cap = CameraNearFade.ScreenHeightCapPermille(role, screenHeight);
            // The one entry point for the drawn alpha: 1000 for every enemy (ENEMY-BODY-STABLE) and every party body
            // (PARTY-SOLID), whatever the distance, occlusion or height.
            want = Mathf.Min(want, CameraNearFade.AppliedPermille(role, distanceMm, occludes, screenHeight));
            LogCamera(now, distanceMm, CameraNearFade.AlphaPermille(distanceMm), occludes, screenHeight, cap, want);
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

    // ENEMY-BODY-STABLE: who the body is, for the flash and the near-camera rules. The player's own model is Self, another
    // player-kind actor is Ally (a party bot), and every other actor is an Enemy (never flashed, never faded).
    private FadeRole RoleOf(ActorState now) =>
        ActorId == SimDriver.WardenId ? FadeRole.Self
        : now.Kind == ActorKind.Player ? FadeRole.Ally
        : FadeRole.Enemy;

    // CAMERA proof line on the run log (the TICK/EVENT channel, Driver.HostLine): once per 60 sim ticks while an enemy is
    // alive (a pull), one line per living body: party (Kind Player, the Warden included) and enemies (the boss and the
    // twins), so the capture proves from the log that no enemy is held by the screen-height rule. dist is the lens to the
    // body's chest, mm; near is the distance rule's alpha; applied is the alpha this view draws; occl is 1 when the body
    // occludes the pivot; sh is the body's projected height, permille of the viewport; cap is the screen-height rule's
    // alpha (1000 for every enemy and for the Warden). A tick is logged once per view, so frames do not repeat it.
    private void LogCamera(ActorState now, int distanceMm, int nearPermille, bool occludes, int screenHeight, int capPermille, int appliedPermille)
    {
        if (Driver is null)
        {
            return;
        }
        var tick = Driver.State.Tick;
        if (tick % 60 != 0 || tick == _cameraTick || !now.Alive || !PullLive())
        {
            return;
        }
        _cameraTick = tick;
        Driver.HostLine(System.FormattableString.Invariant(
            $"CAMERA tick={tick} actor={ActorId} dist={distanceMm} near={nearPermille} applied={appliedPermille} occl={(occludes ? 1 : 0)} sh={screenHeight} cap={capPermille}"));
    }

    // CAMERA-FG screen height: the body's feet and CameraNearFade.BodyHeightMm above them projected on the drawn camera, as
    // permille of the viewport height (0 when either point is behind the camera or there is no viewport). Mirrors
    // CameraRig.MeasureFraming.
    private static int ScreenHeight(Camera3D camera, Vector3 feet, Vector2 viewportSize)
    {
        var head = feet + new Vector3(0, CameraNearFade.BodyHeightMm * MmToM, 0);
        if (viewportSize.Y <= 0f || camera.IsPositionBehind(feet) || camera.IsPositionBehind(head))
        {
            return 0;
        }
        return CameraNearFade.ScreenHeightPermille(
            Mathf.RoundToInt(camera.UnprojectPosition(head).Y),
            Mathf.RoundToInt(camera.UnprojectPosition(feet).Y),
            Mathf.RoundToInt(viewportSize.Y));
    }

    private bool PullLive()
    {
        foreach (var (_, actor) in Driver!.State.Actors)
        {
            if (actor.Kind == ActorKind.Enemy && actor.Alive)
            {
                return true;
            }
        }
        return false;
    }

    // Drives the fade on the bound CC0 model (per-view duplicates of its active materials, swapped in only while faded so
    // an opaque body keeps its original, non-sorted materials) or, with no model, on the capsule's own materials.
    private void ApplyFade(float alpha)
    {
        var faded = alpha < 0.999f;
        FadeGear(alpha, faded);
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

    // U6 GEAR: the marks sit under this node, not under the model, so the model's fade does not reach them. The Warden's
    // gear fades with its body (CAMERA-FG applies to the Warden too) and is opaque again when the body is.
    private void FadeGear(float alpha, bool faded)
    {
        if (_gearMeshes.Count == 0 || (!faded && !_gearFaded))
        {
            return;
        }
        _gearFaded = faded;
        foreach (var mi in _gearMeshes)
        {
            if (mi.MaterialOverride is not StandardMaterial3D m)
            {
                continue;
            }
            m.Transparency = faded ? BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass : BaseMaterial3D.TransparencyEnum.Disabled;
            var c = m.AlbedoColor;
            m.AlbedoColor = new Color(c.R, c.G, c.B, faded ? alpha : 1f);
        }
    }

    // U6 GEAR: the Warden's equipped items, as the pure GearVisual plan. Equipment is read live from the between-runs screen
    // (the player's progression; the Screen group is the one SimDriver reads too), so a change to it rebuilds the marks on
    // the next frame. Only the player's own body (Self) reads the screen; an enemy or a bot ally gets an empty plan.
    private void Gear()
    {
        if (Driver is null || !Driver.State.Actors.TryGetValue(ActorId, out var now))
        {
            return;
        }
        var tiers = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
        if (RoleOf(now) == FadeRole.Self &&
            GetTree()?.GetFirstNodeInGroup(Keyrun.Hud.Screen.GroupName) is Keyrun.Hud.Screen screen)
        {
            foreach (var (slot, item) in screen.Progression.Equipped)
            {
                tiers[slot] = item.Tier;
            }
        }
        var plan = GearVisual.Plan(tiers);
        if (_gearRoot is not null)
        {
            _gearRoot.Visible = now.Alive; // a dead body loses its gear with it
        }
        // Bone-mounted marks sit under the Skeleton3D, outside the gear root, so each one follows the body through the rule.
        foreach (var (attach, placement) in _gearBones)
        {
            if (IsInstanceValid(attach))
            {
                attach.Visible = GearMountRule.Visible(now.Alive, placement);
            }
        }
        if (_gearPlan is not null && plan.SequenceEqual(_gearPlan))
        {
            return;
        }
        _gearPlan = plan;
        if (_gearRoot is not null)
        {
            RemoveChild(_gearRoot);
            _gearRoot.QueueFree();
            _gearRoot = null;
        }
        foreach (var (attach, _) in _gearBones)
        {
            if (IsInstanceValid(attach))
            {
                attach.GetParent()?.RemoveChild(attach);
                attach.QueueFree();
            }
        }
        _gearBones.Clear();
        _gearMeshes.Clear();
        _gearFaded = false;
        if (plan.Count > 0)
        {
            _gearRoot = new Node3D { Name = "Gear", Visible = now.Alive };
            AddChild(_gearRoot);
            var skeleton = _model?.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
            var scale = _model is null || _model.Scale.X <= 0f ? 1f : _model.Scale.X;
            foreach (var mark in plan)
            {
                var mesh = BuildGearMesh(mark);
                var def = GearMarkDefs.All.First(d => d.Slot == mark.Slot);
                var onBone = skeleton is not null && skeleton.FindBone(mark.Bone) >= 0;
                var placement = GearMountRule.Resolve(def, onBone);
                if (onBone)
                {
                    // Ride the bone: BONE-LOCAL offsets in the model frame, undoing the model's scale so sizes stay metres.
                    mesh.Position = PlacementToMetres(placement) / scale;
                    mesh.Scale = Vector3.One / scale;
                    var attach = new BoneAttachment3D { Name = "GearBone_" + mark.Slot, BoneName = mark.Bone };
                    skeleton!.AddChild(attach);
                    attach.AddChild(mesh);
                    attach.Visible = GearMountRule.Visible(now.Alive, placement);
                    _gearBones.Add((attach, placement));
                }
                else
                {
                    // No skeleton (the capsule fallback): root-frame placement from GearMountRule (root X forward, Z lateral).
                    mesh.Position = PlacementToMetres(placement);
                    if (mesh.Mesh is BoxMesh box)
                    {
                        box.Size = new Vector3(mark.SizeZ, mark.SizeY, mark.SizeX);
                    }
                    _gearRoot.AddChild(mesh);
                }
                _gearMeshes.Add(mesh);
            }
        }
        // The GEAR log names only the Warden (Self): the scene test requires that no enemy or bot ally is named by any GEAR
        // line. An enemy still gets its (empty) plan above; it just does not log it.
        if (RoleOf(now) != FadeRole.Self)
        {
            return;
        }
        if (plan.Count == 0)
        {
            GearLine(System.FormattableString.Invariant($"GEAR none actor={ActorId}"));
            return;
        }
        foreach (var mark in plan)
        {
            GearLine(System.FormattableString.Invariant(
                $"GEAR attach actor={ActorId} slot={mark.Slot} tier={mark.Tier} shape={mark.Shape.ToString().ToLowerInvariant()}"));
        }
    }

    private void GearLine(string line)
    {
        GD.Print(line);
        Driver?.HostLine(line);
    }

    /// <summary>Converts a gear mount placement from integer millimetres (GearMountRule) to Godot metres at the view boundary.</summary>
    private static Vector3 PlacementToMetres(GearMountPlacement p) => new(p.X * MmToM, p.Y * MmToM, p.Z * MmToM);

    // One primitive mark: a box or a sphere, unshadowed (the marks add draw calls to the Warden only), tinted by tier.
    private static MeshInstance3D BuildGearMesh(GearMark mark)
    {
        Mesh mesh = mark.Shape == GearShape.Sphere
            ? new SphereMesh { Radius = mark.SizeX / 2f, Height = mark.SizeX }
            : new BoxMesh { Size = new Vector3(mark.SizeX, mark.SizeY, mark.SizeZ) };
        return new MeshInstance3D
        {
            Name = "Gear_" + mark.Slot,
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(((mark.Rgb >> 16) & 0xFF) / 255f, ((mark.Rgb >> 8) & 0xFF) / 255f, (mark.Rgb & 0xFF) / 255f),
                Roughness = 0.5f,
                Metallic = 0.3f,
            },
        };
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
        // FACE-TARGET (f-e0650754): every frame, an actor with a live target faces it while casting, attacking or aggroed
        // (ActorFacing); the seat the human steers keeps the camera's facing. The sim Yaw alone left packs looking away.
        var steered = ActorId == (Driver.Setup?.HumanActorId ?? SimDriver.WardenId);
        var fromYaw = ActorFacing.RenderYaw(Driver.PreviousState, before, abilities, _holdBefore, steered);
        var toYaw = ActorFacing.RenderYaw(Driver.State, now, abilities, _hold, steered);
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

/// <summary>
/// R-ANIM-2 (b, e): the hit presentation of a health drop (the body flash and hit spark on <see cref="ActorView"/>, the
/// floating damage number on <c>CombatText</c>) waits for the contact frame of the attack that dealt it. The sim damage
/// tick is unchanged; this is presentation only.
/// </summary>
public static class HitPresentation
{
    /// <summary>
    /// The sim ticks a hit presentation waits after the damage on <paramref name="victimId"/>: the contact frame of the
    /// release clip of the ability whose source dealt the damage this step or frame (the damage event's sourceId resolved
    /// it), rounded up to whole ticks. A hit with no resolving ability (a tick effect, an unmapped ability) waits 0 ticks.
    /// </summary>
    /// <param name="events">The sim events the presentation reads (one step, or every step of a frame); null means none.</param>
    /// <param name="victimId">The actor the damage landed on.</param>
    public static int DelayTicks(IReadOnlyList<SimEvent>? events, int victimId)
    {
        if (events is null)
        {
            return 0;
        }
        var delayMs = 0;
        foreach (var hit in events)
        {
            if (hit.Type != StrikeCueRule.DamageEvent || hit.Get("targetId") is not JsInt target || target.Value != victimId
                || hit.Get("sourceId") is not JsInt source)
            {
                continue;
            }
            foreach (var done in events)
            {
                if (done.Type == AnimRule.ResolveEvent && done.Get("actorId") is JsInt caster && caster.Value == source.Value
                    && done.Get("abilityId") is JsString ability)
                {
                    delayMs = System.Math.Max(delayMs, AnimRule.HitPresentationDelayMs(AbilityClipTable.ReleaseClip(ability.Value, string.Empty)));
                }
            }
        }
        return (delayMs + AbilityAnim.TickMs - 1) / AbilityAnim.TickMs;
    }
}
