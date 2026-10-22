using Keyrun.Bots.Shared;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Finding f-022c6905 (G2.1 toolkit L2 follow-up): an ability whose op is the content <see cref="C.DisplaceOp"/> (the
/// caster mobility op from keyrun #645, TOOLKIT-CONTRACT lane L2, ruling R-TOOLKIT-2) must convert to the sim's
/// <see cref="Keyrun.Sim.Combat.DisplaceOp"/> under the bots' op map. Before the fix <see cref="ContentCombos.ToSimOp"/>
/// had no arm for it and threw "has an op the sim does not run", so a mobility ability failed run setup.
/// </summary>
public sealed class DisplaceOpMapTests
{
    [Fact(DisplayName = "ContentCombos.ToSimOp maps a content DisplaceOp to the sim DisplaceOp with its fields")]
    public void A_content_displace_op_converts_to_the_sim_displace_op()
    {
        var content = new C.DisplaceOp { DistanceMm = 8000, FreesBinds = true, TowardTarget = false };

        var sim = ContentCombos.ToSimOp(content, "test.blink");

        Assert.Equal(new Keyrun.Sim.Combat.DisplaceOp(8000, FreesBinds: true, TowardTarget: false), sim);
    }

    [Fact(DisplayName = "ContentCombos.ToSimOp maps a toward-target DisplaceOp with defaults preserved")]
    public void A_toward_target_displace_op_keeps_its_flags()
    {
        var content = new C.DisplaceOp { DistanceMm = 2000, TowardTarget = true };

        var sim = ContentCombos.ToSimOp(content, "test.charge");

        Assert.Equal(new Keyrun.Sim.Combat.DisplaceOp(2000, FreesBinds: false, TowardTarget: true), sim);
    }
}
