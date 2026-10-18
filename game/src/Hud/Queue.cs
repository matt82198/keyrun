using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The Godot-side queue list (BIBLE s10 Queue band; ENGINE-PLAN 3G backfill). It holds every <see cref="QueueRequest"/>
/// the screen's Queue button raises (<see cref="Screen.Queued"/>), oldest first, and hands the backfill seam one
/// joiner for an open seat: <see cref="TakeFor"/> picks by the pure key band (<see cref="Backfill.PickJoiner"/>, exact
/// key 2-20), and <see cref="Consume"/> removes the requests a <see cref="Backfill.Step"/> reports as joined.
/// The node holds no rules of its own. Under <c>--autorun</c> it prints "QUEUE add|take|consume ..." proof lines.
/// </summary>
public partial class Queue : Node
{
    private readonly List<QueueRequest> _requests = new();
    private Screen? _screen;
    private SimDriver? _driver;

    /// <summary>The screen whose Queued event feeds this list (default: the parent Screen).</summary>
    [Export]
    public NodePath ScreenPath { get; set; } = new("..");

    /// <summary>The driver, for autorun proof lines.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The waiting requests, oldest first (the list <see cref="Backfill.Step"/> reads).</summary>
    public IReadOnlyList<QueueRequest> Requests => _requests;

    /// <inheritdoc />
    public override void _Ready()
    {
        _driver = HudNodes.FindDriver(this, DriverPath);
        _screen = GetNodeOrNull<Screen>(ScreenPath);
        if (_screen is not null)
        {
            _screen.Queued += Add;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_screen is not null)
        {
            _screen.Queued -= Add;
        }
    }

    /// <summary>Adds one request at the back of the queue.</summary>
    /// <param name="request">The request.</param>
    public void Add(QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);
        Proof(FormattableString.Invariant(
            $"QUEUE add dungeon={request.DungeonId} key={request.KeyLevel} clause={request.ClauseId} size={_requests.Count}"));
    }

    /// <summary>Removes and returns the first request the key band admits to a run at <paramref name="runKeyLevel"/>.</summary>
    /// <param name="runKeyLevel">The run's key.</param>
    /// <returns>The joiner, or null when nobody waiting matches the key.</returns>
    public QueueRequest? TakeFor(int runKeyLevel)
    {
        var index = Backfill.PickJoiner(runKeyLevel, _requests);
        if (index < 0)
        {
            Proof(FormattableString.Invariant($"QUEUE take key={runKeyLevel} none size={_requests.Count}"));
            return null;
        }
        var request = _requests[index];
        _requests.RemoveAt(index);
        Proof(FormattableString.Invariant($"QUEUE take key={runKeyLevel} index={index} size={_requests.Count}"));
        return request;
    }

    /// <summary>Removes the requests a <see cref="Backfill.Step"/> placed into seats.</summary>
    /// <param name="consumedIndices">The step's <see cref="BackfillStep.ConsumedQueueIndices"/> (indices into <see cref="Requests"/>).</param>
    public void Consume(IReadOnlyList<int> consumedIndices)
    {
        ArgumentNullException.ThrowIfNull(consumedIndices);
        foreach (var index in consumedIndices.Distinct().OrderByDescending(i => i))
        {
            if (index >= 0 && index < _requests.Count)
            {
                _requests.RemoveAt(index);
            }
        }
        if (consumedIndices.Count > 0)
        {
            Proof(FormattableString.Invariant($"QUEUE consume n={consumedIndices.Count} size={_requests.Count}"));
        }
    }

    private void Proof(string line)
    {
        if (_driver is not null)
        {
            HudNodes.Proof(_driver, line);
        }
    }
}
