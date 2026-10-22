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
/// <para>NOISE TOLERANCE (findings f-e3daed76, f-8ae6250a, f-bee3bf93): a dominance gates only when A's margins over B
/// EXCEED <see cref="NoiseTolerance.Measured"/> (clear in bp of the slower clear tick in that run, deaths, combos) in at
/// least <see cref="ForkParityMargins.MinScenariosBeyondNoise"/> (two) distinct scenarios. WHY TWO: with all-runs dominance
/// plus one beyond-tolerance margin, within-tolerance drift in the other scenarios flipped the verdict while the
/// beyond-tolerance margin did not move (chanter.dissonance amplify vs discord, #601: only hv-k10-s7 combos 26 > 21 was
/// beyond, identical on main and #601; the 32-seed study has every pair mean inside one sd of zero). A real dominance
/// shows beyond the noise in more than one scenario; a strictly worse fork still does, and the tolerances are unchanged.
/// A dominance whose margins are inside the tolerance, or beyond it in one scenario only, is printed ("DOMINATED WITHIN NOISE") and recorded, never red:
/// the runs are chaotic, so any behaviour change moves every option's measures, and a balance oracle pinned to main's
/// exact dynamics went red on every behaviour PR (#307: hymn staccato dominated by 2 combos on equal clears; #362: surge
/// and discord). The tolerance is not a constant: it is 2 x the measured seed noise, read from the checked-in study
/// game/bots/Keyrun.Bots.Tests/fork-parity-noise.json (<see cref="ForkParityNoiseStudy"/> has the rule, why k = 2, and
/// the re-run command; the check is red when the file is missing or incomplete). A real dominance still blocks:
/// <see cref="ForkParityNoiseStudyTests.Three_sigma_dominance_gates_one_sigma_does_not"/> drives a sibling worse by 3 sd
/// through the same failure path this check asserts on.</para>
/// <para>MARGINS FILE: every sibling pair's per-run measures, margins and verdict are recorded in
/// game/bots/Keyrun.Bots.Tests/ForkParity.margins.txt (format in its header). The check prints margin drift beyond the
/// tolerance since the recording but never fails on it; it fails only when the recorded pair set differs from the
/// measured one (a fork or option added, removed or renamed). RE-BASELINE (after a content retune, a fork change, or when
/// the drift lines say margins moved): run
/// <c>KEYRUN_FORKPARITY_REBASELINE=1 dotnet test game/bots/Keyrun.Bots.Tests --filter "FullyQualifiedName~ForkParityTests"</c>.
/// It rewrites the file with the study's summary in its header; commit it in its own commit and say why in the PR. When
/// a behaviour change may have moved the noise itself, re-run the study (<see cref="ForkParityNoiseStudy"/>) in its own
/// PR, never in the PR whose red it would clear.</para>
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
    /// <remarks>
    /// Empty: since the Chanter and Warden bot changes of PR #449 (R-BOTS-17 and kin) the bots cast warden.strike,
    /// cinder.emberBolt and chanter.refrain in these runs, so every option of those three forks measures differently from
    /// its siblings; the stale-entry assertion below demands their removal.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> UnmeasuredForks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
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

    internal sealed record Scenario(string Name, RunSetup Setup, long Seed, int Dial, int MaxTicks);

    internal static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    private static readonly Lazy<KitRegistry> Kits = new(() => KitRegistry.From(Registry.Value));

    private static readonly Lazy<IReadOnlyList<Scenario>> Loaded = new(() => Scenarios.Select(LoadScenario).ToList());

    internal static Scenario LoadScenario((string Name, string SetupFile, long Seed, int Dial) s) =>
        new(s.Name, RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), s.SetupFile), s.Seed), s.Seed, s.Dial, 24000);

    /// <summary>
    /// Runs <paramref name="scenario"/> with the bots, every member of <paramref name="classId"/> carrying
    /// <paramref name="talented"/> (null = no loadout, the base kit).
    /// </summary>
    /// <param name="scenario">The seeded run.</param>
    /// <param name="classId">The fork's class, or null for the base kit.</param>
    /// <param name="talented">The talented ability, or null for the base kit.</param>
    /// <param name="onStep">Optional observer of each step's events (diagnostic tests only; never changes the run).</param>
    /// <param name="beforeStep">Optional observer of the run just before each step (never changes the run).</param>
    /// <param name="afterStep">Optional observer of the run and the step's events just after each step (never changes the run).</param>
    internal static RunMeasures Measure(Scenario scenario, string? classId, S.AbilityDef? talented, Action<IReadOnlyList<SimEvent>>? onStep = null, Action<Run>? beforeStep = null, Action<Run, IReadOnlyList<SimEvent>>? afterStep = null)
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
            beforeStep?.Invoke(run);
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            onStep?.Invoke(stepped.Events.ToList());
            afterStep?.Invoke(run, stepped.Events.ToList());
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
        C.DamagePctOp d => new S.DamagePctOp(d.MaxHealthBp, d.School),
        C.PullOp p => new S.PullOp(p.DistanceMm),
        C.DisplaceOp d => new S.DisplaceOp(d.DistanceMm, d.FreesBinds, d.TowardTarget),
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

    /// <summary>
    /// R-FORK-RETUNE-1 (findings f-a49de510, f-82e6a860): Flashpoint changes Cinderfield's function, not its numbers.
    /// Spreading Flame is the wide floor patch (a field Combo for whoever stands in it); Flashpoint ignites the Cinder's
    /// target instead, so its Window rides the enemy and an ally's Payoff on that enemy lands the pair burst Combo (Forge
    /// or Steam), the single-target play (BIBLE s6 "which Window/Payoff tag the ability carries", s7 "retags an
    /// ability's Window"). It only earns its place if the bots actually use it that way: counted as burst "combo" events
    /// closing a cinderfield Window on a target (any comboId but the floor's "cinderfield-field"), in every run.
    /// </summary>
    [Fact(DisplayName = "fork parity > the bots close Flashpoint's on-target Window for a pair burst Combo in every seeded run")]
    public void Flashpoint_window_is_closed_on_its_target_by_the_bots()
    {
        var registry = Registry.Value;
        var fork = registry.Classes["cinder"].Talents.Single(f => f.AbilityId == "cinder.cinderfield");
        var option = fork.Options.Single(o => o.Id == "flashpoint");
        var talented = ToSim(C.TalentPicks.ApplyPatch(registry.Abilities[fork.AbilityId], option.Patch));
        var perRun = new List<int>();
        foreach (var scenario in Loaded.Value)
        {
            var landed = 0;
            Measure(scenario, "cinder", talented, events =>
                landed += events.Count(e =>
                    e.Type == "combo"
                    && e.Get("windowId") is JsString w && w.Value == "cinderfield"
                    && e.Get("comboId") is JsString c && c.Value != "cinderfield-field"
                    && e.Get("targetId") is JsInt));
            perRun.Add(landed);
            _output.WriteLine($"{scenario.Name}: Flashpoint's on-target Window closed for a burst Combo {landed} times");
        }
        Assert.True(perRun.All(n => n > 0), "Flashpoint's on-target Window must be closed for a pair burst Combo in every fork-parity run (R-FORK-RETUNE-1), measured: "
            + string.Join(", ", perRun));
    }

    /// <summary>A fork option's talented ability, as the bots' runs carry it.</summary>
    private static S.AbilityDef TalentedOption(string classId, string abilityId, string optionId)
    {
        var registry = Registry.Value;
        var fork = registry.Classes[classId].Talents.Single(f => f.AbilityId == abilityId);
        return ToSim(C.TalentPicks.ApplyPatch(registry.Abilities[abilityId], fork.Options.Single(o => o.Id == optionId).Patch));
    }

    /// <summary>
    /// For every completed cast of <paramref name="abilityId"/> by the bots in <paramref name="scenario"/> (the fork's
    /// class carrying <paramref name="talented"/>): how many living party members carry <paramref name="effectId"/> ending
    /// exactly <paramref name="durationTicks"/> after the cast, i.e. applied by that cast and not by another source.
    /// </summary>
    private static IReadOnlyList<int> CarriersPerCast(Scenario scenario, string classId, S.AbilityDef talented, string abilityId, string effectId, int durationTicks)
    {
        var carriers = new List<int>();
        Measure(scenario, classId, talented, afterStep: (run, events) =>
        {
            foreach (var e in events.Where(e => e.Type == "cast_complete" && e.Get("abilityId") is JsString a && a.Value == abilityId))
            {
                carriers.Add(run.State.Actors.Values.Count(p =>
                    p.Kind == ActorKind.Player && p.Alive && p.Effects.Any(x => x.EffectId == effectId && x.EndsTick == e.Tick + durationTicks)));
            }
        });
        return carriers;
    }

    /// <summary>
    /// R-FORK-RETUNE-1 (findings f-a49de510, f-82e6a860): Fortress changes what Bulwark DOES. Mobile Ward is the personal,
    /// instant ward; Fortress is the group play: its long cast raises the ring over the whole party, braces it and
    /// rallies it (Anthem's haste on every ally in the ring; BIBLE s7: a Facet changes function, not numbers). It only
    /// earns its place if the bots cast it and the haste actually lands on more than the Warden: counted per Bulwark cast
    /// as party members carrying the haste that very cast applied.
    /// </summary>
    [Fact(DisplayName = "fork parity > the bots cast Fortress and the haste it applies lands on more than the Warden in every seeded run")]
    public void Fortress_rallies_the_party_when_the_bots_cast_it()
    {
        var talented = TalentedOption("warden", "warden.bulwark", "fortress");
        var haste = talented.Effects.OfType<S.ApplyEffectOp>().FirstOrDefault(e => e.EffectId == "hasted");
        Assert.True(haste is not null, "Fortress must rally the ring with the hasted effect (R-FORK-RETUNE-1): it carries no hasted op");
        var perRun = new List<string>();
        var ok = true;
        foreach (var scenario in Loaded.Value)
        {
            var carriers = CarriersPerCast(scenario, "warden", talented, "warden.bulwark", "hasted", haste!.DurationTicks);
            var best = carriers.Count == 0 ? 0 : carriers.Max();
            perRun.Add($"{scenario.Name}: {carriers.Count} casts, most hasted at once {best}");
            ok &= carriers.Count > 0 && best >= 2;
            _output.WriteLine(perRun[^1]);
        }
        Assert.True(ok, "the bots must cast Fortress in every fork-parity run and rally at least two party members (R-FORK-RETUNE-1), measured: " + string.Join("; ", perRun));
    }

    /// <summary>
    /// R-FORK-RETUNE-1: Overwhelm changes what Reckoning DOES. Execute is the cheap single-target finisher; Overwhelm is
    /// the full-Momentum cleave that hands Momentum back, so a chain of finishers follows (BIBLE s7 "alters the resource
    /// loop"). Counted per Overwhelm cast by the bots on the Warden's own Momentum across the step: the refund must
    /// show, net of the cost. Steps where a Combo also fired are skipped (Forge pays Momentum too).
    /// </summary>
    [Fact(DisplayName = "fork parity > the bots cast Overwhelm and its cleave hands Momentum back in every seeded run")]
    public void Overwhelm_refunds_momentum_when_the_bots_cast_it()
    {
        var talented = TalentedOption("warden", "warden.reckoning", "overwhelm");
        var refund = talented.Effects.OfType<S.ResourceOp>().Sum(r => r.Delta);
        Assert.True(refund > 0, "Overwhelm must hand Momentum back (R-FORK-RETUNE-1): it carries no positive resource op");
        var net = refund - talented.Cost.Amount;
        var perRun = new List<string>();
        var ok = true;
        foreach (var scenario in Loaded.Value)
        {
            var before = new Dictionary<int, long>();
            var clean = 0;
            var wrong = 0;
            Measure(scenario, "warden", talented,
                beforeStep: run => { before = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).ToDictionary(a => a.Id, a => a.Resource); },
                afterStep: (run, events) =>
                {
                    if (events.Any(e => e.Type == "combo"))
                    {
                        return;
                    }
                    foreach (var e in events.Where(e => e.Type == "cast_complete" && e.Get("abilityId") is JsString a && a.Value == "warden.reckoning"))
                    {
                        var actor = (int)((JsInt)e.Get("actorId")!).Value;
                        if (run.State.Actors[actor].Resource - before[actor] == net)
                        {
                            clean++;
                        }
                        else
                        {
                            wrong++;
                        }
                    }
                });
            perRun.Add($"{scenario.Name}: {clean} casts moved Momentum by {net}, {wrong} by something else");
            ok &= clean > 0 && wrong == 0;
            _output.WriteLine(perRun[^1]);
        }
        Assert.True(ok, "the bots must cast Overwhelm in every fork-parity run and every Combo-free cast must net " + net + " Momentum (R-FORK-RETUNE-1), measured: " + string.Join("; ", perRun));
    }

    /// <summary>
    /// R-FORK-RETUNE-1: Reserve changes what Vigil DOES. Guardian is the single-ally spot shield; Reserve is the group
    /// play: a shorter shield thrown over everyone in the ring, the Warden included. It only earns its place if the bots
    /// cast it and the shield lands on more than the Warden, counted per Vigil cast as party members carrying the shield
    /// that very cast applied.
    /// </summary>
    [Fact(DisplayName = "fork parity > the bots cast Reserve and its shield lands on more than the Warden in every seeded run")]
    public void Reserve_shields_the_ring_when_the_bots_cast_it()
    {
        var talented = TalentedOption("warden", "warden.vigil", "reserve");
        var shield = talented.Effects.OfType<S.ApplyEffectOp>().Single();
        var perRun = new List<string>();
        var ok = true;
        foreach (var scenario in Loaded.Value)
        {
            var carriers = CarriersPerCast(scenario, "warden", talented, "warden.vigil", shield.EffectId, shield.DurationTicks);
            var best = carriers.Count == 0 ? 0 : carriers.Max();
            perRun.Add($"{scenario.Name}: {carriers.Count} casts, most shielded at once {best}");
            ok &= carriers.Count > 0 && best >= 2;
            _output.WriteLine(perRun[^1]);
        }
        Assert.True(ok, "the bots must cast Reserve in every fork-parity run and shield at least two party members (R-FORK-RETUNE-1), measured: " + string.Join("; ", perRun));
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
    /// KEYRUN_FORKPARITY_REBASELINE=1 the file is rewritten first, with the noise study's summary (fork-parity-noise.json)
    /// in its header.
    /// Red only when the recorded pair set differs from the measured one (a fork or option was added, removed or
    /// renamed: re-baseline). Margin drift is printed, never red: margins move on every behaviour change.
    /// </summary>
    private void CheckMarginsFile(IReadOnlyList<string> lines)
    {
        var path = ForkParityMargins.FilePath(RolesOracleTraceTests.OracleDir());
        if (Environment.GetEnvironmentVariable(ForkParityMargins.RebaselineVariable) == "1")
        {
            var study = File.ReadAllText(ForkParityNoiseStudy.FilePath(RolesOracleTraceTests.OracleDir()));
            File.WriteAllText(path, ForkParityMargins.Render(lines, NoiseTolerance.Measured, ForkParityNoiseStudy.HeaderLines(study)));
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
}
