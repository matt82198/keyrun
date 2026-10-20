using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Keyrun.Content;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane PRIVATE-RATING, BIBLE s10 "No exported score" (Matt 2026-10-20: the private Mythic+ rating "is never shown to
/// others and never used to filter groups") and BIBLE test 3 "no shareable score to judge by". Matching and the queue
/// live in the sim (<see cref="QueueRequest"/>, <see cref="RunOptions"/>, <see cref="Backfill"/>): no member of any sim
/// type takes, holds or returns a rating type, so nothing the queue or another seat reads can carry it. The control
/// type proves the scan sees such a member.
/// </summary>
public sealed class PrivateRatingIsolationTests
{
    private static readonly Type[] RatingTypes = { typeof(PrivateRecord), typeof(RatedRun), typeof(StripEntry), typeof(MythicRating), typeof(RunClear) };

    private static bool Mentions(Type t)
    {
        var s = Nullable.GetUnderlyingType(t) ?? (t.IsArray ? t.GetElementType()! : t);
        return RatingTypes.Contains(s) || (s.IsGenericType && s.GetGenericArguments().Any(Mentions));
    }

    private static List<string> Leaks(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var leaks = new List<string>();
        foreach (var t in types)
        {
            leaks.AddRange(t.GetFields(all).Where(f => Mentions(f.FieldType)).Select(f => t.FullName + "." + f.Name));
            leaks.AddRange(t.GetProperties(all).Where(p => Mentions(p.PropertyType)).Select(p => t.FullName + "." + p.Name));
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                if ((m is MethodInfo mi && Mentions(mi.ReturnType)) || m.GetParameters().Any(p => Mentions(p.ParameterType)))
                {
                    leaks.Add(t.FullName + "." + m.Name);
                }
            }
        }
        return leaks;
    }

    private sealed record LeakyQueue(string DungeonId, int KeyLevel, PrivateRecord Rating);

    [Fact]
    public void The_queue_and_matching_types_take_no_rating()
    {
        Assert.NotEmpty(Leaks(new[] { typeof(LeakyQueue) }));
        foreach (var t in new[] { typeof(QueueRequest), typeof(RunOptions), typeof(ScreenState), typeof(Backfill) })
        {
            Assert.Empty(Leaks(new[] { t }));
        }
        var queueCtor = typeof(QueueRequest).GetConstructors().Single(c => c.GetParameters().Length == 3);
        Assert.Equal(new[] { typeof(string), typeof(int), typeof(string) }, queueCtor.GetParameters().Select(p => p.ParameterType).ToArray());
    }

    [Fact]
    public void No_sim_type_carries_the_rating()
    {
        var types = typeof(QueueRequest).Assembly.GetTypes();
        Assert.True(types.Length > 50, "vacuity guard: only " + types.Length + " types scanned");
        var leaks = Leaks(types);
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }
}
