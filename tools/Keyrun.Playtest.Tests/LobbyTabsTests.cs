using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// BIBLE s1 "Screen -> Queue -> Dungeon (timed, one boss) -> End-of-run choice of three -> Screen", as one screen
/// with tabs (Matt 2026-10-20: "just one screen with tabs should be enough before entering a game/queue"). The pure
/// <see cref="Lobby"/> is the screen's selection state: the tab (Character, Gear, Build, Key, Queue), the class, the class's talent
/// build and the Key band. These tests drive it the way the Godot Screen does and follow its request through the same
/// host calls SimDriver.StartRun makes (<see cref="Lobby.SetupFor"/>, <see cref="HostRunSetup.StartFor"/>,
/// <see cref="RunRoot.CreateRun"/>), then end that real run and come back to the screen.
/// </summary>
public sealed class LobbyTabsTests
{
    private const string Dungeon = "hollow-vault";

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static Lobby Fresh(Progression? p = null, int key = 2) =>
        Lobby.Open(Registry, p ?? Progression.Start(1), Dungeon, Registry.Clauses.Keys.ToList(), key, 0, ScreenState.NoClauseStartKey);

    [Fact(DisplayName = "Lobby: opens on Character as the Warden, unlocked, at the asked key")]
    public void Opens_on_character_as_the_default_class()
    {
        var lobby = Fresh(key: 4);
        Assert.Equal(LobbyTab.Character, lobby.Tab);
        Assert.Equal("warden", lobby.ClassId);
        Assert.Equal("warden", lobby.Talents.ClassId);
        Assert.Equal(4, lobby.Key.KeyLevel);
        Assert.False(lobby.Locked);
    }

    [Fact(DisplayName = "Lobby: five tabs in order, cycling wraps, switching tabs changes nothing else")]
    public void Tabs_cycle_and_wrap_without_touching_the_selection()
    {
        Assert.Equal(new[] { LobbyTab.Character, LobbyTab.Gear, LobbyTab.Build, LobbyTab.Key, LobbyTab.Queue }, Lobby.Tabs);
        var lobby = Fresh().PickClass(Registry, Progression.Start(1), "chanter").StepKey(3);
        var c = lobby.SelectTab(LobbyTab.Queue).SelectTab(LobbyTab.Character);
        Assert.Equal(LobbyTab.Character, c.Tab);
        Assert.Equal(LobbyTab.Gear, c.CycleTab(1).Tab);
        Assert.Equal(LobbyTab.Build, c.CycleTab(2).Tab);
        Assert.Equal(LobbyTab.Key, c.CycleTab(3).Tab);
        Assert.Equal(LobbyTab.Queue, c.CycleTab(4).Tab);
        Assert.Equal(LobbyTab.Character, c.CycleTab(5).Tab);
        Assert.Equal(LobbyTab.Queue, c.CycleTab(-1).Tab);
        foreach (var t in Lobby.Tabs)
        {
            var moved = lobby.SelectTab(t);
            Assert.Equal("chanter", moved.ClassId);
            Assert.Equal(5, moved.Key.KeyLevel);
            Assert.Same(lobby.Talents, moved.Talents);
        }
        Assert.Same(lobby, lobby.SelectTab((LobbyTab)99));
    }

    [Fact(DisplayName = "Lobby: the class list is the three shipped classes in roster order")]
    public void Class_list_is_the_roster()
    {
        Assert.Equal(new[] { "warden", "chanter", "cinder" }, Lobby.ClassIds(Registry));
    }

    [Fact(DisplayName = "Lobby: picking a class swaps the build to that class's saved picks; unknown or queued is refused")]
    public void Picking_a_class_restores_that_class_build()
    {
        var cinder = Registry.Classes["cinder"];
        var fork0 = cinder.Talents[0];
        var saved = Progression.Start(1).WithTalents(new[]
        {
            new KeyValuePair<string, string>(fork0.AbilityId, fork0.Options[1].Id),
            new KeyValuePair<string, string>("warden.strike", "rend"),
        });
        var lobby = Fresh(saved);
        Assert.Equal(1, lobby.Talents.Count); // the Warden's own pick, restored on open

        var picked = lobby.PickClass(Registry, saved, "cinder");
        Assert.Equal("cinder", picked.ClassId);
        Assert.Equal("cinder", picked.Talents.ClassId);
        Assert.Equal(fork0.Options[1].Id, picked.Talents.PickedOption(0));
        Assert.Equal(1, picked.Talents.Count);

        Assert.Same(picked, picked.PickClass(Registry, saved, "rogue"));
        var (queued, _) = picked.Queue(Registry, saved);
        Assert.Same(queued, queued.PickClass(Registry, saved, "warden"));
    }

    [Fact(DisplayName = "Lobby: saving a build keeps the other classes' saved picks")]
    public void Saving_one_class_keeps_the_others()
    {
        var saved = Progression.Start(1).WithTalents(new[] { new KeyValuePair<string, string>("warden.strike", "rend") });
        var lobby = Fresh(saved).PickClass(Registry, saved, "cinder");
        lobby = lobby.WithTalents(lobby.Talents.Pick(0, 0));
        var after = lobby.SaveTalents(saved);
        Assert.Equal("rend", after.Talents["warden.strike"]);
        Assert.Equal(lobby.Talents.Forks[0].Options[0].Id, after.Talents[lobby.Talents.Forks[0].AbilityId]);
        Assert.Equal(2, after.Talents.Count);

        // A build of another class is not this lobby's build.
        Assert.Same(lobby, lobby.WithTalents(TalentPicks.Start(Registry.Classes["warden"])));
    }

    [Fact(DisplayName = "Queue: the chosen class, its talents, the key and the equipped gear reach the created run")]
    public void Class_talents_key_and_gear_flow_into_the_run()
    {
        var facet = Registry.Facets.Values.Where(f => f.Id.StartsWith("cinder.weapon.", StringComparison.Ordinal))
            .OrderBy(f => f.Id, StringComparer.Ordinal).First();
        var progression = Progression.Start(1).Take(new Item("reward-weapon-t6-0", "weapon", 6, new Facet(facet.Id, facet.Text, facet.AbilityId), Rewards.StartingCharges));

        var lobby = Fresh(progression).SelectTab(LobbyTab.Character).PickClass(Registry, progression, "cinder");
        lobby = lobby.SelectTab(LobbyTab.Build);
        lobby = lobby.WithTalents(lobby.Talents.Pick(0, 0));
        lobby = lobby.SelectTab(LobbyTab.Key).StepKey(5);
        var talented = lobby.Talents.Forks[0].AbilityId;

        var (locked, request) = lobby.Queue(Registry, progression);
        Assert.True(locked.Locked);
        Assert.NotNull(request);
        Assert.Equal("cinder", locked.ClassId);
        Assert.Equal(7, request!.KeyLevel);
        Assert.Equal(Dungeon, request.DungeonId);
        Assert.Null(locked.Queue(Registry, progression).Request); // one press, one request

        // SimDriver.StartRun's calls, in order: the class comes from the (locked) screen, never from the payload.
        var classId = Lobby.ClassFor(request, locked);
        Assert.Equal("cinder", classId);
        var setup = Lobby.SetupFor(Registry, request, classId);
        var start = setup.StartFor(request, progression, Registry, classId);
        var run = RunRoot.CreateRun(7, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);

        var human = run.State.Actors[setup.HumanActorId];
        Assert.Equal("cinder", human.ClassId);
        Assert.Equal(new[] { "warden", "chanter" }, setup.BotActorIds.Select(id => run.State.Actors[id].ClassId).OrderBy(c => c == "chanter").ToArray());
        Assert.Equal(HostRunSetup.ResolveSlots(Registry.Classes["cinder"]).Select(s => s.AbilityId), setup.Slots.Select(s => s.AbilityId));
        Assert.Equal(7, run.Encounter.KeyLevel);

        var stats = GearItems.StatsAt(Registry.Gear, GearSlot.Weapon, 6);
        Assert.True(stats.DamageDoneBp != 0 || stats.DamageTakenBp != 0);
        Assert.Equal(stats.DamageDoneBp, human.GearDamageDoneBp);
        Assert.Equal(stats.DamageTakenBp, human.GearDamageTakenBp);
        Assert.Contains(talented, request.Loadouts![setup.HumanActorId].Keys);
        Assert.Equal(new[] { facet.Id }, start.Gear!.Facets.Select(f => f.Id));
    }

    [Fact(DisplayName = "Queue: an unchosen class queues the Warden; a CLI run or a run with no screen plays the Warden")]
    public void Default_class_is_the_warden()
    {
        var (locked, request) = Fresh().Queue(Registry, Progression.Start(1));
        Assert.Equal("warden", Lobby.ClassFor(request, locked));
        Assert.Equal("warden", Lobby.SetupFor(Registry, request!, Lobby.ClassFor(request, locked)).HumanClassId);
        var cinder = Fresh().PickClass(Registry, Progression.Start(1), "cinder");
        Assert.Equal("warden", Lobby.ClassFor(null, cinder)); // the CLI / --solo restart has no request
        Assert.Equal("warden", Lobby.ClassFor(new QueueRequest(Dungeon, 2, "echo"), null));
        Assert.Equal("cinder", Lobby.SetupFor(Registry, request!, "cinder").HumanClassId);
    }

    [Fact(DisplayName = "Queue: the class never rides the queue payload (the queue matches on key only, BIBLE s3)")]
    public void The_class_is_not_in_the_queue_payload()
    {
        var p = Progression.Start(1);
        var (_, asWarden) = Fresh().Queue(Registry, p);
        var (_, asCinder) = Fresh().PickClass(Registry, p, "cinder").Queue(Registry, p);
        Assert.Equal(asWarden!.DungeonId, asCinder!.DungeonId);
        Assert.Equal(asWarden.KeyLevel, asCinder.KeyLevel);
        Assert.Equal(asWarden.ClauseId, asCinder.ClauseId);
        Assert.DoesNotContain(typeof(QueueRequest).GetProperties(), prop => prop.Name.Contains("Class", StringComparison.Ordinal) && prop.Name != "ClauseId");
    }

    [Fact(DisplayName = "Run end: the choice of three is taken and the screen comes back unlocked on Character, class kept")]
    public void Run_end_returns_to_the_screen()
    {
        var progression = Progression.Start(1);
        var lobby = Fresh(progression).PickClass(Registry, progression, "chanter").StepKey(2);
        var (queued, request) = lobby.Queue(Registry, progression);
        var classId = Lobby.ClassFor(request, queued);
        var setup = Lobby.SetupFor(Registry, request!, classId);
        Assert.Equal("chanter", setup.HumanClassId);
        var run = RunRoot.CreateRun(3, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, setup.StartFor(request, progression, Registry, classId).Options);

        // A depleted end: every seat dies, and since R-BACKFILL-2 (f-47a7a6db) a wipe releases instead of ending the run,
        // so the run ends depleted at the timer cap. The clock is put one tick short of the cap (the time many deaths add).
        for (var i = 0; i < 20; i++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
        }
        foreach (var id in setup.Party.Select(p => p.Id))
        {
            run.State.Actors[id].Alive = false;
            run.State.Actors[id].Health = 0;
        }
        run = run with { Encounter = run.Encounter with { PenaltyTicks = (int)(Timer.CapTicks(run.Encounter) - run.State.Tick - 1) } };
        for (var i = 0; i < RunWiring.ReleaseDelayTicks + 5 && run.RunEnd.Result is null; i++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
        }
        Assert.Equal(RunResult.Depleted, run.RunEnd.Result);

        // The end-of-run choice of three over the chanter's own pools, then the pick lands on the character.
        // (EndOfRun.Offer was removed by R-ROLL-1; this fact needs a card to take, so it draws the Rewards primitive's.)
        var offer = Rewards.RollRewardChoices(EndOfRun.RewardSeed(run), run.Encounter.KeyLevel, run.RunEnd.Result!.Value, Rewards.GearSlots, Lobby.FacetPools(Registry, "chanter"), progression.Equipped);
        Assert.Equal(3, offer!.Count);
        Assert.All(offer, card => Assert.StartsWith("chanter.", card.Facet.Id, StringComparison.Ordinal));
        progression = progression.BankRun(run.RunEnd.Result!.Value).Take(offer[0]);

        var back = queued.SelectTab(LobbyTab.Queue).ReturnFrom(run);
        Assert.False(back.Locked);
        Assert.Equal(LobbyTab.Character, back.Tab);
        Assert.Equal("chanter", back.ClassId);
        Assert.Equal(KeyMove.ForRun(run)!.To, back.Key.KeyLevel);
        Assert.Equal(4, back.Key.KeyLevel); // a depleted key keeps its level (BIBLE s8)

        // And the next Queue, still as the Chanter, carries the taken item's tier.
        var (requeued, next) = back.Queue(Registry, progression);
        Assert.NotNull(next);
        Assert.Equal("chanter", Lobby.ClassFor(next, requeued));
        Assert.Single(progression.Equipped);
        var taken = GearItems.StatsAt(Registry.Gear, Enum.Parse<GearSlot>(offer[0].Slot, ignoreCase: true), offer[0].Tier);
        Assert.Equal(taken.DamageDoneBp, next!.Gear![setup.HumanActorId].DamageDoneBp);
        Assert.Equal(taken.DamageTakenBp, next.Gear[setup.HumanActorId].DamageTakenBp);
    }

    [Fact(DisplayName = "Run end: a live run is not an end, the screen stays as it is")]
    public void A_live_run_does_not_return()
    {
        var (queued, request) = Fresh().Queue(Registry, Progression.Start(1));
        var setup = Lobby.SetupFor(Registry, request!, Lobby.ClassFor(request, queued));
        var run = RunRoot.CreateRun(3, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, request!.ToRunOptions());
        Assert.Same(queued, queued.ReturnFrom(run));
    }
}
