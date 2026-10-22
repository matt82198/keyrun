using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// HUMAN-PLAY (D65/D66, Matt 2026-10-22 "I also want to be able to play any class, not just the warden"; "if you dont
/// know, defer to wow": a group is one of each role). The class picked on the Character tab is seat 1's class for the
/// run, the bots take the two other classes in roster order with their unchanged roles, and the default tutorial run
/// (Hollow Vault, key 2, seed 1) still ends with the boss dead whichever class seat 1 plays.
/// </summary>
public sealed class HumanClassSeatsTests(ITestOutputHelper output)
{
    private const string Dungeon = "hollow-vault";
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    // The screen's Queue as SimDriver.StartRun plays it: the class from the locked screen (Lobby.ClassFor), the setup at
    // the request's dungeon and key with the bot seats, then HostRunFactory.Create.
    private static (HostRunSetup Setup, Run Run) QueueAs(string classId)
    {
        var progression = Progression.Start(1);
        var lobby = Lobby.Open(Registry, progression, Dungeon, Registry.Clauses.Keys.ToList(), 2, 0, ScreenState.NoClauseStartKey)
            .SelectTab(LobbyTab.Character).PickClass(Registry, progression, classId);
        var (locked, request) = lobby.Queue(Registry, progression);
        Assert.NotNull(request);
        var picked = Lobby.ClassFor(request, locked);
        var setup = HostRunSetup.Build(Registry, request!.DungeonId, request.KeyLevel, picked, withBots: true);
        var run = HostRunFactory.Create(setup, new RunRequest(1, Registry, picked) { Queued = request, Progression = progression }).Run;
        return (setup, run);
    }

    [Theory(DisplayName = "Queue as any class: seat 1 plays it and the bots take the two other classes, one of each role")]
    [InlineData("warden", ActorRole.Tank, "chanter", "cinder")]
    [InlineData("chanter", ActorRole.Healer, "warden", "cinder")]
    [InlineData("cinder", ActorRole.Damage, "warden", "chanter")]
    public void Picked_class_is_seat_one_and_bots_fill_the_rest(string classId, ActorRole role, string bot2, string bot3)
    {
        var (setup, run) = QueueAs(classId);

        var human = run.State.Actors[HostRunSetup.HumanId];
        Assert.Equal(classId, human.ClassId);
        Assert.Equal(role, human.Role);
        Assert.Equal(classId, setup.HumanClassId);
        Assert.Equal(new[] { 2, 3 }, setup.BotActorIds);
        Assert.Equal(new[] { bot2, bot3 }, setup.BotActorIds.Select(id => run.State.Actors[id].ClassId));
        var players = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).ToList();
        Assert.Equal(new[] { ActorRole.Tank, ActorRole.Healer, ActorRole.Damage }.OrderBy(r => r), players.Select(a => a.Role).OrderBy(r => r));
        // The human's keys are the picked class's kit (PlayerInput reads Setup.Slots).
        Assert.Equal(HostRunSetup.ResolveSlots(Registry.Classes[classId]).Select(s => s.AbilityId), setup.Slots.Select(s => s.AbilityId));
    }

    [Theory(DisplayName = "A Chanter or Cinder human starts in front with every bot at its travel slot behind (no Warden walking back through seat 1)")]
    [InlineData("chanter")]
    [InlineData("cinder")]
    public void Non_tank_human_leads_from_the_front(string classId)
    {
        var (setup, run) = QueueAs(classId);
        var human = run.State.Actors[HostRunSetup.HumanId];
        foreach (var id in setup.BotActorIds)
        {
            var bot = run.State.Actors[id];
            var slot = Registry.Layout.Formation.Single(s => s.ClassId == bot.ClassId);
            Assert.Equal(new Vec3(human.Pos.X - slot.BehindMm, human.Pos.Y, human.Pos.Z + slot.SideMm), bot.Pos);
            Assert.True(bot.Pos.X < human.Pos.X, $"bot {bot.ClassId} at x={bot.Pos.X} is not behind the human at x={human.Pos.X}");
        }
    }

    [Fact(DisplayName = "A Warden human's party starts exactly where it always did (roster spots)")]
    public void Warden_party_is_unchanged()
    {
        var party = HostRunSetup.Build(Registry, Dungeon, 2, "warden", withBots: true).Party;
        var roster = NavLayoutsPlace(HostRunSetup.BuildParty(Registry, "warden", withBots: true));
        Assert.Equal(roster.Select(p => (p.Id, p.Pos)), party.Select(p => (p.Id, p.Pos)));
        Assert.Same(party, HostRunSetup.InFormation(Registry.Layout, party));
    }

    private static System.Collections.Generic.IReadOnlyList<PartyMemberSpec> NavLayoutsPlace(System.Collections.Generic.IReadOnlyList<PartyMemberSpec> party) =>
        Keyrun.Bots.Shared.NavLayouts.PlaceParty(Registry.Layout, party, Registry.Layout.Boxes.First(b => b.Pull is not null).Pull!);

    [Theory(DisplayName = "The tutorial run (key 2, seed 1) kills the boss with seat 1 as each class (bots on every seat, headless)")]
    [InlineData("warden")]
    [InlineData("chanter")]
    [InlineData("cinder")]
    public void Tutorial_completes_as_each_class(string classId)
    {
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null) { HumanClassId = classId }, Registry);
        var run = result.Run;
        var outcome = Summary.OutcomeOf(result);
        output.WriteLine($"{classId}: result=\"{outcome.Label}\" clear={run.State.RunTimerTicks / Summary.TicksPerSecond}s par={run.Encounter.ParTicks / Summary.TicksPerSecond}s deaths={result.Stats.Deaths.Count}");
        Assert.Equal(classId, run.State.Actors[HostRunSetup.HumanId].ClassId);
        Assert.NotNull(run.RunEnd.Result);
        Assert.True(run.Encounter.PullIndex >= run.Dungeon.Pulls.Count, $"{classId}: the run ended at pull {run.Encounter.PullIndex} of {run.Dungeon.Pulls.Count} (\"{outcome.Label}\"), the boss is not dead");
    }
}
