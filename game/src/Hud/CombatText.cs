using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.View;

namespace Keyrun.Hud;

/// <summary>
/// Floating combat text (HUD-DECLUTTER, Matt 2026-10-19: "only show the played characters damage numbers in fight"):
/// one rising, fading number over the target for each hit the human's seat lands
/// (<see cref="HudDeclutter.WorldDamageNumber"/>). Bots' hits spawn nothing, and damage the party takes reads as health
/// leaving the party frames, never as a number over a bot. Reads the sim events only.
/// </summary>
public partial class CombatText : Control
{
    private const float LifeSeconds = 1.0f;
    private const float RisePx = 42f;
    private const float AnchorHeightM = 2.6f;
    private const int FontSize = 18;
    private const int OutlineSize = 4;

    private static readonly Color NumberColor = new(1f, 0.93f, 0.55f, 1f);
    private static readonly Color Outline = new(0f, 0f, 0f, 0.85f);

    private readonly List<Number> _numbers = new();
    private SimDriver? _driver;

    /// <summary>The driver to read events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
            _driver.Stepped += OnStepped;
            _driver.RunStarted += _numbers.Clear;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_driver is not null)
        {
            _driver.Stepped -= OnStepped;
            _driver.RunStarted -= _numbers.Clear;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        for (var i = _numbers.Count - 1; i >= 0; i--)
        {
            _numbers[i].Age += (float)delta;
            if (_numbers[i].Age >= LifeSeconds)
            {
                _numbers.RemoveAt(i);
            }
        }
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        var camera = GetViewport()?.GetCamera3D();
        if (_driver is null || camera is null)
        {
            return;
        }
        var font = ThemeDB.FallbackFont;
        var local = GetGlobalRect().Position;
        foreach (var n in _numbers)
        {
            var world = n.World;
            if (_driver.State.Actors.TryGetValue(n.TargetId, out var target))
            {
                world = ActorView.ToMetres(target.Pos);
            }
            world += new Vector3(0, AnchorHeightM, 0);
            if (camera.IsPositionBehind(world))
            {
                continue;
            }
            var t = n.Age / LifeSeconds;
            var text = n.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var at = camera.UnprojectPosition(world) - local - new Vector2(HudNodes.Width(text, FontSize) / 2f, RisePx * t);
            var alpha = 1f - (t * t);
            DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, FontSize, OutlineSize, new Color(Outline, Outline.A * alpha));
            DrawString(font, at, text, HorizontalAlignment.Left, -1, FontSize, new Color(NumberColor, alpha));
        }
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver is null)
        {
            return;
        }
        var human = _driver.Setup?.HumanActorId ?? SimDriver.WardenId;
        foreach (var e in events)
        {
            if (!HudDeclutter.WorldDamageNumber(e, human, out var targetId, out var amount))
            {
                continue;
            }
            var world = _driver.State.Actors.TryGetValue(targetId, out var target) ? ActorView.ToMetres(target.Pos) : Vector3.Zero;
            _numbers.Add(new Number(targetId, amount, world));
            HudNodes.Proof(_driver, FormattableString.Invariant($"HUD fct source={human} target={targetId} amount={amount}"));
        }
    }

    private sealed class Number(int targetId, long amount, Vector3 world)
    {
        public int TargetId { get; } = targetId;

        public long Amount { get; } = amount;

        public Vector3 World { get; } = world;

        public float Age { get; set; }
    }
}
