using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G1.1 / G2.4 (R-EVENTSTORE-2, plan PER-PROJECT-EVENT-STORES.md sections 3.1 and 3.2, lane K1): every command a bot
/// issues carries exactly one <see cref="Reason"/> whose rule id is stable (<c>role:rule</c>, a key of
/// <see cref="Reasons.Catalog"/>), and reading the reasons never changes behaviour (K1-a: same Fingerprint with and
/// without a reason sink). The harness is the autorun host run at seed 1, key 2, hollow-vault, the way ChoirEngageTests
/// builds it (BotCommandSource seats 2 and 3, ScriptedWarden as seat 1); no bots-level autorun existed besides it.
/// A role that emits a command without a rule id fails <see cref="EveryBotCommandOfAnAutorunCarriesOneStableRuleId"/>
/// (the reason is empty), the chosen enforcement for requirement (c).
/// </summary>
public sealed class BotCommandReasonTests
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const int Ticks = 3000;
    private static readonly Regex RuleShape = new("^[a-z]+:[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private sealed record Outcome(ulong Fingerprint, long CommandCount, string ProofLine, List<(Command Command, Reason Reason)> Seen);

    private static Outcome Drive(bool readReasons)
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var seen = new List<(Command, Reason)>();
        while (run.State.Tick < Ticks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands, new List<int>());
            var humanCount = commands.Count;
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            if (readReasons)
            {
                // The sink: read each bot command's reason as the host would, after the batch and before the step.
                foreach (var c in commands.Skip(humanCount))
                {
                    seen.Add((c, Reasons.Of(c)));
                }
            }
            run = RunRoot.StepRun(run, commands).Run;
        }
        return new Outcome(bots.Fingerprint, bots.CommandCount, bots.ProofLine(), seen);
    }

    /// <summary>K1-a: reading the reasons leaves the BOTS proof line (count and hash) byte-identical.</summary>
    [Fact]
    public void ReasonSinkNeverChangesTheFingerprint()
    {
        var without = Drive(readReasons: false);
        var with = Drive(readReasons: true);
        Assert.True(without.CommandCount > 50, $"vacuous run: only {without.CommandCount} commands");
        Assert.Equal(without.ProofLine, with.ProofLine);
        Assert.Equal(without.Fingerprint, with.Fingerprint);
        Assert.Equal(with.CommandCount, with.Seen.Count);
    }

    /// <summary>(a) and (c): every command of the autorun has a catalog rule id of the <c>role:rule</c> shape and a short phrase.</summary>
    [Fact]
    public void EveryBotCommandOfAnAutorunCarriesOneStableRuleId()
    {
        var seen = Drive(readReasons: true).Seen;
        Assert.NotEmpty(seen);
        var untagged = seen.Where(s => string.IsNullOrEmpty(s.Reason.RuleId)).Select(s => s.Command.ToString()).Distinct().Take(5).ToList();
        Assert.True(untagged.Count == 0, "commands with no rule id: " + string.Join(" | ", untagged));
        foreach (var (command, reason) in seen)
        {
            Assert.Matches(RuleShape, reason.RuleId);
            Assert.True(Reasons.Catalog.ContainsKey(reason.RuleId), $"{reason.RuleId} is not in the catalog ({command})");
            Assert.False(string.IsNullOrWhiteSpace(reason.Why));
        }
        // The autorun exercises more than one decision site, so the check is not satisfied by one blanket tag.
        Assert.True(seen.Select(s => s.Reason.RuleId).Distinct().Count() >= 4);
    }

    /// <summary>Every command of a RunBots batch has a matching, index-aligned reason.</summary>
    [Fact]
    public void BatchReasonsAreAlignedWithCommands()
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, "warden", withBots: true);
        var party = setup.PartyFor(setup.Dungeon);
        var run = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, setup.StartFor(null, null, registry, "warden").Options ?? new RunOptions());
        var ids = setup.BotActorIds.ToList();
        var bots = ids.ToDictionary(id => id, _ => Bot.MakeBot(RoleSet.Default));
        IReadOnlyDictionary<int, BotMemory> memories = ids.ToDictionary(id => id, _ => BotMemory.Default(100));
        var kits = KitRegistry.From(registry);
        var prng = Skill.SeedPrng(Seed);
        var total = 0;
        for (var i = 0; i < 400 && run.RunEnd.Result is null; i++)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, memories, prng);
            Assert.Equal(batch.Commands.Count, batch.Reasons.Count);
            for (var k = 0; k < batch.Commands.Count; k++)
            {
                Assert.Equal(Reasons.Of(batch.Commands[k]), batch.Reasons[k]);
                Assert.True(batch.Reasons[k].IsTagged, $"untagged: {batch.Commands[k]}");
            }
            total += batch.Commands.Count;
            memories = batch.Memories;
            prng = batch.Prng;
            run = RunRoot.StepRun(run, batch.Commands.ToList()).Run;
        }
        Assert.True(total > 0);
    }

    /// <summary>The catalog is the only source of ids: free text is rejected, and every id has the shape and a short phrase.</summary>
    [Fact]
    public void TaggingAFreeTextIdIsRejectedAndTheCatalogIsWellFormed()
    {
        var cmd = new TargetCommand(0, 1, 2);
        Assert.Throws<ArgumentException>(() => Reasons.Tag(cmd, "taunt the healer because it was hurt"));
        Assert.Throws<ArgumentException>(() => Reasons.Tag(cmd, ""));
        Assert.False(Reasons.Of(new TargetCommand(0, 1, 2)).IsTagged);
        Assert.NotEmpty(Reasons.Catalog);
        foreach (var (id, why) in Reasons.Catalog)
        {
            Assert.Matches(RuleShape, id);
            Assert.False(string.IsNullOrWhiteSpace(why));
            Assert.True(why.Length <= 60, $"{id}: phrase too long");
        }
    }
}
