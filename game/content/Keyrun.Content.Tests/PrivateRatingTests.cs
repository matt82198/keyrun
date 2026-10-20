using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane PRIVATE-RATING (BIBLE s10 "No exported score", Matt 2026-10-20: "a private WoW-style Mythic+ rating, shown only
/// on the player's own screen. It is never shown to others and never used to filter groups"; s10 "Last-3-runs strip";
/// s8 timer tiers +1 / +2 within 80% of par / +3 within 60% of par). The rating is WoW's Mythic+ score: per dungeon the
/// best run counts, the total is the sum of those bests (Raider.IO FAQ "summing their highest scoring run from each
/// dungeon"), a timed key at par scores the base for its level (+2 = 155, +3 = 170), finishing up to 40% under par adds
/// up to the next level's step, and an untimed clear scores two levels lower and loses more the later it is, zero past
/// 40% over par (warcraft.wiki.gg Mythic+ Rating).
/// </summary>
public sealed class PrivateRatingTests
{
    private const string Vault = "hollow-vault";
    private const int Par = 14400;

    private static RatedRun Timed(int key, int clear, string dungeon = Vault, int deaths = 0) =>
        new(dungeon, key, RunClear.Timed, clear, Par, deaths);

    private static RatedRun Untimed(int key, int clear, string dungeon = Vault, int deaths = 0) =>
        new(dungeon, key, RunClear.Untimed, clear, Par, deaths);

    [Fact]
    public void The_base_for_a_key_is_wows_value_at_par()
    {
        Assert.Equal(155, MythicRating.Base(2));
        Assert.Equal(170, MythicRating.Base(3));
        Assert.Equal(200, MythicRating.Base(5));
        Assert.Equal(275, MythicRating.Base(10));
        Assert.Equal(0, MythicRating.Base(1));
    }

    [Fact]
    public void A_timed_run_scores_the_base_at_par_and_up_to_one_step_more_when_faster()
    {
        Assert.Equal(155, Timed(2, Par).Score);
        Assert.Equal(155 + 7, Timed(2, Par * 8 / 10).Score); // 20% under: half the 15-point window, floored
        Assert.Equal(155 + 15, Timed(2, Par * 6 / 10).Score); // 40% under: the full step
        Assert.Equal(155 + 15, Timed(2, Par / 10).Score); // capped at the next level's step
        Assert.Equal(155, Timed(2, Par + 200).Score); // timed on Slack past par: no bonus, no penalty
    }

    [Fact]
    public void An_untimed_clear_scores_two_levels_lower_and_zero_past_forty_percent_over()
    {
        Assert.Equal(155 - 30, Untimed(2, Par + 1).Score);
        Assert.Equal(155 - 30 - 7, Untimed(2, Par * 12 / 10).Score);
        Assert.Equal(155 - 30 - 15, Untimed(2, Par * 14 / 10).Score);
        Assert.Equal(0, Untimed(2, (Par * 14 / 10) + 1).Score);
        Assert.Equal(MythicRating.Base(3), Untimed(5, Par + 1).Score); // "comparable to a key two levels easier"
    }

    [Fact]
    public void Untimed_always_scores_below_timed_at_the_same_key()
    {
        for (var key = 2; key <= 20; key++)
        {
            var bestUntimed = Untimed(key, Par).Score;
            var worstTimed = Timed(key, Par * 2).Score;
            Assert.True(bestUntimed < worstTimed, $"key {key}: untimed {bestUntimed} vs timed {worstTimed}");
        }
    }

    [Fact]
    public void A_higher_key_scores_more_and_a_faster_clear_scores_more()
    {
        for (var key = 2; key < 20; key++)
        {
            Assert.True(Timed(key + 1, Par).Score > Timed(key, Par).Score, $"key {key}");
            Assert.True(Timed(key + 1, Par).Score >= Timed(key, Par / 2).Score, $"speed never beats a level, key {key}");
        }
        Assert.True(Timed(7, Par * 7 / 10).Score > Timed(7, Par * 9 / 10).Score);
    }

    [Fact]
    public void A_run_that_was_not_cleared_scores_nothing()
    {
        Assert.Equal(0, new RatedRun(Vault, 9, RunClear.NotCleared, 3000, Par, 6).Score);
        Assert.Equal(0, new RatedRun(Vault, 9, RunClear.NotCleared, 3000, Par, 6).Upgrade);
    }

    [Fact]
    public void Timed_upgrade_tiers_follow_the_bible_against_par()
    {
        Assert.Equal(3, Timed(4, Par * 6 / 10).Upgrade);
        Assert.Equal(2, Timed(4, (Par * 6 / 10) + 1).Upgrade);
        Assert.Equal(2, Timed(4, Par * 8 / 10).Upgrade);
        Assert.Equal(1, Timed(4, (Par * 8 / 10) + 1).Upgrade);
        Assert.Equal(1, Timed(4, Par + 300).Upgrade);
        Assert.Equal(0, Untimed(4, Par + 1).Upgrade);
    }

    [Fact]
    public void Only_the_best_run_per_dungeon_counts_and_dungeons_sum()
    {
        var r = PrivateRecord.Empty.Add(Timed(5, Par));
        Assert.Equal(200, r.Rating);
        r = r.Add(Timed(3, Par / 2)); // lower: does not replace the best
        Assert.Equal(200, r.Rating);
        r = r.Add(Untimed(9, Par + 1)); // untimed +9 (230) beats timed +5 (200)
        Assert.Equal(230, r.Rating);
        r = r.Add(Timed(6, Par)); // 215 < 230: the untimed +9 stays the best
        Assert.Equal(230, r.Rating);
        r = r.Add(Timed(2, Par, dungeon: "second-dungeon"));
        Assert.Equal(230 + 155, r.Rating);
        Assert.Equal(2, r.Best.Count);
        Assert.Equal(9, r.Best[Vault].KeyLevel);
    }

    [Fact]
    public void The_strip_keeps_the_newest_three_runs_newest_first_with_what_each_moved()
    {
        var r = PrivateRecord.Empty
            .Add(Timed(2, Par))
            .Add(Timed(3, Par))
            .Add(new RatedRun(Vault, 4, RunClear.NotCleared, 900, Par, 5))
            .Add(Timed(4, Par * 6 / 10, deaths: 1));
        Assert.Equal(PrivateRecord.StripSize, r.Recent.Count);
        Assert.Equal(new[] { 4, 4, 3 }, r.Recent.Select(e => e.Run.KeyLevel).ToArray());
        Assert.Equal(RunClear.Timed, r.Recent[0].Run.Clear);
        Assert.Equal(RunClear.NotCleared, r.Recent[1].Run.Clear);
        Assert.Equal(200 - 170, r.Recent[0].Gain); // +4 at 40% under = 200, replacing +3 at par = 170
        Assert.Equal(0, r.Recent[1].Gain);
        Assert.Equal(170 - 155, r.Recent[2].Gain);
        Assert.Equal(200, r.Rating);
    }

    [Fact]
    public void A_strip_row_reads_like_the_wow_run_history()
    {
        var r = PrivateRecord.Empty.Add(Timed(4, Par * 6 / 10, deaths: 1));
        Assert.Equal("+4  Timed +3  7:12 / 12:00  1 death  +200", PrivateRecord.RowText(r.Recent[0], 20));
        var u = r.Add(Untimed(5, Par + 600, deaths: 4));
        Assert.Equal("+5  Untimed  12:30 / 12:00  4 deaths  +0", PrivateRecord.RowText(u.Recent[0], 20));
        var n = u.Add(new RatedRun(Vault, 6, RunClear.NotCleared, 2400, Par, 5));
        Assert.Equal("+6  Not cleared  2:00 / 12:00  5 deaths", PrivateRecord.RowText(n.Recent[0], 20));
    }

    [Fact]
    public void The_record_round_trips_and_a_bad_save_is_an_empty_record_never_a_throw()
    {
        var r = PrivateRecord.Empty
            .Add(Timed(5, Par * 7 / 10, deaths: 2))
            .Add(Untimed(7, Par + 100))
            .Add(Timed(2, Par, dungeon: "second-dungeon"));
        var back = PrivateRecord.Parse(r.ToJson());
        Assert.Null(back.Error);
        Assert.Equal(r.ToJson(), back.Record.ToJson());
        Assert.Equal(r.Rating, back.Record.Rating);
        Assert.Equal(3, back.Record.Recent.Count);

        foreach (var bad in new[] { null, "", "{", "[]", "{\"version\":2,\"best\":[],\"recent\":[]}",
                     "{\"version\":1,\"best\":[{\"dungeon\":\"x\",\"key\":-1,\"clear\":\"timed\",\"clearTicks\":1,\"parTicks\":1,\"deaths\":0}],\"recent\":[]}" })
        {
            var p = PrivateRecord.Parse(bad);
            Assert.Equal(0, p.Record.Rating);
            Assert.Empty(p.Record.Recent);
            if (!string.IsNullOrEmpty(bad))
            {
                Assert.NotNull(p.Error);
            }
        }
    }

    // BIBLE test 3 "no shareable score to judge by": the rating has no path into matching or any other player's view.
    // Every member of every Content type outside the rating's own family is scanned for a rating type in its fields,
    // properties, method parameters and returns. The control type proves the scan sees such a member.
    private static readonly Type[] RatingTypes = { typeof(PrivateRecord), typeof(RatedRun), typeof(StripEntry), typeof(MythicRating), typeof(RunClear) };

    private static bool Mentions(Type t)
    {
        var s = Nullable.GetUnderlyingType(t) ?? (t.IsArray ? t.GetElementType()! : t);
        if (RatingTypes.Contains(s))
        {
            return true;
        }
        return s.IsGenericType && s.GetGenericArguments().Any(Mentions);
    }

    internal static List<string> Leaks(IEnumerable<Type> types, ICollection<Type> family)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var leaks = new List<string>();
        foreach (var t in types.Where(t => !family.Contains(t) && !(t.DeclaringType is { } d && family.Contains(d))))
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

    private sealed class LeakControl
    {
        public static int Filter(PrivateRecord record) => record.Rating;
    }

    [Fact]
    public void The_rating_has_no_path_into_any_other_content_type()
    {
        Assert.NotEmpty(Leaks(new[] { typeof(LeakControl) }, RatingTypes));
        var types = typeof(ContentLoader).Assembly.GetTypes();
        Assert.True(types.Length > 20, "vacuity guard: only " + types.Length + " types scanned");
        Assert.Contains(typeof(PrivateRecord), types);
        var leaks = Leaks(types, RatingTypes);
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }
}
