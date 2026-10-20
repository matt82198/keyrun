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
/// <para>Runs: the three bot oracle scenarios (bots-hv-k2-s7, bots-hv-k2-s11, bots-hv-k10-s7; their setup file and seed
/// are read from each trace header), every party actor a real-role bot exactly as <see cref="RolesOracleTraceTests.Drive"/>
/// builds them. For one option, every party member of the fork's class carries the option's talented ability (the
/// content patch applied by <see cref="C.TalentPicks.ApplyPatch"/>, the screen's own talent layer) as a
/// <see cref="RunOptions.Loadouts"/> entry; every other ability stays at base. The run steps until it ends or reaches
/// the scenario's maxTicks.</para>
/// <para>Measures, integers only, each one measured separately in each run (a "measure" is one quantity in one run, so
/// an option dominates only when it is at least as good in every run): clear time = the run_end tick (a run that never
/// ends scores maxTicks + 1, worse than any end); deaths = party "death" events (lower is better); Combos landed =
/// "combo" events (higher is better).</para>
/// <para>Dominance: option A dominates sibling B when A is at least as good as B on every measure and strictly better
/// on at least one. Every ordered pair of options of every fork is checked. A dominated option must be listed in
/// <see cref="KnownDominatedForks"/> with a finding line, or the check is red; a listed option that is no longer
/// dominated is also red (the stale entry must be removed). Listed failures are printed on every run.</para>
/// </remarks>
public sealed class ForkParityTests
{
    private readonly ITestOutputHelper _output;

    public ForkParityTests(ITestOutputHelper output) => _output = output;

    /// <summary>The seeded Hollow Vault runs the bible names (keys 2 and 10), by bot oracle trace name.</summary>
    private static readonly string[] Scenarios = { "bots-hv-k2-s7", "bots-hv-k2-s11", "bots-hv-k10-s7" };

    /// <summary>
    /// KNOWN DOMINATED FORKS on origin/main 082bbfc, measured by this check: "class.ability:dominatedOption" to the
    /// finding (which sibling dominates it, and the measures). Each entry is a real content defect (BIBLE section 6:
    /// "a defect that blocks the content change"), kept here only so the check can land green while the retune lands
    /// separately. Never add an entry to make a content change pass: retune the content.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownDominatedForks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["warden.guardBreak:surge"] = "Surge is dominated by Focused and Pin: slower at both keys (k2 2048 vs 1705/1838, k10 2289 vs 2257/2170) with no more Combos and no fewer deaths.",
        ["cinder.flare:efficient"] = "Efficient is dominated by Wildfire (re-measured on origin/main 704c6af, after #296): slower with fewer Combos at both keys (k2 1664/26 vs 1533/30, k10 1906/35 vs 1865/37), no deaths either side (finding f-e7e6461c).",
        ["cinder.detonate:cheapShot"] = "Cheap Shot is dominated by Overkill: slower at both keys (k2 1868 vs 1648, k10 2380 vs 2014) with equal or fewer Combos (32/32, 43/44).",
        ["chanter.cadence:swift"] = "Swift is dominated by Resonance and Group Song: identical at k2, 91 ticks slower at k10 (2261 vs 2170), same Combos.",
        ["chanter.hymn:staccato"] = "Staccato is dominated by Round Robin: one Combo fewer at k2 (32 vs 33) and slower with fewer Combos at k10 (2289/38 vs 2170/40).",
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
        ["chanter.dissonance"] = "Amplify and Discord equal the base run: the bots never cast chanter.dissonance.",
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

    private sealed record Scenario(string Name, RunSetup Setup, long Seed, int MaxTicks);

    private static readonly Lazy<C.ContentRegistry> Registry = new(() => C.ContentLoader.Load());

    private static readonly Lazy<KitRegistry> Kits = new(() => KitRegistry.From(Registry.Value));

    private static readonly Lazy<IReadOnlyList<Scenario>> Loaded = new(() => Scenarios.Select(LoadScenario).ToList());

    private static Scenario LoadScenario(string name)
    {
        var dir = RolesOracleTraceTests.OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, name + ".trace.jsonl"));
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed);
        return new Scenario(name, setup, trace.Seed, 24000);
    }

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
            initial[p.Id] = BotMemory.Default(100);
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(scenario.Seed);
        var deaths = 0;
        var combos = 0;
        while (run.RunEnd.Result is null && run.State.Tick < scenario.MaxTicks)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, Kits.Value);
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

    /// <summary>Every dominated option as "class.ability:option" to its finding line.</summary>
    internal static SortedDictionary<string, string> DominatedOptions(
        IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, IReadOnlyList<RunMeasures> Runs)> Options)> all)
    {
        var dominated = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, abilityId, options) in all)
        {
            foreach (var (loser, loserRuns) in options)
            {
                var by = options.Where(o => o.Option != loser && Dominates(o.Runs, loserRuns)).Select(o => o.Option).ToList();
                if (by.Count == 0)
                {
                    continue;
                }
                var winner = options.First(o => o.Option == by[0]);
                dominated[abilityId + ":" + loser] = $"{abilityId}:{loser} is dominated by {string.Join("/", by)} | "
                    + string.Join("; ", Scenarios.Select((s, i) => $"{s} {loser} [{loserRuns[i]}] vs {by[0]} [{winner.Runs[i]}]"));
            }
        }
        return dominated;
    }

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
                _output.WriteLine($"{abilityId}:{option} | " + string.Join(" | ", runs.Select((r, i) => $"{Scenarios[i]} {r}")));
            }
        }

        var dominated = DominatedOptions(all);
        foreach (var known in KnownDominatedForks)
        {
            _output.WriteLine("KNOWN DOMINATED (content defect, BIBLE s6): " + known.Key + " -- " + known.Value);
        }
        foreach (var unseen in UnmeasuredForks)
        {
            _output.WriteLine("UNMEASURED FORK (bots never cast it): " + unseen.Key + " -- " + unseen.Value);
        }
        var unlisted = dominated.Where(d => !KnownDominatedForks.ContainsKey(d.Key)).Select(d => d.Value).ToList();
        var stale = KnownDominatedForks.Keys.Where(k => !dominated.ContainsKey(k)).ToList();
        Assert.True(unlisted.Count == 0, "dominated fork options (BIBLE s6: a defect that blocks the content change):\n" + string.Join("\n", unlisted));
        Assert.True(stale.Count == 0, "KnownDominatedForks lists options that are no longer dominated; remove them:\n" + string.Join("\n", stale));

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
}
