using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>What one run is created from, on top of its <see cref="HostRunSetup"/> (review R7, slice S9).</summary>
/// <param name="Seed">The run seed (<c>--seed</c>).</param>
/// <param name="Registry">The content the setup was built from (<see cref="HostRunSetup.StartFor"/> reads its Facets).</param>
/// <param name="HumanClassId">The human seat's class (the setup's <see cref="HostRunSetup.HumanClassId"/>).</param>
public sealed record RunRequest(long Seed, C.ContentRegistry Registry, string HumanClassId)
{
    /// <summary>The queued key and Clause, or null for the CLI run (<c>--key</c>, no Clause).</summary>
    public QueueRequest? Queued { get; init; }

    /// <summary>The screen's progression (equipped gear, keystone), or null when no screen is hooked.</summary>
    public Progression? Progression { get; init; }

    /// <summary>1-based pull the run starts at (<c>--pull N</c>); a later start drops the earlier pulls and engages the
    /// first remaining one at creation (<see cref="RunRoot.EngageStartPull"/>).</summary>
    public int StartPull { get; init; } = 1;

    /// <summary>
    /// Whether the bot seats wear their default gear (<see cref="HostRunSetup.WithBotLoadouts"/>, BIBLE s6 parity). True
    /// for the host. Keyrun.Playtest passes false and an absent options record, as it built its runs before this factory
    /// existed, so its summaries and parity tests are unchanged; giving the playtest bots their gear is a separate,
    /// behaviour-changing slice.
    /// </summary>
    public bool BotLoadouts { get; init; } = true;

    /// <summary>
    /// Whether the run is created with an options record carrying the setup's rotation index (the host) or with the
    /// start's options as they are, null included (Keyrun.Playtest before this factory; see <see cref="BotLoadouts"/>).
    /// </summary>
    public bool RotationOptions { get; init; } = true;

    /// <summary>
    /// <c>--talent SEAT:TALENT</c> picks (finding f-a3de0a01), in flag order: each seat, bot or human, plays with that
    /// talent (<see cref="SeatTalentPatch"/>). Empty (the default) leaves every run exactly as before.
    /// </summary>
    public IReadOnlyList<SeatTalent> Talents { get; init; } = Array.Empty<SeatTalent>();
}

/// <summary>A created run and the pieces of its assembly the host logs.</summary>
/// <param name="Run">The created run, boss armed.</param>
/// <param name="Start">The run start (options and the human's gear, <see cref="HostRunSetup.StartFor"/>).</param>
/// <param name="Party">The placed party the run was created with (<see cref="HostRunSetup.PartyFor"/>).</param>
/// <param name="Dungeon">The dungeon the run was created with (the setup's, or its <c>--pull N</c> slice).</param>
public sealed record HostRun(Run Run, RunStart Start, IReadOnlyList<PartyMemberSpec> Party, DungeonDef Dungeon);

/// <summary>
/// The one way a host run is assembled (review R7): SimDriver.StartRun, Keyrun.Playtest's PlaytestRunner and the tests
/// all call <see cref="Create"/>, so a new assembly step lands in one place. Godot-free, so it lives in the
/// HostCore assembly (S4) beside HostRunSetup.
/// </summary>
public static class HostRunFactory
{
    /// <summary>
    /// Creates a run over <paramref name="setup"/> (step 1, <see cref="HostRunSetup.Build"/>, is the caller's: SimDriver
    /// keeps its setup across runs and rebuilds it only when the dungeon, key or class changes). Then, in order:
    /// <see cref="HostRunSetup.StartFor"/>, <see cref="HostRunSetup.WithBotLoadouts"/>, <see cref="RunRoot.CreateRun"/>,
    /// <see cref="RunRoot.EngageStartPull"/> for a <c>--pull N</c> start, and <see cref="BossRules.Arm"/> with
    /// <see cref="HostRunSetup.Boss"/>.
    /// </summary>
    /// <param name="setup">The run's setup.</param>
    /// <param name="req">The seed, the queued request and the rest of what the run is created from.</param>
    /// <returns>The created run with its start, party and dungeon.</returns>
    /// <exception cref="ArgumentException"><see cref="RunRequest.StartPull"/> is past the dungeon's pulls.</exception>
    public static HostRun Create(HostRunSetup setup, RunRequest req)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(req);
        var dungeon = setup.Dungeon;
        // --pull N counts the dungeon's pulls in route order, segment boss pulls included (setup.Dungeon.Pulls is that order).
        if (req.StartPull < 1 || req.StartPull > dungeon.Pulls.Count)
        {
            throw new ArgumentException(
                $"--pull {req.StartPull} is out of range: the dungeon's pulls are numbered 1..{dungeon.Pulls.Count} in route order (segment boss pulls included)");
        }
        if (req.StartPull > 1)
        {
            dungeon = dungeon with { Pulls = dungeon.Pulls.Skip(req.StartPull - 1).ToList() };
        }
        // Lane NAV-CORE: a --pull N run starts its party in pull N's room (HostRunSetup.PartyFor), else in the Entrance.
        // Finding f-f9b80112 (BIBLE s7): the screen's equipped gear, keystone included, reaches the run on top of the
        // request's Clause and talents (RunStart.Of; no screen yet means the request's options alone).
        // f-a3de0a01: --talent picks resolve first (a bad seat or talent stops the launch); the human seat's rides the
        // queued loadout so RunStart.Of stacks its Facets on it, as on a screen pick.
        var talents = SeatTalentPatch.Resolve(setup, req.Registry, req.Talents);
        var start = setup.StartFor(talents is null ? req.Queued : talents.WithHuman(req.Queued), req.Progression, req.Registry, req.HumanClassId);
        // The run's options record the weekly rotation index the setup was built with (the sim reads it as an input).
        var options = req.RotationOptions ? (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex } : start.Options;
        // Bot seats wear their default gear (HostRunSetup.BotLoadouts, BIBLE s6 parity) next to the human's loadouts.
        var party = setup.PartyFor(dungeon);
        if (req.BotLoadouts)
        {
            options = setup.WithBotLoadouts(options, party) ?? options;
        }
        // f-a3de0a01: a bot seat's --talent replaces its carried ability (its default gear stacked on when bots wear it).
        if (talents is not null)
        {
            options = talents.WithBots(options, req.BotLoadouts);
        }
        var created = RunRoot.CreateRun(req.Seed, party, dungeon, setup.Content, options);
        if (req.StartPull > 1)
        {
            // A --pull N start begins in pull N's fight (NavLayouts.PlaceParty; STATE.md D27 spawned it at creation).
            // PRESPAWN-AGGRO leaves that pull idle with the party at its room start, outside the aggro ring, so an idle
            // human never fights (HudSceneTest runs I and K1 never wiped): engage it here, the host's explicit fight start.
            created = RunRoot.EngageStartPull(created);
        }
        // 3F's hand-off (its decision 6): arm the boss on the created run's encounter.
        // R-MULTIBOSS-1: a multi-segment dungeon arms all its bosses (the first now, each later one when the one before dies);
        // a one-boss dungeon arms exactly as before.
        var run = created with
        {
            Encounter = setup.NextBosses.Count == 0
                ? BossRules.Arm(created.Encounter, setup.Boss)
                : BossRules.ArmAll(created.Encounter, new[] { setup.Boss }.Concat(setup.NextBosses).ToList()),
        };
        return new HostRun(run, start, party, dungeon);
    }
}
