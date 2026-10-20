using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane PRIVATE-RATING (Matt 2026-10-20, f-a3230c59: "private WoW-style M+ rating shown only on the player's own
/// screen, never to others and never used to filter groups"; BIBLE s0 "no shareable score to judge by"). The rating is
/// the sum over dungeons of each dungeon's best TIMED run, scored by key level and clear time in integers. It lives
/// only in the local profile file and on the between-runs Screen; the log line the Screen prints about it carries no
/// number, and no other Keyrun.Content type takes or returns it.
/// </summary>
public sealed class PrivateRatingTests
{
    private const string Vault = "hollow-vault";
    private const int Par = 14400;

    private static TimedRun Run(int key, int clear, string dungeon = Vault) => new(dungeon, key, clear, Par);

    [Fact]
    public void A_key_cleared_at_par_scores_its_level_base()
    {
        Assert.Equal(155, PrivateRating.Score(Run(2, Par)));
        Assert.Equal(170, PrivateRating.Score(Run(3, Par)));
        Assert.Equal(275, PrivateRating.Score(Run(10, Par)));
        Assert.Equal(155, PrivateRating.Score(Run(2, Par + 300))); // timed on Slack past par: the base, no penalty
    }

    [Fact]
    public void A_faster_clear_scores_more_but_never_past_the_next_key_at_par()
    {
        Assert.Equal(155 + 7, PrivateRating.Score(Run(2, Par * 8 / 10))); // 20% under par: half of 15, floored
        Assert.Equal(155 + 15, PrivateRating.Score(Run(2, Par * 6 / 10))); // 40% under par: the full step
        Assert.Equal(155 + 15, PrivateRating.Score(Run(2, 1))); // capped
        Assert.True(PrivateRating.Score(Run(2, 1)) <= PrivateRating.Score(Run(3, Par)));
    }

    [Fact]
    public void Only_a_timed_run_counts()
    {
        Assert.NotNull(PrivateRating.FromResult(true, 9000, Vault, 4, Par));
        Assert.Null(PrivateRating.FromResult(false, 9000, Vault, 4, Par)); // depleted or abandoned
        Assert.Null(PrivateRating.FromResult(true, null, Vault, 4, Par)); // still running
        Assert.Equal(new TimedRun(Vault, 4, 9000, Par), PrivateRating.FromResult(true, 9000, Vault, 4, Par));
        Assert.Equal(0, PrivateRecord.Empty.Add(new TimedRun(Vault, 1, 9000, Par)).Rating); // below key 2 scores nothing
    }

    [Fact]
    public void The_rating_sums_the_best_timed_run_of_each_dungeon()
    {
        var r = PrivateRecord.Empty;
        Assert.Equal(0, r.Rating);
        r = r.Add(Run(3, Par)); // 170
        Assert.Equal(170, r.Rating);
        r = r.Add(Run(2, Par)); // worse: the best stays
        Assert.Equal(170, r.Rating);
        r = r.Add(Run(3, Par * 6 / 10)); // better: 185 replaces 170
        Assert.Equal(185, r.Rating);
        r = r.Add(Run(5, Par, "ember-crypt")); // another dungeon adds its own best: 200
        Assert.Equal(385, r.Rating);
        Assert.Equal(2, r.Best.Count);
        Assert.Equal(3, r.Best[Vault].KeyLevel);
    }

    [Fact]
    public void The_record_round_trips_through_the_local_profile_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "keyrun-private-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, PrivateRecord.FileName);
            var (none, noneError) = PrivateRecord.LoadFile(path);
            Assert.Null(noneError);
            Assert.Equal(0, none.Rating);

            var r = PrivateRecord.Empty.Add(Run(4, Par * 9 / 10)).Add(Run(6, Par, "ember-crypt"));
            Assert.Null(PrivateRecord.SaveFile(path, r));
            var (back, error) = PrivateRecord.LoadFile(path);
            Assert.Null(error);
            Assert.Equal(r.Rating, back.Rating);
            Assert.Equal(r.ToJson(), back.ToJson());
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"best\":[]}")]
    [InlineData("{\"version\":1,\"best\":[{\"dungeon\":\"hollow-vault\",\"key\":-3,\"clearTicks\":100,\"parTicks\":14400}]}")]
    [InlineData("{\"version\":1,\"best\":[{\"dungeon\":\"a\",\"key\":3,\"clearTicks\":1,\"parTicks\":9},{\"dungeon\":\"a\",\"key\":4,\"clearTicks\":1,\"parTicks\":9}]}")]
    public void A_bad_profile_file_reads_as_no_rating_plus_a_reason(string text)
    {
        var (record, error) = PrivateRecord.Parse(text);
        Assert.Equal(0, record.Rating);
        Assert.NotNull(error);
    }

    [Fact]
    public void The_screen_shows_the_number_but_the_log_line_never_carries_it()
    {
        var r = PrivateRecord.Empty.Add(Run(7, Par * 7 / 10)).Add(Run(9, Par, "ember-crypt"));
        var rating = r.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(r.Rating > 400, "vacuity guard: a distinctive rating");
        Assert.Contains(rating, r.ScreenText(), StringComparison.Ordinal);
        var log = r.LogLine();
        Assert.StartsWith("SCREEN rating", log, StringComparison.Ordinal);
        Assert.DoesNotContain(rating, log, StringComparison.Ordinal);
        foreach (var best in r.Best.Values)
        {
            Assert.DoesNotContain(PrivateRating.Score(best).ToString(System.Globalization.CultureInfo.InvariantCulture), log, StringComparison.Ordinal);
        }
    }

    // No Keyrun.Content type outside the rating's own takes, holds or returns it: nothing the queue, the run options or
    // the party adapter builds can carry it. The control type proves the scan sees such a member.
    private sealed record LeakyQueueControl(string DungeonId, int KeyLevel, PrivateRecord Rating);

    [Fact]
    public void No_other_content_type_carries_the_rating()
    {
        Type[] ratingTypes = { typeof(PrivateRecord), typeof(TimedRun), typeof(PrivateRating) };
        Assert.NotEmpty(Leaks(new[] { typeof(LeakyQueueControl) }, ratingTypes));
        var types = typeof(PrivateRecord).Assembly.GetTypes().Where(t => !ratingTypes.Contains(t) && !IsNestedIn(t, ratingTypes)).ToArray();
        Assert.True(types.Length > 50, "vacuity guard: only " + types.Length + " types scanned");
        var leaks = Leaks(types, ratingTypes);
        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }

    private static bool IsNestedIn(Type t, Type[] outers) =>
        t.DeclaringType is { } d && (outers.Contains(d) || IsNestedIn(d, outers));

    private static bool Mentions(Type t, Type[] ratingTypes)
    {
        var s = Nullable.GetUnderlyingType(t) ?? (t.HasElementType ? t.GetElementType()! : t);
        return ratingTypes.Contains(s) || (s.IsGenericType && s.GetGenericArguments().Any(a => Mentions(a, ratingTypes)));
    }

    private static System.Collections.Generic.List<string> Leaks(Type[] types, Type[] ratingTypes)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var leaks = new System.Collections.Generic.List<string>();
        foreach (var t in types)
        {
            leaks.AddRange(t.GetFields(all).Where(f => Mentions(f.FieldType, ratingTypes)).Select(f => t.FullName + "." + f.Name));
            leaks.AddRange(t.GetProperties(all).Where(p => Mentions(p.PropertyType, ratingTypes)).Select(p => t.FullName + "." + p.Name));
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                if ((m is MethodInfo mi && Mentions(mi.ReturnType, ratingTypes)) || m.GetParameters().Any(p => Mentions(p.ParameterType, ratingTypes)))
                {
                    leaks.Add(t.FullName + "." + m.Name);
                }
            }
        }
        return leaks;
    }
}
