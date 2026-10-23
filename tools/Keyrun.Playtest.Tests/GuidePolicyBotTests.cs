using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots;
using Keyrun.Bots.Guides;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-GUIDE-1 (event store seq 80; G2.4): a guided plan's Warden seat plays the authored guide
/// (game/content/data/guides/warden.json) through PolicyBot, a blind plan plays RoleSet.Default, and every run
/// Summary carries firstKickTick and firstFullRotationTick. RED on main: PolicyBot never reads Mode or a guide,
/// and summary.json has neither field.
/// </summary>
public sealed class GuidePolicyBotTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 2400;
    private const int MinWardenCasts = 3;
    private const string GuidedPlan = "{\"mode\":\"guided\"}";
    private const string BlindPlan = "{\"mode\":\"blind\"}";

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private sealed class CollectingSink : IBotReceiptSink
    {
        public List<BotReceipt> Receipts { get; } = new();

        public void Record(BotReceipt receipt) => Receipts.Add(receipt);
    }

    private sealed class Captured
    {
        public PlaytestOptions Options { get; set; } = null!;

        public PlaytestResult Result { get; set; } = null!;

        public List<BotReceipt> Receipts { get; } = new();

        /// <summary>The bot view and the Warden seat at every tick the Warden issued a cast (pre-step state).</summary>
        public Dictionary<int, (BotView View, ActorState Self)> WardenViews { get; } = new();
    }

    private static string GuideText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "content", "data", "guides", "warden.json")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "game/content/data/guides/warden.json not found above the test binary");
        return File.ReadAllText(Path.Combine(dir!.FullName, "game", "content", "data", "guides", "warden.json"));
    }

    private static GuideSet Guides() => GuideSet.Parse(GuideText(), Registry.Abilities.Keys.ToHashSet(StringComparer.Ordinal));

    private static Captured Drive(string plan)
    {
        var policy = PolicyReader.Parse(plan, Registry);
        var kits = KitRegistry.From(Registry);
        var sink = new CollectingSink();
        var captured = new Captured();
        var options = new PlaytestOptions(Key, Seed, policy)
        {
            HumanClassId = "warden",
            MaxTicks = MaxTicks,
            Receipts = sink,
            Observe = (tick, run, commands) =>
            {
                var wardenCast = commands.OfType<CastCommand>().Any(c => run.State.Actors.TryGetValue(c.ActorId, out var a) && a.ClassId == "warden");
                if (!wardenCast)
                {
                    return;
                }
                var self = run.State.Actors.Values.First(a => a.ClassId == "warden");
                var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits)
                    with { Layout = run.Content.Layout };
                captured.WardenViews[tick] = (view, self);
            },
        };
        captured.Options = options;
        captured.Result = PlaytestRunner.Play(options, Registry);
        captured.Receipts.AddRange(sink.Receipts);
        return captured;
    }

    private static List<BotReceipt> WardenCasts(Captured run) =>
        run.Receipts.Where(r => r.Class == "warden" && !string.IsNullOrEmpty(r.Ability)).OrderBy(r => r.Tick).ToList();

    [Fact(DisplayName = "Guided plan: a Warden cast carries a warden.json entry whose when holds at that tick, and is RoleSet.Guided's choice")]
    public void Guided_plan_casts_the_guide_entry_whose_condition_holds()
    {
        var guides = Guides();
        var run = Drive(GuidedPlan);
        var casts = WardenCasts(run);
        Assert.True(casts.Count >= MinWardenCasts, $"vacuous run: only {casts.Count} Warden casts");

        var rotation = guides.Require("warden").Rotation;
        var mismatches = new List<string>();
        foreach (var cast in casts)
        {
            if (!run.WardenViews.TryGetValue(cast.Tick, out var seen))
            {
                mismatches.Add($"tick {cast.Tick} {cast.Ability}: no bot view captured");
                continue;
            }
            const string prefix = "guide:warden:";
            if (!cast.Reason.StartsWith(prefix, StringComparison.Ordinal))
            {
                mismatches.Add($"tick {cast.Tick} {cast.Ability}: reason '{cast.Reason}' is not a warden.json entry");
                continue;
            }
            var ruleId = cast.Reason.Substring(prefix.Length);
            var rule = rotation.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null)
            {
                mismatches.Add($"tick {cast.Tick}: reason names rule '{ruleId}', which warden.json does not contain");
                continue;
            }
            var kit = seen.View.Kits.ResolveKit(seen.Self.ClassId, seen.View.ClassKit);
            if (!rule.When(new GuideContext(seen.View, seen.Self, kit)))
            {
                mismatches.Add($"tick {cast.Tick}: rule '{ruleId}' was cast but its when condition does not hold");
            }
            var expected = RoleSet.Guided(guides).Tank?.Decide(seen.View, seen.Self).OfType<CastCommand>().FirstOrDefault();
            if (expected?.AbilityId != cast.Ability)
            {
                mismatches.Add($"tick {cast.Tick}: cast {cast.Ability}, RoleSet.Guided chooses {expected?.AbilityId ?? "nothing"}");
            }
        }
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {casts.Count} Warden casts differ from warden.json:\n" + string.Join("\n", mismatches.Take(20)));
    }

    [Fact(DisplayName = "Blind plan: no Warden cast reads a guide, and the Warden's receipts differ from the guided plan's on at least one tick")]
    public void Blind_plan_plays_role_set_default_and_differs_from_guided()
    {
        var guided = WardenCasts(Drive(GuidedPlan));
        var blind = WardenCasts(Drive(BlindPlan));
        Assert.True(guided.Count >= MinWardenCasts, $"vacuous run: only {guided.Count} guided Warden casts");
        Assert.True(blind.Count >= MinWardenCasts, $"vacuous run: only {blind.Count} blind Warden casts");
        Assert.DoesNotContain(blind, r => r.Reason.StartsWith("guide:", StringComparison.Ordinal));
        Assert.False(
            guided.Select(r => (r.Tick, r.Ability, r.Reason)).SequenceEqual(blind.Select(r => (r.Tick, r.Ability, r.Reason))),
            "guided and blind Warden receipts are identical: PolicyBot ignores the plan's mode");
    }

    /// <summary>The ticks of every interrupt the run's party seats landed, read from the run log's sim events.</summary>
    private static List<int> SeatInterruptTicks(Captured run)
    {
        var seats = run.Result.Setup.Party.Select(p => p.Id).ToHashSet();
        var ticks = new List<int>();
        foreach (var raw in run.Result.Log.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("E {", StringComparison.Ordinal))
            {
                continue;
            }
            using var doc = JsonDocument.Parse(line.Substring(2));
            var e = doc.RootElement;
            if (!e.TryGetProperty("type", out var type) || type.GetString() != "interrupt")
            {
                continue;
            }
            if (e.TryGetProperty("sourceId", out var source) && seats.Contains(source.GetInt32()))
            {
                ticks.Add(e.GetProperty("tick").GetInt32());
            }
        }
        return ticks;
    }

    [Theory(DisplayName = "Summary: carries firstKickTick (first seat interrupt, null when none) and firstFullRotationTick (integer)")]
    [InlineData("{\"mode\":\"guided\"}")]
    [InlineData("{\"mode\":\"blind\"}")]
    public void Summary_carries_first_kick_and_first_full_rotation(string plan)
    {
        var run = Drive(plan);
        var json = Summary.ToJson(run.Result, run.Options, new SummaryHeader(null, null, "run.log", Key, Key, null));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("firstFullRotationTick", out var rotation), "summary.json has no firstFullRotationTick");
        Assert.Equal(JsonValueKind.Number, rotation.ValueKind);

        Assert.True(root.TryGetProperty("firstKickTick", out var kick), "summary.json has no firstKickTick");
        var kicks = SeatInterruptTicks(run);
        if (kicks.Count == 0)
        {
            Assert.True(kick.ValueKind == JsonValueKind.Null, $"firstKickTick is {kick} but the log has no seat interrupt");
        }
        else
        {
            Assert.True(kick.ValueKind == JsonValueKind.Number, $"firstKickTick is {kick} but the log has a seat interrupt at tick {kicks.Min()}");
            Assert.Equal(kicks.Min(), kick.GetInt32());
        }
    }
}
