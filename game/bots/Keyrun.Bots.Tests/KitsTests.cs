using System;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The real content registry (tests/bots/real-content-setup.ts <c>setContentRegistry(loadContent())</c>). The TS
/// injects it into module state; the port holds it in a <see cref="KitRegistry"/> that views carry.
/// </summary>
public static class RealContent
{
    private static readonly Lazy<KitRegistry> Lazy = new(() => KitRegistry.From(ContentLoader.Load()));

    /// <summary>A KitRegistry over <c>ContentLoader.Load()</c>, built once per test run.</summary>
    public static KitRegistry Kits => Lazy.Value;
}

/// <summary>Port of tests/bots/kits.test.ts (8 cases).</summary>
public sealed class KitsTests
{
    private static readonly KitRegistry Kits = RealContent.Kits;

    [Fact(DisplayName = "getClassKit > returns null for a classId content doesn't define")]
    public void NullForUnknownClass() => Assert.Null(Kits.GetClassKit("priest"));

    [Fact(DisplayName = "getClassKit > builds Warden's kit: two Setup, two Payoff, the real interrupt/defensive/movement ids, no dispel")]
    public void WardenKit()
    {
        var kit = Kits.GetClassKit("warden");
        Assert.NotNull(kit);
        Assert.Equal(LoopKind.BuilderSpender, kit!.LoopKind);
        Assert.Equal(new[] { "warden.strike", "warden.bulwark" }, kit.SetupAbilityIds);
        Assert.Equal(new[] { "warden.guardBreak", "warden.reckoning" }, kit.PayoffAbilityIds);
        Assert.Equal("sundered", kit.WindowIdBySetupAbility["warden.strike"]);
        Assert.Equal("bulwark", kit.WindowIdBySetupAbility["warden.bulwark"]);
        Assert.Equal("warden.shieldBash", kit.InterruptAbilityId);
        Assert.Equal("warden.brace", kit.DefensiveAbilityId);
        Assert.Equal("warden.charge", kit.MovementAbilityId);
        Assert.Null(kit.DispelAbilityId);
        Assert.Equal("warden.vigil", kit.SelfSustainAbilityId);
    }

    [Fact(DisplayName = "getClassKit > builds Cinder's kit: Cold Snap is the sole resource dump, Purge is the dispel")]
    public void CinderKit()
    {
        var kit = Kits.GetClassKit("cinder");
        Assert.Equal(LoopKind.HeatOverheat, kit?.LoopKind);
        Assert.Equal(new[] { "cinder.coldSnap" }, kit?.ResourceDumpAbilityIds);
        Assert.Equal("cinder.purge", kit?.DispelAbilityId);
        Assert.Equal(30, kit?.CostByAbility["cinder.detonate"]);
        Assert.Equal(-100, kit?.ResourceGainByAbility["cinder.coldSnap"]);
    }

    [Fact(DisplayName = "getClassKit > builds Chanter's kit: Refrain swaps the stance resource and is the heal-triage ability")]
    public void ChanterKit()
    {
        var kit = Kits.GetClassKit("chanter");
        Assert.Equal(LoopKind.Stance, kit?.LoopKind);
        Assert.Equal("chanter.refrain", kit?.StanceSwapAbilityId);
        Assert.Equal("chanter.refrain", kit?.HealAbilityId);
        Assert.Equal("chanter.cleanse", kit?.DispelAbilityId);
    }

    [Fact(DisplayName = "getClassKit > rotation priority: a resource-cost finisher outranks a Payoff, which outranks a Setup, which outranks filler")]
    public void RotationPriority()
    {
        var rot = Kits.GetClassKit("cinder")?.Rotation ?? Array.Empty<string>();
        int At(string id)
        {
            for (var i = 0; i < rot.Count; i++)
            {
                if (rot[i] == id)
                {
                    return i;
                }
            }
            return -1;
        }
        Assert.True(At("cinder.detonate") < At("cinder.coldSnap"));
        Assert.True(At("cinder.coldSnap") < At("cinder.flare"));
        Assert.True(At("cinder.flare") < At("cinder.emberBolt"));
    }

    [Fact(DisplayName = "getClassKit > memoises: repeated calls for the same classId return the identical object")]
    public void Memoises() => Assert.Same(Kits.GetClassKit("warden"), Kits.GetClassKit("warden"));

    [Fact(DisplayName = "resolveKit > prefers content's real kit when classId matches a starter class")]
    public void ResolvePrefersContent()
    {
        var kit = Kits.ResolveKit("warden", ClassKitView.Empty);
        Assert.Equal("warden.shieldBash", kit.InterruptAbilityId);
        Assert.Equal(new[] { "warden.strike", "warden.bulwark" }, kit.SetupAbilityIds);
    }

    [Fact(DisplayName = "resolveKit > falls back to BotView.classKit's own fields for a scripted-test placeholder classId")]
    public void ResolveFallsBack()
    {
        var kit = Kits.ResolveKit("warrior", new ClassKitView(new[] { "filler" }, "kick", "wall", null));
        Assert.Equal(new[] { "filler" }, kit.Rotation);
        Assert.Equal("kick", kit.InterruptAbilityId);
        Assert.Equal("wall", kit.DefensiveAbilityId);
        Assert.Empty(kit.SetupAbilityIds);
        Assert.Empty(kit.PayoffAbilityIds);
        Assert.Null(kit.LoopKind);
    }
}

/// <summary>
/// Port of tests/bots/heal-pick.test.ts, case 1 of 7 (the kit-heals.ts classification). Cases 2 to 7 call
/// roles/healer.ts healerDecide and belong to lane 3B.
/// </summary>
public sealed class HealPickTests
{
    [Fact(DisplayName = "Chanter shape-aware heals > classifies Refrain as self, Chorus as group (5000mm), Cadence as the targeted heal")]
    public void ClassifiesChanterHeals()
    {
        var expected = new HealKit("chanter.refrain", "chanter.chorus", 5000, "chanter.cadence")
        {
            SecondGroupHeal = new SecondGroupHeal("chanter.hymn", 2500),
        };
        Assert.Equal(expected, RealContent.Kits.GetClassKit("chanter")?.Heals);
    }
}
