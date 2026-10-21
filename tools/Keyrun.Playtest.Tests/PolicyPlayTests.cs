using System;
using System.Linq;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// A policy seat does only what its plan says (Matt 2026-10-20, verbatim: "ensure the bots can fail lol, they should
/// not clear the first dungeon on the first try unless they have played wow"): no plan, no kicks; a kick rule kicks
/// with exactly the classes it names; the hands are seeded (same seed, same run).
/// </summary>
public sealed class PolicyPlayTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static PlaytestResult Play(string policyJson, long seed = 1, int key = 2, int maxTicks = 6000) =>
        PlaytestRunner.Play(new PlaytestOptions(key, seed, PolicyReader.Parse(policyJson, Registry)) { MaxTicks = maxTicks }, Registry);

    [Fact(DisplayName = "Policy play: an empty plan never kicks, so every Choir heal cast resolves")]
    public void Empty_plan_never_kicks()
    {
        var r = Play("{}");
        Assert.All(r.Stats.KickPresses.Values, n => Assert.Equal(0, n));
        Assert.Empty(r.Stats.Interrupted);
        Assert.True(r.Stats.InterruptibleResolved.TryGetValue("choir.healCast", out var healed) && healed > 0, "no heal cast resolved");
    }

    [Fact(DisplayName = "Policy play: the scripted party (no plan) does kick the same pull")]
    public void Scripted_party_kicks()
    {
        var r = PlaytestRunner.Play(new PlaytestOptions(2, 1, null) { MaxTicks = 6000 }, Registry);
        Assert.True(r.Stats.KickPresses.Values.Sum() > 0, "the scripted party never kicked");
        Assert.True(r.Stats.Interrupted.ContainsKey("choir.healCast"));
    }

    [Fact(DisplayName = "Policy play: a kick rule kicks with exactly the classes it names")]
    public void Kick_rule_uses_only_named_kickers()
    {
        var r = Play("{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\"]}]}");
        Assert.Equal(0, r.Stats.KickPresses[1]);
        Assert.Equal(0, r.Stats.KickPresses[2]);
        Assert.True(r.Stats.KickPresses[3] > 0, "the cinder never pressed its kick");
        Assert.True(r.Stats.KicksLanded[3] > 0, "the cinder never landed a kick");
        Assert.True(r.Stats.Interrupted.TryGetValue("choir.healCast", out var n) && n > 0);
    }

    [Fact(DisplayName = "Policy play: a mitigate plan's tank presses Brace and Bulwark under pressure; without it neither (STATE.md D48)")]
    public void Mitigation_only_by_plan()
    {
        static int Pressed(PlaytestResult r, string id) =>
            r.Stats.CastsBySeat.TryGetValue(1, out var casts) && casts.TryGetValue(id, out var n) ? n : 0;
        var bare = Play("{}");
        var mitigating = Play("{\"mitigate\": [\"warden\"]}");
        Assert.Equal(0, Pressed(bare, "warden.brace"));
        Assert.Equal(0, Pressed(bare, "warden.bulwark"));
        Assert.True(Pressed(mitigating, "warden.brace") > 0, "the mitigating tank never pressed Brace");
        Assert.True(Pressed(mitigating, "warden.bulwark") > 0, "the mitigating tank never pressed Bulwark");
    }

    [Fact(DisplayName = "Policy play: a dodge plan moves seats out of telegraphs; without it nobody steps out")]
    public void Dodge_only_by_plan()
    {
        // Both plans kick the Choir heal so the party reaches the telegraphed pulls.
        const string kicks = "\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]";
        var still = Play("{" + kicks + "}", maxTicks: 12000);
        var dodging = Play("{" + kicks + ", \"dodge\": {\"telegraphs\": true}}", maxTicks: 12000);
        Assert.True(still.Stats.Pulls.Count >= 2, "the party never reached a telegraphed pull");
        Assert.All(still.Stats.Dodges.Values, n => Assert.Equal(0, n));
        Assert.True(dodging.Stats.Dodges.Values.Sum() > 0, "the dodge plan never stepped out of a telegraph");
        Assert.NotEqual(still.Log, dodging.Log);
    }

    [Fact(DisplayName = "Policy play: the hands are seeded (same seed and plan, same log; another seed, another log)")]
    public void Hands_are_seeded()
    {
        const string plan = "{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\"]}]}";
        var a = Play(plan, seed: 3, maxTicks: 3000);
        var b = Play(plan, seed: 3, maxTicks: 3000);
        var c = Play(plan, seed: 4, maxTicks: 3000);
        Assert.Equal(a.Log, b.Log);
        Assert.NotEqual(a.Log, c.Log);
    }

    [Fact(DisplayName = "Hands: reaction delays stay inside their bounds and the rolls are deterministic")]
    public void Hands_bounds()
    {
        var h1 = new Hands(9, 2);
        var h2 = new Hands(9, 2);
        for (var i = 0; i < 2000; i++)
        {
            var r = h1.React();
            Assert.InRange(r, Hands.ReactBaseTicks, Hands.ReactBaseTicks + Hands.ReactSpreadTicks - 1);
            Assert.Equal(r, h2.React());
            var k = h1.KickReact();
            Assert.InRange(k, Hands.KickReactBaseTicks, Hands.KickReactBaseTicks + Hands.KickReactSpreadTicks - 1);
            Assert.Equal(k, h2.KickReact());
        }
        var misses = Enumerable.Range(0, 10000).Count(_ => h1.Roll(Hands.MissPermille));
        Assert.InRange(misses, 600, 1000);
    }
}
