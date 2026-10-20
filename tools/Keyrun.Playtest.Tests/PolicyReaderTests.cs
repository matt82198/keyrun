using System.Linq;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>The policy file is read fail-closed: every field it names is checked against the content.</summary>
public sealed class PolicyReaderTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private const string Full = """
        {
          "reason": "kick the heal, dodge the floor",
          "talents": { "warden": { "warden.strike": "rend" } },
          "kicks": [ { "cast": "choir.healCast", "kickers": ["cinder", "chanter"] } ],
          "dodge": { "telegraphs": true, "floor": false },
          "defensive": { "warden": 35, "cinder": 50 },
          "utility": ["chanter"],
          "hold": { "chanter.chorus": ["seed", "boss"] },
          "focus": ["choir.lieutenant"],
          "pull": { "restHealthPct": 80 }
        }
        """;

    [Fact(DisplayName = "Policy: every field of a full file is read")]
    public void Full_file_reads_every_field()
    {
        var p = PolicyReader.Parse(Full, Registry);
        Assert.Equal("kick the heal, dodge the floor", p.Reason);
        Assert.Equal("rend", p.Talents["warden"]["warden.strike"]);
        var kick = Assert.Single(p.Kicks);
        Assert.Equal("choir.healCast", kick.Cast);
        Assert.Equal(new[] { "cinder", "chanter" }, kick.Kickers);
        Assert.Equal(new[] { "cinder", "chanter" }, p.KickersFor("choir.healCast"));
        Assert.Null(p.KickersFor("pair.spreadMark"));
        Assert.True(p.DodgeTelegraphs);
        Assert.False(p.DodgeFloor);
        Assert.Equal(35, p.Defensive["warden"]);
        Assert.Equal(50, p.Defensive["cinder"]);
        Assert.Equal(new[] { "chanter" }, p.Utility);
        Assert.Equal(new[] { "seed", "boss" }, p.Hold["chanter.chorus"]);
        Assert.Equal(new[] { "choir.lieutenant" }, p.Focus);
        Assert.Equal(80, p.RestHealthPct);
    }

    [Fact(DisplayName = "Policy: an empty file switches every skill off (no hidden defaults)")]
    public void Empty_file_is_all_off()
    {
        var p = PolicyReader.Parse("{}", Registry);
        Assert.Empty(p.Kicks);
        Assert.False(p.DodgeTelegraphs);
        Assert.False(p.DodgeFloor);
        Assert.Empty(p.Defensive);
        Assert.Empty(p.Utility);
        Assert.Empty(p.Hold);
        Assert.Empty(p.Focus);
        Assert.Empty(p.Talents);
        Assert.Equal(0, p.RestHealthPct);
    }

    [Theory(DisplayName = "Policy: a bad field or id fails closed with a message naming it")]
    [InlineData("{\"kick\": []}", "unknown field \"kick\"")]
    [InlineData("{\"talents\": {\"warden\": {\"warden.strike\": \"nope\"}}}", "unknown option \"nope\"")]
    [InlineData("{\"talents\": {\"warden\": {\"warden.taunt\": \"rend\"}}}", "not one of warden's talent forks")]
    [InlineData("{\"talents\": {\"rogue\": {}}}", "unknown class \"rogue\"")]
    [InlineData("{\"kicks\": [{\"cast\": \"choir.meleeSwing\", \"kickers\": [\"warden\"]}]}", "cannot be interrupted")]
    [InlineData("{\"kicks\": [{\"cast\": \"warden.strike\", \"kickers\": [\"warden\"]}]}", "not an enemy ability")]
    [InlineData("{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": []}]}", "names no kicker")]
    [InlineData("{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"warden\"], \"when\": 1}]}", "unknown field \"when\"")]
    [InlineData("{\"dodge\": {\"telegraphs\": \"yes\"}}", "must be true or false")]
    [InlineData("{\"defensive\": {\"warden\": 0}}", "1-99")]
    [InlineData("{\"hold\": {\"warden.strike\": [\"vault\"]}}", "unknown pull \"vault\"")]
    [InlineData("{\"hold\": {\"choir.healCast\": [\"boss\"]}}", "not a player ability")]
    [InlineData("{\"focus\": [\"choir.cantor\"]}", "unknown enemy \"choir.cantor\"")]
    [InlineData("{\"pull\": {\"restHealthPct\": 101}}", "0-100")]
    [InlineData("[1, 2]", "one JSON object")]
    [InlineData("{ not json", "not valid JSON")]
    public void Bad_input_fails_closed(string json, string message)
    {
        var e = Assert.Throws<PolicyException>(() => PolicyReader.Parse(json, Registry));
        Assert.Contains(message, e.Message, System.StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Policy: talents patch the picked fork's ability in the run content")]
    public void Talents_patch_the_ability()
    {
        var p = PolicyReader.Parse("{\"talents\": {\"warden\": {\"warden.strike\": \"rend\"}}}", Registry);
        var patched = PlaytestRunner.WithTalents(Registry, p);
        var window = patched.Abilities["warden.strike"].Effects.OfType<WindowOp>().Single();
        var baseWindow = Registry.Abilities["warden.strike"].Effects.OfType<WindowOp>().Single();
        Assert.Equal(60, baseWindow.DurationTicks);
        Assert.Equal(100, window.DurationTicks);
        Assert.Same(Registry, PlaytestRunner.WithTalents(Registry, PolicyReader.Parse("{}", Registry)));
    }
}
