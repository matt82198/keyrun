using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// ENGINE-PLAN 3B exit gate: the three bot oracle traces (game/sim/ORACLE.md section 6) replayed with the C# bots
/// GENERATING every command, not replaying the recorded ones. Mirrors tests/oracle/traces.ts botRun: every party
/// actor is <c>makeBot()</c> with the real roles (<see cref="RoleSet.Default"/>), <c>defaultMemory(100)</c>, the bot
/// PRNG is <c>seedPrng(runSeed)</c>, and each tick's view is
/// <c>buildBotView(state, encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT)</c> over <c>loadContent()</c>'s registry
/// (run-setup.ts setContentRegistry), with no pings. Per tick the bot commands must equal the record's <c>c</c>
/// (canonical JSON, in order), then the stepped projection hash, full line and events must match; finally the trace
/// hash and the run result.
/// </summary>
public sealed class RolesOracleTraceTests
{
    private readonly ITestOutputHelper _output;

    public RolesOracleTraceTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<KitRegistry> Kits = new(() => KitRegistry.From(ContentLoader.Load()));

    /// <summary>The oracle fixture directory (game/sim/Keyrun.Sim.Tests/Fixtures/oracle).</summary>
    public static string OracleDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Keyrun.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new DirectoryNotFoundException("could not locate game/Keyrun.sln above " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir.FullName, "sim", "Keyrun.Sim.Tests", "Fixtures", "oracle");
    }

    /// <summary>The outcome of one bot-driven replay.</summary>
    /// <param name="Checked">Records that matched (commands, hash, line, events), from t=0.</param>
    /// <param name="Total">Records in the trace.</param>
    /// <param name="CommandsGenerated">Bot commands generated and matched.</param>
    /// <param name="TraceHash">The C# trace hash over the matched lines.</param>
    /// <param name="Result">The C# run end, "result@tick".</param>
    /// <param name="Failure">The first divergence, or null.</param>
    public sealed record BotReplay(int Checked, int Total, int CommandsGenerated, string TraceHash, string Result, string? Failure);

    /// <summary>Drives <paramref name="name"/> with the C# bots over <paramref name="roles"/>.</summary>
    /// <param name="name">Trace name (e.g. bots-hv-k2-s7).</param>
    /// <param name="roles">Role set the bots dispatch through.</param>
    /// <returns>The replay outcome.</returns>
    public static BotReplay Drive(string name, RoleSet roles)
    {
        var dir = OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, name + ".trace.jsonl"));
        // R-BOTS-9: the trace is the frozen TS run, which has no verse rule (RunSetup.WithTsKit).
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed).WithTsKit();
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var kits = Kits.Value;
        var bots = new SortedDictionary<int, IBot>();
        IReadOnlyDictionary<int, BotMemory> memories = new SortedDictionary<int, BotMemory>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(roles);
            initial[p.Id] = BotMemory.Default(100);
        }
        memories = initial;
        var prng = Skill.SeedPrng(trace.Seed);
        var lines = new List<string>();
        var generated = 0;
        string? failure = null;

        foreach (var record in trace.Records)
        {
            var t = record.T.ToString(CultureInfo.InvariantCulture);
            IReadOnlyList<string> events = Array.Empty<string>();
            if (record.T > 0)
            {
                var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits);
                var batch = Bot.RunBots(view, bots, memories, prng);
                memories = batch.Memories;
                prng = batch.Prng;
                var want = record.Commands.Select(CommandJson.ToCanonicalJson).ToList();
                var got = batch.Commands.Select(CommandJson.ToCanonicalJson).ToList();
                if (!want.SequenceEqual(got, StringComparer.Ordinal))
                {
                    failure = $"t={t} commands\n  expected: [{string.Join(",", want)}]\n  actual:   [{string.Join(",", got)}]";
                    break;
                }
                generated += got.Count;
                var stepped = RunRoot.StepRun(run, batch.Commands);
                run = stepped.Run;
                events = stepped.Events.Select(e => e.ToCanonicalJson()).ToList();
            }
            var line = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
            if (Fnv1a.Hex(Projection.RecordHash(line)) != record.H || (record.P is not null && record.P != line))
            {
                failure = $"t={t} projection\n  expected: {record.P ?? record.H}\n  actual:   {line}";
                break;
            }
            if (!record.Events.SequenceEqual(events, StringComparer.Ordinal))
            {
                failure = $"t={t} events\n  expected: {string.Join(" ", record.Events)}\n  actual:   {string.Join(" ", events)}";
                break;
            }
            lines.Add(line);
        }

        var traceHash = Fnv1a.Hex(Projection.TraceHash(lines));
        var resultJs = run.RunEnd.Result?.ToJs();
        var result = resultJs is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{resultJs}@{run.RunEnd.ResultTick}");
        var expectedResult = trace.Result is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{trace.Result}@{trace.ResultTick}");
        if (failure is null && traceHash != trace.TraceHash)
        {
            failure = $"traceHash {traceHash} (expected {trace.TraceHash})";
        }
        if (failure is null && result != expectedResult)
        {
            failure = $"result {result} (expected {expectedResult})";
        }
        return new BotReplay(lines.Count, trace.Records.Count, generated, traceHash, result, failure);
    }

    private void AssertDrives(string name, string expectedHash)
    {
        var r = Drive(name, RoleSet.Default);
        _output.WriteLine($"{name}: ticks {r.Checked}/{r.Total} commands {r.CommandsGenerated} traceHash {r.TraceHash} (expected {expectedHash}) result {r.Result}");
        Assert.True(r.Failure is null, $"{name}: diverged after {r.Checked}/{r.Total} records: {r.Failure}");
        Assert.Equal(r.Total, r.Checked);
        Assert.Equal(expectedHash, r.TraceHash);
        Assert.True(r.CommandsGenerated > 0, "a bot replay that generates no command is vacuous");
    }

    [Fact(DisplayName = "bot oracle > bots-hv-k2-s7 replays every tick from C#-generated bot commands")]
    public void Key2Seed7() => AssertDrives("bots-hv-k2-s7", "8b954c9c"); // R-SEED-1a (f-244d102b): the tank strikes the lull caster; the lull-hold is off in TS (inLull false) and the C# InLull rule is idle-only, so the bots now fight on from t=574. Re-recorded with ORACLE_WRITE=1 (TS oracle): header traceHash 8b954c9c timed@1504 (was 65b83a06 timed@1709). R-BOTS-17 on #449 (in Mend the Chanter presses the damaging Payoff on the fight's first idle GCD only, then the Refrain into Ward; healer-idle.ts twin of HealerIdle.cs; #449's bots merged with main 0f6a8989) re-recorded once on the merged tree with ORACLE_WRITE=1: header traceHash 65b83a06 timed@1709 (was main's 0b577722 timed@1565; #449's unpushed R-BOTS-14 record was 46f87509 timed@1894). R-ANTHEM-1 (f-7d5df7f9: Anthem's hasted goes to the party, never the combo's carrier) + R-ENCORE-1 re-recorded once with ORACLE_WRITE=1: header traceHash 0b577722 timed@1565 (was c47bc285 timed@1423; first changed record t=1053, after the Anthem closed on enemy 107 at 1051 now hastes the party instead of 107). R-HASTE-1 (haste from every live effect shortens casts; TS casting.ts now mirrors Casting.HastedCastTicks) re-recorded once with ORACLE_WRITE=1: header traceHash c47bc285 timed@1423 (was 46f7aef4 timed@1423; first change t=1177, the Anthem's hasted +1500 bp on Seed 108 ends its seed.slam at 1193, not 1196). RETARGET-FIZZLE (R-RETARGET-1, f-f1ab879b: a cast whose start target dies mid-cast fizzles) re-recorded once with ORACLE_WRITE=1: header traceHash 46f7aef4 timed@1423 (was 36715d32 timed@1407; first change t=691, actor 3's cinder.flare begun on 102 at 660, 102 died at 660, now completes with no damage instead of hitting 101). PILE-FOLLOWUP (R-DEADCAST-1: the TS oracle stops a dead caster's cast; with main 026bca17, #425, #426 merged) re-recorded once with ORACLE_WRITE=1: header traceHash 36715d32 timed@1407 (was c777e2f2 timed@1407; first change tick 1024, Pair 106 dies mid pair.stackCall and the cast now ends with cast_stopped instead of completing). PILE-INT (R-PILE-INT: #307 c99d4a94 + #362 d80b8233 + #364 e7e2092b + setup-lesson e60d840b + #326 f79b304b merged onto main 8f131464) re-recorded on the integrated tree with ORACLE_WRITE=1: header traceHash c777e2f2 timed@1407 (#307 parent d6bab866 timed@1717, #362 parent 2dc1cb67 timed@1407). HOLDER-LOCK D30 (PR #307) merged with CADENCE-SETUP (#415) and STUN-STOPS-ENEMIES (#344), re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash d6bab866, timed@1717; main 9ea7ef58 alone 2c1a47df timed@1730, HOLDER-LOCK 7721eefb alone fa9bebe3 timed@1600). main 9ea7ef58 history: CADENCE-SETUP (f-b31e8f34: the healer opens Attuned on a damage partner's enemy; TS twin of HEAL-NEVER-HOSTILE) re-recorded with ORACLE_WRITE=1: header traceHash 2c1a47df timed@1730 (was 00b7f106 timed@1625). HOLDER-LOCK D30 (PR #307) merged with PR #370 SPELL-QUEUE + ZERO-GCD, re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash fa9bebe3, timed@1600; main 7fb46645 alone 00b7f106, HOLDER-LOCK merge 6b057907 e8f4feaf). HOLDER-LOCK history: HOLDER-LOCK D30 merged with INTEGRATE-PLAYABLE, re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash e8f4feaf, timed@1723; main d5c20907 timed@1736, HOLDER-LOCK PR 78511dd0 timed@1723). main history: PR #370 merged with INTEGRATE-PLAYABLE (#396), re-recorded on the merged tree (ORACLE_WRITE=1): header traceHash 00b7f106 timed@1625, byte-identical to PR #370's own recording (main alone d5c20907 timed@1736). SPELL-QUEUE PR #370 (bots press in the last 8 ticks of their cast; was 306aeb4f); ZERO-GCD + NO-PRESS-MID-CAST (f-80b5cf16) merged with ENEMY-ENGAGE, re-recorded on the merged tree (was f0369dfc; ZERO-GCD + NO-PRESS-MID-CAST alone d6621e57); INTEGRATE-PLAYABLE alone: the literal follows the trace recorded on the integrated tree (e19bc046, header traceHash d5c20907; was f0369dfc, timed@1719 -> timed@1736). ENEMY-ENGAGE merged with SLAM-TELEGRAPH, re-recorded on the merged tree (timed@1719; ENEMY-ENGAGE alone f86f0835 timed@1766, SLAM-TELEGRAPH + QUENCH-REFUND alone 13473efa timed@1719); SLAM-TELEGRAPH alone 67b9605f, QUENCH-REFUND alone 1f9fd280; before both c3d7a583; was 4df1e3f1; re-recorded: healer no longer swaps verse to self-heal. DEAD-CASTER-TELEGRAPH (was 902b84a7), KF3-SEED-ENGAGE (was 71eb4d2e) MERGE-362 parent history (its literal "2dc1cb67"): MERGE-362 (#362 tank survivability, healer press, Mending 6000, TS blind port merged with main's CADENCE-SETUP #415 and #344) re-recorded on the merged tree with ORACLE_WRITE=1: header traceHash 2dc1cb67 timed@1407 (#362 alone, D-ORACLE-BLIND-PARITY: b4bdfb59 timed@1475, was d5c20907 / 9d2294c1; main alone 2c1a47df timed@1730).

    [Fact(DisplayName = "bot oracle > bots-hv-k2-s11 replays every tick from C#-generated bot commands")]
    public void Key2Seed11() => AssertDrives("bots-hv-k2-s11", "8b954c9c"); // R-SEED-1a (f-244d102b): as k2-s7, first change t=574. Re-recorded with ORACLE_WRITE=1 (TS oracle): header traceHash 8b954c9c timed@1504 (was 65b83a06 timed@1709). R-BOTS-17 on #449 (in Mend the Chanter presses the damaging Payoff on the fight's first idle GCD only, then the Refrain into Ward; healer-idle.ts twin of HealerIdle.cs; #449's bots merged with main 0f6a8989) re-recorded once on the merged tree with ORACLE_WRITE=1: header traceHash 65b83a06 timed@1709 (was main's 0b577722 timed@1565; #449's unpushed R-BOTS-14 record was 46f87509 timed@1894). R-ANTHEM-1 + R-ENCORE-1 re-recorded once with ORACLE_WRITE=1: header traceHash 0b577722 timed@1565 (was c47bc285 timed@1423; first changed record t=1053, as k2-s7). R-HASTE-1 re-recorded once with ORACLE_WRITE=1: header traceHash c47bc285 timed@1423 (was 46f7aef4 timed@1423; first change t=1177, as k2-s7). RETARGET-FIZZLE (R-RETARGET-1, f-f1ab879b) re-recorded once with ORACLE_WRITE=1: header traceHash 46f7aef4 timed@1423 (was 36715d32 timed@1407; first change t=691, as k2-s7). PILE-FOLLOWUP (R-DEADCAST-1: the TS oracle stops a dead caster's cast; with main 026bca17, #425, #426 merged) re-recorded once with ORACLE_WRITE=1: header traceHash 36715d32 timed@1407 (was c777e2f2 timed@1407; first change tick 1024, Pair 106 dies mid pair.stackCall and the cast now ends with cast_stopped instead of completing). PILE-INT (R-PILE-INT: #307 c99d4a94 + #362 d80b8233 + #364 e7e2092b + setup-lesson e60d840b + #326 f79b304b merged onto main 8f131464) re-recorded on the integrated tree with ORACLE_WRITE=1: header traceHash c777e2f2 timed@1407 (#307 parent d6bab866 timed@1717, #362 parent 2dc1cb67 timed@1407). HOLDER-LOCK D30 (PR #307) merged with CADENCE-SETUP (#415) and STUN-STOPS-ENEMIES (#344), re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash d6bab866, timed@1717; main 9ea7ef58 alone 2c1a47df timed@1730, HOLDER-LOCK 7721eefb alone fa9bebe3 timed@1600). main 9ea7ef58 history: CADENCE-SETUP (f-b31e8f34) re-recorded with ORACLE_WRITE=1: header traceHash 2c1a47df timed@1730 (was 00b7f106 timed@1625). HOLDER-LOCK D30 (PR #307) merged with PR #370 SPELL-QUEUE + ZERO-GCD, re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash fa9bebe3, timed@1600; main 7fb46645 alone 00b7f106, HOLDER-LOCK merge 6b057907 e8f4feaf). HOLDER-LOCK history: HOLDER-LOCK D30 merged with INTEGRATE-PLAYABLE, re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash e8f4feaf, timed@1723; main d5c20907 timed@1736, HOLDER-LOCK PR 78511dd0 timed@1723). main history: PR #370 merged with INTEGRATE-PLAYABLE (#396), re-recorded on the merged tree (ORACLE_WRITE=1): header traceHash 00b7f106 timed@1625, byte-identical to PR #370's own recording (main alone d5c20907 timed@1736). SPELL-QUEUE PR #370 (was 306aeb4f); ZERO-GCD + NO-PRESS-MID-CAST (f-80b5cf16) merged with ENEMY-ENGAGE, re-recorded on the merged tree (was f0369dfc; ZERO-GCD + NO-PRESS-MID-CAST alone d6621e57); INTEGRATE-PLAYABLE alone: the literal follows the trace recorded on the integrated tree (header traceHash d5c20907; was f0369dfc, timed@1719 -> timed@1736). ENEMY-ENGAGE merged with SLAM-TELEGRAPH, re-recorded on the merged tree (timed@1719; ENEMY-ENGAGE alone f86f0835, SLAM-TELEGRAPH + QUENCH-REFUND alone 13473efa); SLAM-TELEGRAPH alone 67b9605f, QUENCH-REFUND alone 1f9fd280; before both c3d7a583; was 4df1e3f1; re-recorded: healer no longer swaps verse to self-heal. DEAD-CASTER-TELEGRAPH (was 902b84a7), KF3-SEED-ENGAGE (was 71eb4d2e) MERGE-362 parent history (its literal "2dc1cb67"): MERGE-362 (#362 tank survivability, healer press, Mending 6000, TS blind port merged with main's CADENCE-SETUP #415 and #344) re-recorded on the merged tree with ORACLE_WRITE=1: header traceHash 2dc1cb67 timed@1407 (#362 alone, D-ORACLE-BLIND-PARITY: b4bdfb59 timed@1475, was d5c20907 / 9d2294c1; main alone 2c1a47df timed@1730).

    [Fact(DisplayName = "bot oracle > bots-hv-k10-s7 replays every tick from C#-generated bot commands")]
    public void Key10Seed7() => AssertDrives("bots-hv-k10-s7", "167a3b93"); // LINE-TELEGRAPH (seed.lash circle->line, BIBLE s3) re-recorded with ORACLE_WRITE=1: header traceHash 167a3b93 timed@1842 (was 9c4709e8 timed@1865). R-SEED-1a (f-244d102b): the tank strikes the lull caster, first change t=753. Re-recorded with ORACLE_WRITE=1 (TS oracle): header traceHash 9c4709e8 timed@1865 (was 34681a6e timed@1855). R-BOTS-17 on #449 (in Mend the Chanter presses the damaging Payoff on the fight's first idle GCD only, then the Refrain into Ward; healer-idle.ts twin of HealerIdle.cs; #449's bots merged with main 0f6a8989) re-recorded once on the merged tree with ORACLE_WRITE=1: header traceHash 34681a6e timed@1855 (was main's 45af272e timed@2045; #449's unpushed R-BOTS-14 record was 95400744 timed@1852). R-ANTHEM-1 + R-ENCORE-1 re-recorded once with ORACLE_WRITE=1: header traceHash 45af272e timed@2045 (was 3faf0def timed@1989; first changed record t=1503, after the Anthem closed on Seed 108 at 1499 now hastes the party instead of 108). R-HASTE-1 re-recorded once with ORACLE_WRITE=1: header traceHash 3faf0def timed@1989 (was eba9e2a3 timed@1989; first change t=1513, the Anthem's hasted on Seed 108 (combo at 1499) ends its seed.slam begun at 1512 at 1529, not 1532). RETARGET-FIZZLE (R-RETARGET-1, f-f1ab879b) re-recorded once with ORACLE_WRITE=1: header traceHash eba9e2a3 timed@1989 (was 346e3c22 timed@1855; first change t=331, actors 4 and 5's cinder.flare begun on 100 at 300, 100 died at 301, now complete with no damage instead of hitting 101). PILE-FOLLOWUP (R-DEADCAST-1: the TS oracle stops a dead caster's cast; with main 026bca17, #425, #426 merged) re-recorded once with ORACLE_WRITE=1: header traceHash 346e3c22 timed@1855 (was eca7f962 timed@1855; first change tick 1279, Pair 105 and 106 die mid pair.stackCall and both casts now end with cast_stopped instead of completing). PILE-INT (R-PILE-INT: #307 c99d4a94 + #362 d80b8233 + #364 e7e2092b + setup-lesson e60d840b + #326 f79b304b merged onto main 8f131464) re-recorded on the integrated tree with ORACLE_WRITE=1: header traceHash eca7f962 timed@1855 (#307 parent e149a22e timed@2134, #362 parent 736f595c timed@1955). HOLDER-LOCK D30 (PR #307) merged with CADENCE-SETUP (#415) and STUN-STOPS-ENEMIES (#344), re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash e149a22e, timed@2134; main 9ea7ef58 alone aa65cde8 timed@2074, HOLDER-LOCK 7721eefb alone 3fce00d0 timed@2198). main 9ea7ef58 history: CADENCE-SETUP (f-b31e8f34) re-recorded with ORACLE_WRITE=1: header traceHash aa65cde8 timed@2074 (was 4f1aac04 timed@2197). HOLDER-LOCK D30 (PR #307) merged with PR #370 SPELL-QUEUE + ZERO-GCD, re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash 3fce00d0, timed@2198; main 7fb46645 alone 4f1aac04, HOLDER-LOCK merge 6b057907 0df2d046). HOLDER-LOCK history: HOLDER-LOCK D30 merged with INTEGRATE-PLAYABLE CAST-DEFERRAL, re-recorded on the merged tree with ORACLE_WRITE=1 (header traceHash 0df2d046, timed@2026; main 9d2294c1 timed@2081, HOLDER-LOCK PR d72bac20 timed@2002; HOLDER-LOCK PR alone 82575f9c). main history: PR #370 merged with INTEGRATE-PLAYABLE (#396), re-recorded on the merged tree (ORACLE_WRITE=1): header traceHash 4f1aac04, timed@2197 (PR #370 alone e821b3fd timed@2315; main alone 9d2294c1 timed@2081). SPELL-QUEUE PR #370 (was e87ad70a); ZERO-GCD + NO-PRESS-MID-CAST (f-80b5cf16) merged with ENEMY-ENGAGE, re-recorded on the merged tree (was d7b7304f; ZERO-GCD + NO-PRESS-MID-CAST alone 888c2c8e). INTEGRATE-PLAYABLE CAST-DEFERRAL: re-recorded with #325's deferral for frozen-TS steps, header traceHash 9d2294c1, timed@2071 -> timed@2081 (was 6f856fd1). INTEGRATE-PLAYABLE #325 re-fold (6d620f62): re-recorded with the StartCast busy guard, header traceHash 6f856fd1, timed@2172 -> timed@2071 (the boss no longer replaces its cleave mid-bar; was e90cdc28). Before that, INTEGRATE-PLAYABLE: the literal follows the trace recorded on the integrated tree (e19bc046, header traceHash e90cdc28; was d7b7304f, timed@2172 unchanged). ENEMY-ENGAGE merged with SLAM-TELEGRAPH, re-recorded on the merged tree (timed@2172; ENEMY-ENGAGE alone f4a4073a timed@2230, SLAM-TELEGRAPH + QUENCH-REFUND alone 5791f0ff timed@2172); SLAM-TELEGRAPH alone c076efdb, QUENCH-REFUND alone 15f41e2e; before both 3b33f4ad, DEAD-CASTER-TELEGRAPH (was 8c7e9098), CAST-RANGE-RECHECK (the t1760 seed.lash on actor 1 at 2093 mm now misses; was 867dee64), KF3-SEED-ENGAGE (was e1286ac0; SLAM-TELEGRAPH B1 was 27a3ceaf); equals the TS manifest MERGE-362 parent history (its literal "736f595c"): MERGE-362 (#362 tank survivability, healer press, Mending 6000, TS blind port merged with main's CADENCE-SETUP #415 and #344) re-recorded on the merged tree with ORACLE_WRITE=1: header traceHash 736f595c timed@1955 (#362 alone, D-ORACLE-BLIND-PARITY: 6e1b5236 timed@1836, was d5c20907 / 9d2294c1; main alone aa65cde8 timed@2074).

    [Fact(DisplayName = "bot oracle > anti-vacuity: with no roles the bots diverge from the trace")]
    public void NoRolesDiverge()
    {
        var r = Drive("bots-hv-k2-s7", RoleSet.None);
        Assert.NotNull(r.Failure);
        Assert.True(r.Checked < r.Total);
    }
}
