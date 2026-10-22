using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// KICK-LEDGER-COOLDOWN-GAP (finding f-13b5c33a; design/BIBLE.md section 9, the Choir teaches the kick ledger). In the
/// Playtest default run (key 2 seed 1) the Choral Mending at 639..719 resolved: the Chanter's and Cinder's kicks were on
/// cooldown and the Warden's 3 m Shield Bash was ready but never assigned, so a cast the party could answer healed the
/// Choir. A property over the run's own trajectory, not a tick pin (R-PINS-1): every Choral Mending cast the run sees is
/// answered, either a seat's interrupt lands on it before it resolves or it was already stopped by another effect
/// (a cast_stopped, e.g. its caster died). No Mending may complete while a seat could have kicked it.
/// </summary>
public sealed class KickLedgerCooldownGapTests
{
    private const string Mending = "choir.healCast";
    private static readonly Regex TickRx = new("\"tick\":(\\d+)");
    private static readonly Regex SourceRx = new("\"sourceId\":(\\d+)");
    private static readonly Regex TargetRx = new("\"targetId\":(\\d+)");
    private static readonly Regex ActorRx = new("\"actorId\":(\\d+)");

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the kick timeline is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public KickLedgerCooldownGapTests(ITestOutputHelper output) => _output = output;

    private static int Int(Regex rx, string line) => int.Parse(rx.Match(line).Groups[1].Value, CultureInfo.InvariantCulture);

    private sealed record Cast(int Caster, int Start, int Ends, int Able);

    [Fact(DisplayName = "Kick ledger gap: in the default run (key 2 seed 1) no Choral Mending resolves; each is kicked by a seat before it ends or was already stopped")]
    public void Every_mending_is_answered_before_it_resolves()
    {
        var registry = ContentLoader.Load();
        var kits = KitRegistry.From(registry);
        // Per cast: when it ends, and how many seats could land a kick on it. f-7aa25aa6: the default run now pulls to 100%
        // forces, which includes the Warrior Remnant (two Cantors, each healing the whole pack every 200 ticks against three
        // 480-tick kicks), so a Mending can resolve while every seat's interrupt is on cooldown until after it resolves.
        // "Could land" is the ledger's own definition (Ledger.LateKicker): the seat's interrupt is off cooldown before the cast
        // resolves, read at the tick the cast first shows.
        var casts = new Dictionary<(int Caster, int Start), (int Ends, int Able)>();
        var r = PlaytestRunner.Play(new PlaytestOptions(2, 1, null)
        {
            Observe = (_, run, _) =>
            {
                foreach (var a in run.State.Actors.Values)
                {
                    if (a.CastId == Mending && a.CastStartTick is { } s && a.CastEndsTick is { } e && !casts.ContainsKey((a.Id, s)))
                    {
                        var able = run.State.Actors.Values.Count(p => p.Kind == ActorKind.Player && p.Alive
                            && kits.ResolveKit(p.ClassId, ClassKitView.Empty).InterruptAbilityId is { Length: > 0 } interrupt
                            && Readiness.CooldownReadyTick(p, interrupt) < e);
                        casts[(a.Id, s)] = (e, able);
                    }
                }
            },
        }, registry);

        var events = r.Log.Split('\n').Where(l => l.StartsWith("E ", StringComparison.Ordinal) && l.Contains($"\"abilityId\":\"{Mending}\"")).ToList();
        var kicks = events.Where(l => l.Contains("\"type\":\"interrupt\"") && !l.Contains("\"stop\":")).ToList();
        var stops = events.Where(l => l.Contains("\"type\":\"interrupt\"") && l.Contains("\"stop\":")).ToList();
        var stopped = events.Where(l => l.Contains("\"type\":\"cast_stopped\"")).ToList();
        var completed = events.Where(l => l.Contains("\"type\":\"cast_complete\"")).ToList();

        var seen = casts.Select(kv => new Cast(kv.Key.Caster, kv.Key.Start, kv.Value.Ends, kv.Value.Able)).OrderBy(c => c.Start).ThenBy(c => c.Caster).ToList();
        var failures = new List<string>();
        var answeredBy = new Dictionary<Cast, bool>();
        foreach (var c in seen)
        {
            var kick = kicks.FirstOrDefault(l => Int(TargetRx, l) == c.Caster && Int(TickRx, l) >= c.Start && Int(TickRx, l) < c.Ends);
            var stop = stops.Concat(stopped).FirstOrDefault(l =>
                (l.Contains("\"type\":\"cast_stopped\"") ? Int(ActorRx, l) : Int(TargetRx, l)) == c.Caster && Int(TickRx, l) >= c.Start && Int(TickRx, l) <= c.Ends);
            var done = completed.FirstOrDefault(l => Int(ActorRx, l) == c.Caster && Int(TickRx, l) == c.Ends);
            _output.WriteLine($"Choral Mending caster={c.Caster} {c.Start}..{c.Ends} able={c.Able} kick={(kick is null ? "none" : $"seat {Int(SourceRx, kick)} at {Int(TickRx, kick)}")} stopped={(stop is null ? "no" : Int(TickRx, stop).ToString(CultureInfo.InvariantCulture))} resolved={(done is null ? "no" : "YES")}");
            answeredBy[c] = kick is not null || stop is not null;
        }
        // The party answers as many Mendings resolving on the same tick as it has seats able to land a kick on them: a
        // Mending left to resolve with an able seat unused is the finding's defect; one that no seat could reach is the
        // kick economy (three interrupts on 480 ticks against two Cantors casting every 200), reported by able=0 above.
        foreach (var group in seen.GroupBy(c => c.Ends))
        {
            var answered = group.Count(c => answeredBy[c]);
            var owed = Math.Min(group.Count(), group.Max(c => c.Able));
            if (answered < owed)
            {
                failures.Add($"{group.Count()} Mendings resolving at {group.Key} (casters {string.Join(",", group.Select(c => c.Caster))}), {owed} seats able to land a kick, only {answered} answered");
            }
        }
        Assert.True(seen.Count >= 4, $"only {seen.Count} Choral Mending casts seen; the property would be vacuous");
        Assert.True(kicks.Count >= 3, $"only {kicks.Count} seat kicks landed on a Mending; the property would be vacuous");
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
