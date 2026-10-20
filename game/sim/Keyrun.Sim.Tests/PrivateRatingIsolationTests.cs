using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane PRIVATE-RATING (Matt 2026-10-20, f-a3230c59: the M+ rating is "shown only on the player's own screen, never to
/// others and never used to filter groups"; BIBLE s0 "no shareable score to judge by"). The rating is
/// Keyrun.Content.PrivateRecord, which Keyrun.Sim cannot reference. Everything another seat or the queue reads is a
/// Keyrun.Sim value: the queue payload (<see cref="QueueRequest"/>), backfill matching (<see cref="Backfill"/>), the run
/// options, and the run trace (every <see cref="SimEvent"/> and the per-tick projection, which the run log writes as its
/// EVENT lines). These tests pin that none of them can carry a rating: the sim assembly does not reference the
/// content assembly, no sim member is named for a rating or score, the queue payload serializes to exactly its key,
/// dungeon and Clause, and a real queued run's trace carries no rating or score field.
/// </summary>
public sealed class PrivateRatingIsolationTests
{
    private static readonly string[] Banned = { "rating", "score", "mplus" };

    private static bool IsBanned(string name) =>
        Banned.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase));

    private static List<string> NamedLeaks(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var leaks = new List<string>();
        foreach (var t in types)
        {
            if (IsBanned(t.Name))
            {
                leaks.Add(t.FullName!);
            }
            leaks.AddRange(t.GetFields(all).Where(f => IsBanned(f.Name)).Select(f => t.FullName + "." + f.Name));
            leaks.AddRange(t.GetProperties(all).Where(p => IsBanned(p.Name)).Select(p => t.FullName + "." + p.Name));
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                if (IsBanned(m.Name) || m.GetParameters().Any(p => IsBanned(p.Name ?? string.Empty)))
                {
                    leaks.Add(t.FullName + "." + m.Name);
                }
            }
        }
        return leaks;
    }

    private sealed record LeakyQueueControl(string DungeonId, int KeyLevel, int MplusRating);

    [Fact(DisplayName = "Private rating: the sim and the replay tools cannot reference the rating's assembly")]
    public void Sim_cannot_see_the_rating_type()
    {
        foreach (var asm in new[] { typeof(QueueRequest).Assembly, typeof(RunSetup).Assembly })
        {
            var refs = asm.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.DoesNotContain("Keyrun.Content", refs);
        }
    }

    [Fact(DisplayName = "Private rating: no queue, backfill, run or trace member is named for a rating or score")]
    public void No_sim_member_is_named_for_a_rating()
    {
        Assert.NotEmpty(NamedLeaks(new[] { typeof(LeakyQueueControl) }));
        var types = typeof(QueueRequest).Assembly.GetTypes().Concat(typeof(RunSetup).Assembly.GetTypes()).ToArray();
        Assert.True(types.Length > 50, "vacuity guard: only " + types.Length + " types scanned");
        Assert.Contains(typeof(QueueRequest), types);
        Assert.Contains(typeof(Backfill), types);
        Assert.Contains(typeof(SimEvent), types);
        var leaks = NamedLeaks(types);
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }

    [Fact(DisplayName = "Private rating: the queue payload serializes to its dungeon, key, Clause and build (talents, gear) only")]
    public void Queue_payload_is_key_dungeon_and_clause_only()
    {
        // Loadouts (#255, talents) and Gear (#314, BIBLE s7 summed gear stats) are the queued build, never a rating.
        // Gear is populated so its serialized members are scanned for a rating or score name as well.
        var request = new QueueRequest("hollow-vault", 7, "tide") { Gear = new Dictionary<int, GearStats> { [1] = new GearStats(500, -300) } };
        var json = JsonSerializer.Serialize(request);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "ClauseId", "DungeonId", "Gear", "KeyLevel", "Loadouts" }, names);
        Assert.Contains("DamageDoneBp", json);
        Assert.False(IsBanned(json));
    }

    [Fact(DisplayName = "Private rating: a queued run's trace and log lines carry no rating or score field")]
    public void Queued_run_trace_has_no_rating()
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 7);
        var request = new QueueRequest("hollow-vault", 3, "tide");
        var run = RunRoot.CreateRun(7, setup.Party, setup.Dungeon, setup.Content, request.ToRunOptions());
        var lines = new List<string>();
        for (var t = 0; t < 600; t++)
        {
            var step = RunRoot.StepRun(run, Array.Empty<Command>());
            run = step.Run;
            lines.AddRange(step.Events.Select(e => e.ToCanonicalJson()));
            lines.Add(Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks));
        }
        Assert.True(lines.Count > 600, "vacuity guard: the trace has events beyond the projections");
        var leaked = lines.Where(IsBanned).Take(3).ToList();
        Assert.True(leaked.Count == 0, string.Join(Environment.NewLine, leaked));
    }
}
