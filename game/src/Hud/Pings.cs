using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.View;

namespace Keyrun.Hud;

/// <summary>Where a SPREAD or STACK call comes from.</summary>
public enum PingSource
{
    /// <summary>A pending encounter telegraph whose cast applies the mark (the warning before it lands).</summary>
    Telegraph,

    /// <summary>An actor carrying the mark effect (the call is live on that player).</summary>
    Mark,
}

/// <summary>One SPREAD or STACK call (Godot-free).</summary>
/// <param name="Label">"SPREAD" or "STACK" (telegraph-label.ts title).</param>
/// <param name="Cue">"move apart" or "group up" (telegraph-label.ts cue).</param>
/// <param name="Source">Telegraph or mark.</param>
/// <param name="Id">The telegraph id, or the marked actor id.</param>
/// <param name="CasterId">The enemy whose cast makes the call (telegraph source or effect source).</param>
/// <param name="Pos">Where the label sits, mm (telegraph origin or the marked actor).</param>
/// <param name="TicksLeft">Ticks until the telegraph resolves or the mark ends.</param>
public sealed record PingCall(string Label, string Cue, PingSource Source, int Id, int CasterId, Vec3 Pos, int TicksLeft)
{
    /// <summary>"SPREAD: move apart".</summary>
    public string Text => $"{Label}: {Cue}";

    /// <summary>Identity across ticks: the same telegraph or the same marked actor and caster.</summary>
    public string Key => FormattableString.Invariant($"{Source}:{Id}:{CasterId}:{Label}");

    /// <summary>"HUD ping label=SPREAD cue="move apart" from=telegraph id=3 caster=103 at=(5000,0) left=100".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD ping label={Label} cue=\"{Cue}\" from={Source.ToString().ToLowerInvariant()} id={Id} caster={CasterId} at=({Pos.X},{Pos.Z}) left={TicksLeft}");
}

/// <summary>
/// SPREAD and STACK calls read from the sim state alone (BIBLE s10 "stack here"; client/src/render/telegraph-label.ts
/// and status-chips.ts): every pending telegraph whose ability applies the spread or stack mark, and every live actor
/// that carries one. The mark is the party's shared call: bots read the same telegraphs and effects through their
/// BotView, so the HUD and the bots see one source of truth. Godot-free; never writes the run.
/// </summary>
public static class PingCalls
{
    /// <summary>The spread mark effect (content effects/enemy.json).</summary>
    public const string SpreadEffectId = "spreadMarked";

    /// <summary>The stack mark effect (content effects/enemy.json).</summary>
    public const string StackEffectId = "stackMarked";

    /// <summary>The call for a mark effect id, or null.</summary>
    /// <param name="effectId">The effect id.</param>
    /// <returns>(label, cue) or null.</returns>
    public static (string Label, string Cue)? CallOfEffect(string effectId) => effectId switch
    {
        SpreadEffectId => ("SPREAD", "move apart"),
        StackEffectId => ("STACK", "group up"),
        _ => null,
    };

    /// <summary>The call an ability makes: the first spread or stack mark among its applyEffect ops, or null.</summary>
    /// <param name="def">The ability.</param>
    /// <returns>(label, cue) or null.</returns>
    public static (string Label, string Cue)? CallOfAbility(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        foreach (var op in def.Effects)
        {
            if (op is ApplyEffectOp apply && CallOfEffect(apply.EffectId) is { } call)
            {
                return call;
            }
        }
        return null;
    }

    /// <summary>Every SPREAD/STACK call in the run now: telegraphs first (registry order), then marks by actor id.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The calls.</returns>
    public static IReadOnlyList<PingCall> Calls(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var now = run.State.Tick;
        var calls = new List<PingCall>();
        foreach (var t in run.Encounter.Telegraphs)
        {
            if (run.Content.Abilities.TryGetValue(t.AbilityId, out var def) && CallOfAbility(def) is { } call)
            {
                calls.Add(new PingCall(call.Label, call.Cue, PingSource.Telegraph, t.Id, t.SourceId, t.Origin, Math.Max(0, t.ResolvesTick - now)));
            }
        }
        foreach (var actor in run.State.Actors.Values)
        {
            if (!actor.Alive)
            {
                continue;
            }
            foreach (var e in actor.Effects.Where(e => e.EndsTick > now).OrderBy(e => e.EffectId, StringComparer.Ordinal))
            {
                if (CallOfEffect(e.EffectId) is { } call)
                {
                    calls.Add(new PingCall(call.Label, call.Cue, PingSource.Mark, actor.Id, e.SourceId, actor.Pos, e.EndsTick - now));
                }
            }
        }
        return calls;
    }
}

/// <summary>
/// The SPREAD / STACK labels (ENGINE-PLAN 3C): each call is drawn over its spot in the world (projected through the
/// arena camera) and listed as a callout banner under the Slack bar, so the instruction reads even off screen.
/// Reads the sim state only.
/// </summary>
public partial class Pings : Control
{
    private const int CalloutFont = 22;
    private const int WorldFont = 16;
    private const float CalloutTop = 100;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private SimDriver? _driver;

    /// <summary>The driver to read the run from.</summary>
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
            _driver.RunStarted += OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_driver is not null)
        {
            _driver.Stepped -= OnStepped;
            _driver.RunStarted -= OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta) => QueueRedraw();

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var calls = PingCalls.Calls(run);
        if (calls.Count == 0)
        {
            return;
        }
        var camera = GetViewport()?.GetCamera3D();
        foreach (var call in calls)
        {
            var color = call.Label == "SPREAD" ? HudNodes.Rgb(0xcc79a7) : HudNodes.Rgb(0x56b4e9);
            if (camera is not null)
            {
                var world = ActorView.ToMetres(call.Pos) + new Vector3(0, call.Source == PingSource.Mark ? 2.6f : 0.3f, 0);
                if (!camera.IsPositionBehind(world))
                {
                    var screen = camera.UnprojectPosition(world) - GetGlobalRect().Position;
                    var w = HudNodes.Width(call.Text, WorldFont);
                    HudNodes.Text(this, screen - new Vector2(w / 2, 0), call.Text, WorldFont, color);
                }
            }
        }
        // One callout per distinct label, the soonest first.
        var y = CalloutTop;
        foreach (var group in calls.GroupBy(c => c.Label).OrderBy(g => g.Min(c => c.TicksLeft)))
        {
            var soonest = group.Min(c => c.TicksLeft);
            var text = FormattableString.Invariant($"{group.First().Text}  {soonest / (double)Tick.TickHz:0.0}s");
            var w = HudNodes.Width(text, CalloutFont);
            var color = group.Key == "SPREAD" ? HudNodes.Rgb(0xcc79a7) : HudNodes.Rgb(0x56b4e9);
            DrawRect(new Rect2(((Size.X - w) / 2) - 10, y, w + 20, CalloutFont + 10), HudNodes.Back);
            HudNodes.Text(this, new Vector2((Size.X - w) / 2, y + CalloutFont), text, CalloutFont, color);
            y += CalloutFont + 14;
        }
    }

    private void OnRunStarted() => _seen.Clear();

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        foreach (var call in PingCalls.Calls(run))
        {
            if (_seen.Add(call.Key))
            {
                HudNodes.Proof(_driver, call.LogLine());
            }
        }
    }
}
