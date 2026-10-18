using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Cast bars, drawn in screen space over the 3D view: one above every casting actor's head (the sim carries a cast as
/// state: CastId, CastStartTick, CastEndsTick), filled by the fraction of the cast elapsed at the displayed fractional
/// tick, with the ability's content name and the seconds left. An interruptible cast has an amber frame (kick it),
/// an uninterruptible one grey. The Warden's own cast also shows as a wide bar above the ability bar. When the sim
/// interrupts a cast the bar turns red and reads "Interrupted"; a completed cast flashes green; both fade out.
/// </summary>
public partial class CastBarView : Control
{
    private const float HeadHeightM = 3.0f; // above the nameplate (LabelView, 2.35 m)
    private const float FadeSeconds = 0.9f;
    private const int FontSize = 14;

    private static readonly Vector2 BarSize = new(150, 16);
    private static readonly Vector2 PlayerBarSize = new(320, 22);
    private static readonly Color Back = new(0.05f, 0.05f, 0.07f, 0.8f);
    private static readonly Color Fill = new(0.95f, 0.72f, 0.2f, 0.95f);
    private static readonly Color KickFrame = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color LockedFrame = new(0.55f, 0.55f, 0.6f, 1f);
    private static readonly Color InterruptedColor = new(0.9f, 0.15f, 0.12f, 1f);
    private static readonly Color DoneColor = new(0.35f, 0.85f, 0.4f, 1f);

    private readonly List<Ending> _endings = new();

    /// <summary>The driver to read state and events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>Gap between the player's cast bar and the bottom of the screen, px (clears the ability bar).</summary>
    [Export]
    public float PlayerBarBottomGap { get; set; } = 150;

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        if (Driver is not null)
        {
            Driver.Stepped += OnStepped;
            Driver.RunStarted += _endings.Clear;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Driver is not null)
        {
            Driver.Stepped -= OnStepped;
            Driver.RunStarted -= _endings.Clear;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        for (var i = _endings.Count - 1; i >= 0; i--)
        {
            _endings[i].Age += (float)delta;
            if (_endings[i].Age >= FadeSeconds)
            {
                _endings.RemoveAt(i);
            }
        }
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Driver is null)
        {
            return;
        }
        var camera = GetViewport().GetCamera3D();
        var font = ThemeDB.FallbackFont;
        var now = Driver.State.Tick + Driver.Alpha;
        foreach (var (id, actor) in Driver.State.Actors)
        {
            if (!actor.Alive || actor.CastId is not { } castId || actor.CastStartTick is not { } start || actor.CastEndsTick is not { } end)
            {
                continue;
            }
            var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
            var name = Driver.AbilityName(castId);
            var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
            var interruptible = Driver.Run?.Content.Abilities.TryGetValue(castId, out var def) == true && def.Interruptible;
            var frame = interruptible ? KickFrame : LockedFrame;
            var label = $"{name}  {secondsLeft:0.0}s";
            if (camera is not null && ScreenPoint(camera, id, out var p))
            {
                DrawBar(font, p - new Vector2(BarSize.X / 2, 0), BarSize, progress, Fill, frame, label);
            }
            if (id == SimDriver.WardenId)
            {
                DrawBar(font, PlayerBarOrigin(), PlayerBarSize, progress, Fill, frame, label);
            }
        }
        foreach (var e in _endings)
        {
            var alpha = 1f - (e.Age / FadeSeconds);
            var color = new Color(e.Interrupted ? InterruptedColor : DoneColor, alpha);
            var label = e.Interrupted ? $"Interrupted: {e.Name}" : e.Name;
            if (camera is not null && ScreenPoint(camera, e.ActorId, out var p))
            {
                DrawBar(font, p - new Vector2(BarSize.X / 2, 0), BarSize, 1f, color, color, label);
            }
            if (e.ActorId == SimDriver.WardenId)
            {
                DrawBar(font, PlayerBarOrigin(), PlayerBarSize, 1f, color, color, label);
            }
        }
    }

    private Vector2 PlayerBarOrigin() => new((Size.X - PlayerBarSize.X) / 2, Size.Y - PlayerBarBottomGap);

    private bool ScreenPoint(Camera3D camera, int actorId, out Vector2 point)
    {
        point = default;
        if (Driver is null || !Driver.State.Actors.TryGetValue(actorId, out var actor))
        {
            return false;
        }
        var before = Driver.PreviousState.Actors.TryGetValue(actorId, out var prev) ? prev : actor;
        var world = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha) + new Vector3(0, HeadHeightM, 0);
        if (camera.IsPositionBehind(world))
        {
            return false;
        }
        point = camera.UnprojectPosition(world);
        return true;
    }

    private void DrawBar(Font font, Vector2 origin, Vector2 size, float progress, Color fill, Color frame, string label)
    {
        DrawRect(new Rect2(origin, size), Back);
        DrawRect(new Rect2(origin, new Vector2(size.X * progress, size.Y)), fill);
        DrawRect(new Rect2(origin, size), frame, filled: false, width: 2f);
        var textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, FontSize);
        var textPos = new Vector2(origin.X + ((size.X - textSize.X) / 2), origin.Y - 4);
        DrawString(font, textPos + new Vector2(1, 1), label, HorizontalAlignment.Left, -1, FontSize, new Color(0, 0, 0, frame.A));
        DrawString(font, textPos, label, HorizontalAlignment.Left, -1, FontSize, new Color(1, 1, 1, frame.A));
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (Driver is null)
        {
            return;
        }
        foreach (var e in events)
        {
            if (e.Type == "interrupt" && e.Get("targetId") is JsInt target && e.Get("abilityId") is JsString ability)
            {
                _endings.Add(new Ending((int)target.Value, Driver.AbilityName(ability.Value), interrupted: true));
            }
            else if (e.Type == "cast_complete" && e.Get("actorId") is JsInt actor && e.Get("abilityId") is JsString done &&
                Driver.Run?.Content.Abilities.TryGetValue(done.Value, out var def) == true && def.CastTicks > 0)
            {
                _endings.Add(new Ending((int)actor.Value, Driver.AbilityName(done.Value), interrupted: false));
            }
        }
    }

    private sealed class Ending
    {
        public Ending(int actorId, string name, bool interrupted)
        {
            ActorId = actorId;
            Name = name;
            Interrupted = interrupted;
        }

        public int ActorId { get; }

        public string Name { get; }

        public bool Interrupted { get; }

        public float Age { get; set; }
    }
}
