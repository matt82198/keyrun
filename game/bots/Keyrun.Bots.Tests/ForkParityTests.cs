using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// FORK-PARITY (BIBLE section 6, Matt 2026-10-20: "talents = keep per-ability forks + add a balance/dominance check").
/// The bible's parity check, as written: "the bots suite runs each fork's options through the same seeded Hollow Vault
/// runs at keys 2 and 10, measuring clear time, deaths and Combos landed. An option that is at least as good on every
/// measure, and strictly better on one, is a defect that blocks the content change, the same way a red test does."
/// </summary>
/// <remarks>
/// <para>Runs: three seeded Hollow Vault runs, each a bot oracle setup file and a seed (<see cref="Scenarios"/>), every
/// party actor a real-role bot exactly as <see cref="RolesOracleTraceTests.Drive"/>
/// builds them. For one option, every party member of the fork's class carries the option's talented ability (the
/// content patch applied by <see cref="C.TalentPicks.ApplyPatch"/>, the screen's own talent layer) as a
/// <see cref="RunOptions.Loadouts"/> entry; every other ability stays at base. The run steps until it ends or reaches
/// the scenario's maxTicks.</para>
/// <para>Measures, integers only, each one measured separately in each run (a "measure" is one quantity in one run, so
/// an option dominates only when it is at least as good in every run): clear time = the run_end tick (a run that never
/// ends scores maxTicks + 1, worse than any end); deaths = party "death" events (lower is better); Combos landed =
/// "combo" events (higher is better).</para>
/// <para>Dominance: option A dominates sibling B when A is at least as good as B on every measure and strictly better
/// on at least one. Every ordered pair of options of every fork is checked.</para>
/// <para>NOISE TOLERANCE (finding f-e3daed76, R-PILE-7): a dominance gates only when at least one of A's margins over B
/// EXCEEDS <see cref="NoiseTolerance.Measured"/>: clear 715 bp of the slower clear tick in that run, deaths 0, combos 3.
/// A dominance whose every margin is inside the tolerance is printed ("DOMINATED WITHIN NOISE") and recorded, never red:
/// the runs are chaotic, so any behaviour change moves every option's measures, and a balance oracle pinned to main's
/// exact dynamics went red on every behaviour PR (#307: hymn staccato dominated by 2 combos on equal clears; #362:
/// surge and discord). Why those numbers: the tolerance is one standard deviation of seed noise. At dial 90 the seed
/// decides every bot mistake, so each seed is a different trajectory through the same content, the same kind of
/// perturbation a behaviour PR makes; the hv runs this check uses (base kit 1407/0/43, 1478/0/42, 1855/1/59) all sit
/// inside that seed spread (key 2: clear 1402..1664, key 10: 1782..2238). Over seeds 1..12, for each of the 24 measured
/// sibling pairs the per-seed margin's standard deviation was taken; its median is clear 90.4 ticks (615 bp) at key 2
/// and 132.1 ticks (715 bp) at key 10, deaths 0.52 and 0.45, combos 3.59 and 3.85. The larger key, floored to each
/// measure's grain, is the tolerance (one death is beyond 0.52; four combos is beyond 3.85, three is not). A real
/// dominance still blocks: <see cref="ForkParityMarginsTests.A_deliberately_dominated_fork_is_red"/> drives a test-double
/// fork through the same failure path this check asserts on.</para>
/// <para>MARGINS FILE: every sibling pair's per-run measures, margins and verdict are recorded in
/// game/bots/Keyrun.Bots.Tests/ForkParity.margins.txt (format in its header). The check prints margin drift beyond the
/// tolerance since the recording but never fails on it; it fails only when the recorded pair set differs from the
/// measured one (a fork or option added, removed or renamed). RE-BASELINE (after a content retune, a fork change, or when
/// the drift lines say margins moved): run
/// <c>KEYRUN_FORKPARITY_REBASELINE=1 dotnet test game/bots/Keyrun.Bots.Tests --filter "FullyQualifiedName~ForkParity"</c>.
/// It rewrites the file and re-measures the noise sweep into its header (about a minute); commit the file in its own
/// commit and say why in the PR. If the re-measured paired-margin sd has moved past <see cref="NoiseTolerance.Measured"/>,
/// re-derive the tolerance by the rule above in the same PR, with the header as the evidence; never widen it to pass a
/// change.</para>
/// <para> Two siblings that measure identically on
/// every measure of every run are red too (finding f-8476dbf4): neither dominates, but a player cannot tell them apart,
/// so the fork is not a decision. A fork whose options all measure identically is the separate unmeasured case
/// (<see cref="UnmeasuredForks"/>).</para>
/// <para>Distinct runs (finding f-313fba9c): two scenarios that measure identically for every option of every fork are
/// one run counted twice, so the check is red until each scenario adds a run that differs. A dominated option must be listed in
/// <see cref="KnownDominatedForks"/> with a finding line, or the check is red; a listed option that is no longer
/// dominated is also red (the stale entry must be removed). Listed failures are printed on every run.</para>
/// </remarks>
public sealed class ForkParityTests
{
    private readonly ITestOutputHelper _output;

    public ForkParityTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The seeded Hollow Vault runs the bible names (keys 2 and 10): name, bot oracle setup file, seed (the run seed and
    /// the bot PRNG seed, one seed for both as the bot oracle traces use it), and the bots' skill dial.
    /// </summary>
    /// <remarks>
    /// The seed is live only below dial 100 (finding f-313fba9c). At dial 100 <see cref="Skill.MistakeChance"/> is 0, so
    /// the bots never draw from their PRNG, and the sim's own PRNG only breaks random script targets: the old
    /// bots-hv-k2-s7 and bots-hv-k2-s11 oracle traces are line-identical (both traceHash 13473efa), and seeds 1 to 40 at
    /// key 2 and 1 to 30 at key 10 all measured the same base run at dial 100. So the second key-2 run is a dial-90 party,
    /// where the seed decides each bot's mistakes; <see cref="The_dial_90_seed_is_live"/> proves seed 11 and seed 7 differ
    /// there, and the parity check's distinct-runs assertion proves this run differs from the other two for some option.
    /// </remarks>
    private static readonly (string Name, string SetupFile, long Seed, int Dial)[] Scenarios =
    {
        ("hv-k2-s7", "hollow-vault-k2.setup.json", 7, 100),
        ("hv-k2-s11-d90", "hollow-vault-k2.setup.json", 11, 90),
        ("hv-k10-s7", "hollow-vault-k10.setup.json", 7, 100),
    };

    /// <summary>
    /// KNOWN DOMINATED FORKS: "class.ability:dominatedOption" to the finding (which sibling dominates it, and the
    /// measures). Empty (finding f-4cda6f59): the options this check found dominated on origin/main 173a6a4
    /// (guardBreak:surge, flare:efficient, cadence:swift, cadence:resonance, cinderfield:flashpoint, bulwark:mobile)
    /// were retuned into sidegrades in the same change that landed it; detonate:cheapShot and hymn:staccato, dominated
    /// on the older main PR #291 measured, already measured undominated on 173a6a4. An entry would be a real content
    /// defect (BIBLE section 6: "a defect that blocks the content change"). Never add an entry to make a content change
    /// pass: retune the content.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownDominatedForks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
    };

    /// <summary>
    /// UNMEASURED FORKS: forks whose options all measure identically in every run, because the bots never cast the
    /// forked ability in these scenarios (the bot oracle traces carry no cast of it). The dominance check cannot see
    /// these forks at all, so they are named here, loudly, instead of passing silently. An entry is red once the fork
    /// becomes measurable (remove it), and a newly unmeasurable fork is red until it is listed.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> UnmeasuredForks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["warden.strike"] = "Rend, Riposte and Warcry all equal the base run: the bots never cast warden.strike.",
        ["cinder.emberBolt"] = "Instant, Piercing and Overcharge all equal the base run: the bots never cast cinder.emberBolt.",
        ["chanter.refrain"] = "Overture and Crescendo equal the base run: the bots never cast chanter.refrain.",
    };

    /// <summary>One run's measures.</summary>
    /// <param name="ClearTick">The run_end tick, or maxTicks + 1 when the run never ended (lower is better).</param>
    /// <param name="Deaths">Party deaths (lower is better).</param>
    /// <param name="Combos">Combos landed (higher is better).</param>
    /// <param name="Result">The run result ("timed", "depleted", or "none").</param>
    public sealed record RunMeasures(int ClearTick, int Deaths, int Combos, string Result)
    {
        /// <summary>Compact form for findings.</summary>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"clear {ClearTick} ({Result}) deaths {Deaths} combos {Combos}");
    }

    private sealed record Scenario(string Name, RunSetup Setup, long Seed, int Dial, int MaxTicks);

    private static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    private static readonly Lazy<KitRegistry> Kits = new(() => KitRegistry.From(Registry.Value));

    private static readonly Lazy<IReadOnlyList<Scenario>> Loaded = new(() => Scenarios.Select(LoadScenario).ToList());

    private static Scenario LoadScenario((string Name, string SetupFile, long Seed, int Dial) s) =>
        new(s.Name, RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), s.SetupFile), s.Seed), s.Seed, s.Dial, 24000);

    /// <summary>
    /// Runs <paramref name="scenario"/> with the bots, every member of <paramref name="classId"/> carrying
    /// <paramref name="talented"/> (null = no loadout, the base kit).
    /// </summary>
    private static RunMeasures Measure(Scenario scenario, string? classId, S.AbilityDef? talented)
    {
        var setup = scenario.Setup;
        var options = setup.Options ?? new RunOptions();
        if (classId is not null && talented is not null)
        {
            var loadouts = new Dictionary<int, IReadOnlyDictionary<string, S.AbilityDef>>();
            foreach (var m in setup.Party.Where(p => p.ClassId == classId))
            {
                loadouts[m.Id] = new Dictionary<string, S.AbilityDef>(StringComparer.Ordinal) { [talented.Id] = talented };
            }
            Assert.True(loadouts.Count > 0, $"{scenario.Name}: no party member of class {classId}");
            options = options with { Loadouts = loadouts };
        }
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, options);
        var partyIds = new HashSet<int>(setup.Party.Select(p => p.Id));
        var bots = new SortedDictionary<int, IBot>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            initial[p.Id] = BotMemory.Default(scenario.Dial);
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(scenario.Seed);
        var deaths = 0;
        var combos = 0;
        while (run.RunEnd.Result is null && run.State.Tick < scenario.MaxTicks)
        {
            // As the host builds it (game/src/Host/BotCommandSource.cs Sample): the bots read the run's own, talented
            // ability map, so a talent that reshapes an ability (Vigil "ally") is played as the talent (ALLY-VIGIL).
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, Kits.Value.ForRun(run.Content.Abilities));
            var batch = Bot.RunBots(view, bots, memories, prng);
            memories = batch.Memories;
            prng = batch.Prng;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "combo")
                {
                    combos++;
                }
                else if (e.Type == "death" && e.Get("actorId") is JsInt id && partyIds.Contains(checked((int)id.Value)))
                {
                    deaths++;
                }
            }
        }
        var result = run.RunEnd.Result?.ToJs() ?? "none";
        var clear = run.RunEnd.ResultTick ?? scenario.MaxTicks + 1;
        return new RunMeasures(clear, deaths, combos, result);
    }

    /// <summary>
    /// A content ability as the sim runs it: game/src/Host/HostRunSetup.cs ToSim (the host's own converter, which the
    /// Godot-only assembly keeps private). A stance ability's static Effects are its "mend" variant; the variants ride
    /// along. <see cref="Base_kit_through_the_converter_runs_identically"/> proves this matches the setup's kit.
    /// </summary>
    internal static S.AbilityDef ToSim(C.AbilityDef a)
    {
        if (a.Shape.Width is not null)
        {
            throw new InvalidOperationException($"fork parity: ability \"{a.Id}\" has a shape width (facet patches only)");
        }
        var ops = a.Variants is { } v && v.TryGetValue("mend", out var mend) ? mend : a.Effects;
        Dictionary<string, IReadOnlyList<S.EffectOp>>? variants = null;
        if (a.Variants is { } all)
        {
            variants = new Dictionary<string, IReadOnlyList<S.EffectOp>>(StringComparer.Ordinal);
            foreach (var kv in all)
            {
                variants[kv.Key] = kv.Value.Select(o => ToSim(o, a.Id + " variant " + kv.Key)).ToList();
            }
        }
        return new S.AbilityDef
        {
            Id = a.Id,
            CastTicks = a.CastTicks,
            GcdTicks = a.GcdTicks,
            CooldownTicks = a.CooldownTicks,
            Charges = a.Charges,
            Cost = new S.AbilityCost(a.Cost.Resource, a.Cost.Amount),
            Range = a.Range,
            Shape = new S.AbilityShape(ToSim(a.Shape.Kind), a.Shape.Radius, a.Shape.Angle1024, a.Shape.Length),
            Interruptible = a.Interruptible,
            Effects = ops.Select(o => ToSim(o, a.Id)).ToList(),
            Tags = a.Tags.ToList(),
            Variants = variants,
        };
    }

    private static S.ShapeKind ToSim(C.ShapeKind k) => k switch
    {
        C.ShapeKind.Target => S.ShapeKind.Target,
        C.ShapeKind.Self => S.ShapeKind.Self,
        C.ShapeKind.Cone => S.ShapeKind.Cone,
        C.ShapeKind.Circle => S.ShapeKind.Circle,
        C.ShapeKind.Line => S.ShapeKind.Line,
        _ => throw new InvalidOperationException($"fork parity: unknown shape kind {k}"),
    };

    private static S.EffectOp ToSim(C.EffectOp op, string where) => op switch
    {
        C.DamageOp d => new S.DamageOp(d.Amount, d.School),
        C.HealOp h => new S.HealOp(h.Amount),
        C.ApplyEffectOp e => new S.ApplyEffectOp(e.EffectId, e.DurationTicks, e.Stacks),
        C.RemoveEffectOp r => new S.RemoveEffectOp(r.EffectId),
        C.InterruptOp => new S.InterruptOp(),
        C.ResourceOp r => new S.ResourceOp(r.Delta),
        C.ThreatOp t => new S.ThreatOp(t.Amount, t.MultiplierBp),
        C.WindowOp w => new S.WindowOp(
            w.WindowId,
            w.DurationTicks,
            w.OnTarget,
            w.SetupClass ?? throw new InvalidOperationException($"fork parity: {where} has a window op without setupClass"),
            w.Self,
            w.Alternate,
            w.RadiusMm),
        C.PayoffOp => new S.PayoffOp(),
        C.ComboOp c => new S.ComboOp(c.ComboTable),
        C.PartyEffectOp p => new S.PartyEffectOp(p.EffectId, p.DurationTicks, p.Stacks),
        C.CloudOp c => new S.CloudOp(c.EffectId, c.DurationTicks, c.RadiusMm),
        _ => throw new InvalidOperationException($"fork parity: {where} has an op the sim does not run ({op.GetType().Name})"),
    };

    /// <summary>True when <paramref name="a"/> is at least as good as <paramref name="b"/> on every measure of every run
    /// and strictly better on at least one (the bible's dominance rule).</summary>
    internal static bool Dominates(IReadOnlyList<RunMeasures> a, IReadOnlyList<RunMeasures> b)
    {
        if (a.Count != b.Count || a.Count == 0)
        {
            throw new ArgumentException("dominance compares the same non-empty run list");
        }
        var strictly = false;
        for (var i = 0; i < a.Count; i++)
        {
            // Normalised so that lower is better on all three axes.
            var axesA = new long[] { a[i].ClearTick, a[i].Deaths, -(long)a[i].Combos };
            var axesB = new long[] { b[i].ClearTick, b[i].Deaths, -(long)b[i].Combos };
            for (var k = 0; k < axesA.Length; k++)
            {
                if (axesA[k] > axesB[k])
                {
                    return false;
                }
                if (axesA[k] < axesB[k])
                {
                    strictly = true;
                }
            }
        }
        return strictly;
    }

    /// <summary>Every fork option's measures over every scenario, keyed "class.ability:option" in content order.</summary>
    private static IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, IReadOnlyList<RunMeasures> Runs)> Options)> MeasureAll()
    {
        var registry = Registry.Value;
        var scenarios = Loaded.Value;
        var result = new List<(string, string, IReadOnlyList<(string, IReadOnlyList<RunMeasures>)>)>();
        foreach (var cls in registry.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                var baseDef = registry.Abilities[fork.AbilityId];
                var options = new List<(string, IReadOnlyList<RunMeasures>)>();
                foreach (var option in fork.Options)
                {
                    var talented = ToSim(C.TalentPicks.ApplyPatch(baseDef, option.Patch));
                    options.Add((option.Id, scenarios.Select(s => Measure(s, cls.Id, talented)).ToList()));
                }
                result.Add((cls.Id, fork.AbilityId, options));
            }
        }
        return result;
    }

    /// <summary>
    /// Every option dominated beyond <paramref name="tolerance"/> (<see cref="ForkParityMargins.DominatesBeyondNoise"/>)
    /// as "class.ability:option" to its finding line, naming the measures whose margin exceeds the noise.
    /// </summary>
    internal static SortedDictionary<string, string> DominatedOptions(
        IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, IReadOnlyList<RunMeasures> Runs)> Options)> all,
        NoiseTolerance tolerance)
    {
        var dominated = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, abilityId, options) in all)
        {
            var runNames = RunNames(options[0].Runs.Count);
            foreach (var (loser, loserRuns) in options)
            {
                var by = options.Where(o => o.Option != loser && ForkParityMargins.DominatesBeyondNoise(o.Runs, loserRuns, tolerance)).Select(o => o.Option).ToList();
                if (by.Count == 0)
                {
                    continue;
                }
                var winner = options.First(o => o.Option == by[0]);
                dominated[abilityId + ":" + loser] = $"{abilityId}:{loser} is dominated by {string.Join("/", by)} beyond noise ("
                    + string.Join(", ", ForkParityMargins.Excess(winner.Runs, loserRuns, tolerance, runNames)) + ") | "
                    + string.Join("; ", runNames.Select((s, i) => $"{s} {loser} [{loserRuns[i]}] vs {by[0]} [{winner.Runs[i]}]"));
            }
        }
        return dominated;
    }

    /// <summary>Scenario names for <paramref name="count"/> runs (the real scenarios, or "run i" for a test double).</summary>
    private static IReadOnlyList<string> RunNames(int count) =>
        count == Scenarios.Length ? Scenarios.Select(s => s.Name).ToList() : Enumerable.Range(0, count).Select(i => "run " + i).ToList();

    /// <summary>
    /// The dominance half of the parity check: options dominated beyond <paramref name="tolerance"/> and not listed in
    /// <paramref name="known"/>, then listed options that are no longer dominated beyond it (stale). Empty = pass.
    /// </summary>
    internal static IReadOnlyList<string> DominanceFailures(
        IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, IReadOnlyList<RunMeasures> Runs)> Options)> all,
        IReadOnlyDictionary<string, string> known,
        NoiseTolerance tolerance)
    {
        var dominated = DominatedOptions(all, tolerance);
        var failures = dominated.Where(d => !known.ContainsKey(d.Key)).Select(d => d.Value).ToList();
        failures.AddRange(known.Keys.Where(k => !dominated.ContainsKey(k)).Select(k => "stale KnownDominatedForks entry (no longer dominated beyond noise; remove it): " + k));
        return failures;
    }

    /// <summary>
    /// Sibling options that measure identically on every measure of every run, as "class.ability:a = b" lines, for every
    /// fork that is measured at all (a fork whose options all run identically is the <see cref="UnmeasuredForks"/> case).
    /// </summary>
    internal static IReadOnlyList<string> IdenticalSiblings(
        IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, IReadOnlyList<RunMeasures> Runs)> Options)> all)
    {
        var lines = new List<string>();
        foreach (var (_, abilityId, options) in all)
        {
            if (options.All(o => o.Runs.SequenceEqual(options[0].Runs)))
            {
                continue;
            }
            for (var i = 0; i < options.Count; i++)
            {
                for (var j = i + 1; j < options.Count; j++)
                {
                    if (options[i].Runs.SequenceEqual(options[j].Runs))
                    {
                        lines.Add(abilityId + ":" + options[i].Option + " = " + options[j].Option + " | "
                            + string.Join(" | ", options[i].Runs.Select((r, k) => "run " + k + " " + r)));
                    }
                }
            }
        }
        return lines;
    }

    /// <summary>Pairs of runs (by index) in which every option of every fork measures identically: one run counted twice.</summary>
    internal static IReadOnlyList<(int A, int B)> IdenticalRuns(
        IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, IReadOnlyList<RunMeasures> Runs)> Options)> all,
        int runCount)
    {
        var pairs = new List<(int, int)>();
        for (var a = 0; a < runCount; a++)
        {
            for (var b = a + 1; b < runCount; b++)
            {
                if (all.All(f => f.Options.All(o => o.Runs[a] == o.Runs[b])))
                {
                    pairs.Add((a, b));
                }
            }
        }
        return pairs;
    }

    // Balance oracle: runs in the reported, non-required `balance` CI job, not in game-test (game/CLAUDE.md).
    [Trait("Category", "Balance")]
    [Fact(DisplayName = "fork parity > no fork option is dominated by a sibling over the seeded Hollow Vault runs at keys 2 and 10")]
    public void No_fork_option_is_dominated()
    {
        var all = MeasureAll();
        Assert.True(all.Count >= 15, $"fork parity measured only {all.Count} forks: the check is vacuous");
        foreach (var (_, abilityId, options) in all)
        {
            Assert.True(options.Count >= 2, $"{abilityId}: a fork with fewer than two options is not a decision");
            foreach (var (option, runs) in options)
            {
                _output.WriteLine($"{abilityId}:{option} | " + string.Join(" | ", runs.Select((r, i) => $"{Scenarios[i].Name} {r}")));
            }
        }

        foreach (var known in KnownDominatedForks)
        {
            _output.WriteLine("KNOWN DOMINATED (content defect, BIBLE s6): " + known.Key + " -- " + known.Value);
        }
        foreach (var unseen in UnmeasuredForks)
        {
            _output.WriteLine("UNMEASURED FORK (bots never cast it): " + unseen.Key + " -- " + unseen.Value);
        }
        var marginsLines = ForkParityMargins.Lines(all, Scenarios.Select(s => s.Name).ToList(), NoiseTolerance.Measured);
        foreach (var line in marginsLines.Where(l => l.EndsWith(" within noise", StringComparison.Ordinal)))
        {
            _output.WriteLine("DOMINATED WITHIN NOISE (recorded, does not gate): " + line);
        }
        CheckMarginsFile(marginsLines);
        var failures = DominanceFailures(all, KnownDominatedForks, NoiseTolerance.Measured);
        Assert.True(failures.Count == 0, "fork options dominated beyond the measured noise (BIBLE s6: a defect that blocks the content change):\n" + string.Join("\n", failures));
        var identical = IdenticalSiblings(all);
        Assert.True(identical.Count == 0, "sibling fork options that measure identically in every run (a player cannot tell them apart; retune the content):\n" + string.Join("\n", identical));
        var sameRuns = IdenticalRuns(all, Scenarios.Length);
        Assert.True(sameRuns.Count == 0, "scenarios that measure identically for every fork option (one run counted twice; pick a seed that differs):\n"
            + string.Join("\n", sameRuns.Select(p => Scenarios[p.A].Name + " = " + Scenarios[p.B].Name)));

        var flat = all.Where(f => f.Options.All(o => o.Runs.SequenceEqual(f.Options[0].Runs))).Select(f => f.AbilityId).ToList();
        var unlistedFlat = flat.Where(a => !UnmeasuredForks.ContainsKey(a)).ToList();
        var staleFlat = UnmeasuredForks.Keys.Where(k => !flat.Contains(k)).ToList();
        Assert.True(unlistedFlat.Count == 0, "forks the check cannot measure (every option runs identically), list them in UnmeasuredForks:\n" + string.Join("\n", unlistedFlat));
        Assert.True(staleFlat.Count == 0, "UnmeasuredForks lists forks that are now measured; remove them:\n" + string.Join("\n", staleFlat));
        Assert.True(flat.Count < all.Count, "no fork is measured at all: the check is vacuous");
    }

    [Fact(DisplayName = "fork parity > the base kit through the converter runs identically to the setup's kit")]
    public void Base_kit_through_the_converter_runs_identically()
    {
        var registry = Registry.Value;
        var scenario = Loaded.Value[0];
        var baseline = Measure(scenario, null, null);
        Assert.Equal("timed", baseline.Result);
        foreach (var cls in registry.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                var converted = ToSim(registry.Abilities[fork.AbilityId]);
                Assert.Equal(baseline, Measure(scenario, cls.Id, converted));
            }
        }
    }

    [Fact(DisplayName = "fork parity > a talent patch changes the measured run (the loadout reaches the bots' run)")]
    public void A_talent_reaches_the_run()
    {
        var registry = Registry.Value;
        var scenario = Loaded.Value[0];
        var baseline = Measure(scenario, null, null);
        // warden.guardBreak, because the bots cast it (56 casts in bots-hv-k2-s7); warden.strike is never cast.
        var guardBreak = registry.Abilities["warden.guardBreak"];
        var crippled = ToSim(guardBreak) with { Effects = Array.Empty<S.EffectOp>(), Tags = Array.Empty<string>() };
        Assert.NotEqual(baseline, Measure(scenario, "warden", crippled));
    }

    [Fact(DisplayName = "fork parity > the dial-90 key-2 seed is live: seed 11 and seed 7 measure differently there, and identically at dial 100")]
    public void The_dial_90_seed_is_live()
    {
        var k2 = Scenarios[1];
        Assert.Equal(90, k2.Dial);
        Assert.Equal(11, k2.Seed);
        var at90s11 = Measure(LoadScenario(k2), null, null);
        var at90s7 = Measure(LoadScenario((k2.Name + "-seed7", k2.SetupFile, 7, k2.Dial)), null, null);
        var at100s11 = Measure(LoadScenario((k2.Name + "-dial100", k2.SetupFile, 11, 100)), null, null);
        var at100s7 = Measure(Loaded.Value[0], null, null);
        _output.WriteLine($"dial 90: seed 11 {at90s11} | seed 7 {at90s7}; dial 100: seed 11 {at100s11} | seed 7 {at100s7}");
        Assert.NotEqual(at90s7, at90s11);
        // Why the dial is below 100 (f-313fba9c): at dial 100 the seed changes nothing this check measures.
        Assert.Equal(at100s7, at100s11);
    }

    [Fact(DisplayName = "fork parity > identical siblings and identical runs are caught; a fork that never runs differently is left to UnmeasuredForks")]
    public void Identical_siblings_and_runs()
    {
        var r1 = new RunMeasures(1000, 0, 5, "timed");
        var r2 = new RunMeasures(1100, 0, 4, "timed");
        var r3 = new RunMeasures(900, 1, 6, "timed");
        static IReadOnlyList<RunMeasures> Runs(params RunMeasures[] r) => r;
        static (string, string, IReadOnlyList<(string, IReadOnlyList<RunMeasures>)>) Fork(string id, params (string, IReadOnlyList<RunMeasures>)[] o) => ("c", id, o);
        var twins = Fork("c.a", ("x", Runs(r1, r2)), ("y", Runs(r1, r2)), ("z", Runs(r3, r2)));
        var flat = Fork("c.b", ("p", Runs(r1, r1)), ("q", Runs(r1, r1)));
        var apart = Fork("c.c", ("m", Runs(r1, r3)), ("n", Runs(r2, r3)));

        var found = IdenticalSiblings(new[] { twins, flat, apart });
        Assert.Single(found);
        Assert.StartsWith("c.a:x = y |", found[0]);
        Assert.Empty(IdenticalSiblings(new[] { flat, apart }));

        Assert.Equal(new[] { (0, 1) }, IdenticalRuns(new[] { flat }, 2));
        Assert.Empty(IdenticalRuns(new[] { flat, apart }, 2));
        Assert.Empty(IdenticalRuns(new[] { twins }, 2));
    }

    [Fact(DisplayName = "fork parity > dominance: at least as good everywhere and strictly better once; ties and trade-offs are not")]
    public void Dominance_rule()
    {
        var b = new[] { new RunMeasures(1000, 1, 5, "timed"), new RunMeasures(2000, 2, 8, "timed") };
        Assert.False(Dominates(b, b));
        Assert.True(Dominates(new[] { b[0] with { ClearTick = 999 }, b[1] }, b));
        Assert.True(Dominates(new[] { b[0], b[1] with { Combos = 9 } }, b));
        Assert.True(Dominates(new[] { b[0] with { Deaths = 0 }, b[1] }, b));
        Assert.False(Dominates(new[] { b[0] with { ClearTick = 999 }, b[1] with { Combos = 7 } }, b));
        Assert.False(Dominates(new[] { b[0] with { ClearTick = 999, Deaths = 2 }, b[1] }, b));
        Assert.False(Dominates(b, new[] { b[0] with { ClearTick = 999 }, b[1] }));
    }

    /// <summary>
    /// Checks the run's margins against the checked-in ForkParity.margins.txt. With
    /// KEYRUN_FORKPARITY_REBASELINE=1 the file is rewritten first, with the noise sweep re-measured into its header.
    /// Red only when the recorded pair set differs from the measured one (a fork or option was added, removed or
    /// renamed: re-baseline). Margin drift is printed, never red: margins move on every behaviour change.
    /// </summary>
    private void CheckMarginsFile(IReadOnlyList<string> lines)
    {
        var path = ForkParityMargins.FilePath(RolesOracleTraceTests.OracleDir());
        if (Environment.GetEnvironmentVariable(ForkParityMargins.RebaselineVariable) == "1")
        {
            File.WriteAllText(path, ForkParityMargins.Render(lines, NoiseTolerance.Measured, MeasureNoise()));
            _output.WriteLine("REBASELINED " + path);
        }
        Assert.True(File.Exists(path), $"{path} is missing: run the fork parity tests with {ForkParityMargins.RebaselineVariable}=1 once");
        var recorded = ForkParityMargins.Parse(File.ReadAllText(path));
        foreach (var drift in ForkParityMargins.Drift(recorded, lines, NoiseTolerance.Measured))
        {
            _output.WriteLine("MARGIN DRIFT beyond noise since the last re-baseline (informational): " + drift);
        }
        var changes = ForkParityMargins.PairSetChanges(recorded, lines);
        Assert.True(changes.Count == 0, $"ForkParity.margins.txt records a different set of sibling pairs; re-baseline with {ForkParityMargins.RebaselineVariable}=1:\n" + string.Join("\n", changes));
    }

    /// <summary>
    /// The noise sweep behind <see cref="NoiseTolerance.Measured"/>: every fork option and the base kit over
    /// <see cref="NoiseSeeds"/> seeds at dial 90 (where the seed decides each bot's mistakes, so each seed is a different
    /// trajectory through the same content) at keys 2 and 10. Per key: the base kit's spread, and the median over all
    /// sibling pairs of the population standard deviation of the pair's per-seed margin. Run only when re-baselining.
    /// </summary>
    private static IReadOnlyList<string> MeasureNoise()
    {
        var registry = Registry.Value;
        var lines = new List<string>();
        foreach (var (key, file) in new[] { ("k2", "hollow-vault-k2.setup.json"), ("k10", "hollow-vault-k10.setup.json") })
        {
            var sweep = Enumerable.Range(1, NoiseSeeds).Select(seed => LoadScenario(("noise", file, seed, 90))).ToList();
            var baseRuns = sweep.Select(s => Measure(s, null, null)).ToList();
            var sds = new List<double>[] { new(), new(), new() };
            foreach (var cls in registry.Classes.Values)
            {
                foreach (var fork in cls.Talents)
                {
                    var baseDef = registry.Abilities[fork.AbilityId];
                    var options = fork.Options.Select(o => sweep.Select(s => Measure(s, cls.Id, ToSim(C.TalentPicks.ApplyPatch(baseDef, o.Patch)))).ToList()).ToList();
                    for (var i = 0; i < options.Count; i++)
                    {
                        for (var j = i + 1; j < options.Count; j++)
                        {
                            var m = options[i].Zip(options[j], ForkParityMargins.Margin).ToList();
                            if (m.All(x => x == (0, 0, 0)))
                            {
                                continue; // an unmeasured fork: both options run identically on every seed
                            }
                            sds[0].Add(PopulationSd(m.Select(x => (double)x.Clear)));
                            sds[1].Add(PopulationSd(m.Select(x => (double)x.Deaths)));
                            sds[2].Add(PopulationSd(m.Select(x => (double)x.Combos)));
                        }
                    }
                }
            }
            var clears = baseRuns.Select(r => r.ClearTick).OrderBy(x => x).ToList();
            var medianClear = Median(clears.Select(x => (double)x));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{key} base kit, dial 90, seeds 1..{NoiseSeeds}: clear {clears[0]}..{clears[^1]} (median {medianClear:0}, sd {PopulationSd(clears.Select(x => (double)x)):0.0}), deaths {baseRuns.Min(r => r.Deaths)}..{baseRuns.Max(r => r.Deaths)}, combos {baseRuns.Min(r => r.Combos)}..{baseRuns.Max(r => r.Combos)}"));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{key} paired margin sd, median over {sds[0].Count} sibling pairs: clear {Median(sds[0]):0.0} ticks ({Median(sds[0]) * 10000 / medianClear:0} bp of the median base clear), deaths {Median(sds[1]):0.00}, combos {Median(sds[2]):0.00}"));
        }
        return lines;
    }

    /// <summary>Seeds in the re-baseline noise sweep.</summary>
    private const int NoiseSeeds = 12;

    private static double PopulationSd(IEnumerable<double> values)
    {
        var v = values.ToList();
        var mean = v.Average();
        return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Count);
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
