using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The BIBLE s8 Clause pick (ENGINE-PLAN 3D): the party is offered three of Echo, Tide, Hush and Pact and picks one;
/// exactly one Clause runs, never none and never two.
/// </summary>
public sealed class ClausePickTests
{
    private const string Dungeon = "hollow-vault";

    private static readonly IReadOnlyList<string> Four = new[] { "echo", "tide", "hush", "pact" };

    [Fact]
    public void Offer_is_three_distinct_clauses_of_the_four_on_every_key()
    {
        for (var key = ScreenState.MinKey; key <= ScreenState.MaxKey; key++)
        {
            var offers = ClausePick.Open(Four, Dungeon, key).Offers;
            Assert.Equal(ClausePick.OfferCount, offers.Count);
            Assert.Equal(offers.Count, offers.Distinct(StringComparer.Ordinal).Count());
            Assert.All(offers, id => Assert.Contains(id, Four));
        }
    }

    [Fact]
    public void Offer_is_a_pure_function_of_the_seed_and_ignores_input_order()
    {
        var seed = ClausePick.SeedFor(Dungeon, 7);
        var a = ClausePick.Offer(Four, seed);
        var b = ClausePick.Offer(Four.Reverse().ToList(), seed);
        Assert.Equal(a, b);
        Assert.Equal(ClausePick.SeedFor(Dungeon, 7), seed);
        Assert.NotEqual(ClausePick.SeedFor(Dungeon, 7), ClausePick.SeedFor(Dungeon, 8));
    }

    [Fact]
    public void Offers_vary_with_the_key_so_each_clause_is_left_out_somewhere()
    {
        var leftOut = new HashSet<string>(StringComparer.Ordinal);
        for (var key = ScreenState.MinKey; key <= ScreenState.MaxKey; key++)
        {
            var offers = ClausePick.Open(Four, Dungeon, key).Offers;
            leftOut.UnionWith(Four.Except(offers));
        }
        Assert.Equal(Four.OrderBy(x => x, StringComparer.Ordinal), leftOut.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Open_picks_the_first_offer_so_one_clause_always_runs()
    {
        var choice = ClausePick.Open(Four, Dungeon, 2);
        Assert.Equal(choice.Offers[0], choice.Picked);
        Assert.Equal(choice.Picked, choice.ToRunOptions().ClauseId);
    }

    [Fact]
    public void Pick_takes_an_offered_clause_and_refuses_the_one_not_offered()
    {
        var choice = ClausePick.Open(Four, Dungeon, 5);
        var second = choice.Pick(choice.Offers[1]);
        Assert.NotNull(second);
        Assert.Equal(choice.Offers[1], second!.Picked);
        Assert.Equal(choice.Offers, second.Offers);

        var missing = Four.Except(choice.Offers).Single();
        Assert.Null(choice.Pick(missing));
        Assert.Null(choice.Pick("none"));
    }

    [Fact]
    public void The_picked_clause_is_the_one_clause_the_run_resolves()
    {
        var defs = new Dictionary<string, ClauseDef>(StringComparer.Ordinal)
        {
            ["echo"] = new("echo", "Echo", ClauseKind.Echo, new Dictionary<string, long>(), "d"),
            ["tide"] = new("tide", "Tide", ClauseKind.Tide, new Dictionary<string, long>(), "d"),
            ["hush"] = new("hush", "Hush", ClauseKind.Hush, new Dictionary<string, long>(), "d"),
            ["pact"] = new("pact", "Pact", ClauseKind.Pact, new Dictionary<string, long>(), "d"),
        };
        var content = RunTestKit.Content() with { Clauses = defs };
        foreach (var id in ClausePick.Open(Four, Dungeon, 3).Offers)
        {
            var choice = ClausePick.Open(Four, Dungeon, 3).Pick(id)!;
            var rules = RunOptions.ResolveEncounterRules(content, 3, choice.ToRunOptions().ClauseId);
            Assert.Equal(id, rules.ActiveClauseId);
            Assert.Equal(defs[id].Kind, rules.ActiveClauseKind);
        }
    }

    [Fact]
    public void Offer_fails_closed_with_fewer_than_three_clauses()
    {
        Assert.Throws<ArgumentException>(() => ClausePick.Offer(new[] { "echo", "tide" }, 1));
        Assert.Throws<ArgumentException>(() => ClausePick.Offer(new[] { "echo", "echo", "tide" }, 1));
    }
}
