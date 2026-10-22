using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-b24741e2: every surface that prints a player ability to the player (the cast bar, the run log, the party and unit
/// frames, the pre-run kit list, the talent panel) asks the one resolver <see cref="AbilityNames.Display"/> through
/// Keyrun.Host.Names.Ability / SimDriver.AbilityName, so the real default run's player casts and the pre-run kit list carry
/// the ability-text.json display name and never the raw id or the old content name. The Godot node files (CastBarView,
/// PartyFrames, UnitFrames, Screen.TabCharacter, TalentPanel, RunLog) cannot run headless; these tests drive the resolver on
/// the exact ids those files pass it (the ids a real run casts, the slots the pre-run screen lists).
/// </summary>
public sealed class AbilityNamesEverywhereTests
{
    private const long Seed = 1;
    private const int WardenId = 1;
    private const string DungeonId = "hollow-vault";

    [Fact(DisplayName = "the real default run: every ability the human casts resolves to a display name, never the id or the old content name")]
    public void Real_run_player_casts_carry_the_display_name()
    {
        var registry = ContentLoader.Load();
        var table = AbilityTextLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, 2, Lobby.DefaultClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, Lobby.DefaultClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;
        var cast = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 400; i++)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            foreach (var e in step.Step.Events)
            {
                if ((e.Type == "cast_complete" || e.Type == "cast_start") && e.Get("actorId") is JsInt actor && actor.Value == WardenId &&
                    e.Get("abilityId") is JsString id)
                {
                    cast.Add(id.Value);
                }
            }
        }
        Assert.NotEmpty(cast);
        foreach (var id in cast)
        {
            var shown = Names.Ability(registry, id);
            Assert.Equal(table.For(id)!.DisplayName, shown);
            Assert.NotEqual(id, shown);
            Assert.NotEqual(registry.Abilities[id].Name, shown);
        }
    }

    [Fact(DisplayName = "the pre-run kit list (the slots Screen.TabCharacter draws) shows a display name for all nine slots of every class")]
    public void Pre_run_kit_list_shows_display_names()
    {
        var registry = ContentLoader.Load();
        var table = AbilityTextLoader.Load();
        foreach (var cls in registry.Classes.Values)
        {
            var slots = HostRunSetup.ResolveSlots(cls);
            Assert.Equal(9, slots.Count);
            foreach (var slot in slots)
            {
                var shown = Names.Ability(registry, slot.AbilityId);
                Assert.Equal(table.For(slot.AbilityId)!.DisplayName, shown);
                Assert.NotEqual(slot.AbilityId, shown);
            }
        }
    }

    [Fact(DisplayName = "the talent panel's fork rows (Screen.TalentAbilityName) read display names for every class's talent forks")]
    public void Talent_fork_rows_show_display_names()
    {
        var registry = ContentLoader.Load();
        foreach (var cls in registry.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                var shown = Names.Ability(registry, fork.AbilityId);
                Assert.NotEqual(fork.AbilityId, shown);
                Assert.NotEqual(registry.Abilities[fork.AbilityId].Name, shown);
            }
        }
    }
}
