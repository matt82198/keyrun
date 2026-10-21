using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The overhead label rule (<see cref="LabelGroups"/>, LABEL-CULL): a label is drawn only for a living actor the plate
/// rule draws, a dead actor is in no group and no "xN" count, and a respawned actor regains its label. Party labels
/// keep their rule (no distance cull, no count for distinct names).
/// </summary>
public sealed class LabelGroupsTests
{
    private const int Far = 60000;

    [Fact]
    public void GroupOfThreeWithOneDead_ShowsX2_AndNeverCountsTheDeadMember()
    {
        var roster = new[]
        {
            new LabelActor(201, "Paired Warrior", Alive: true, Enemy: true, Human: false, DistanceMm: 0),
            new LabelActor(202, "Paired Warrior", Alive: false, Enemy: true, Human: false, DistanceMm: 0),
            new LabelActor(203, "Paired Warrior", Alive: true, Enemy: true, Human: false, DistanceMm: 0),
        };

        var groups = LabelGroups.Build(roster, bossFrameActorId: null);

        var group = Assert.Single(groups);
        Assert.Equal("Paired Warrior x2", group.Text);
        Assert.Equal(new[] { 201, 203 }, group.ActorIds);
    }

    [Fact]
    public void SingleDeadActor_ShowsNoLabel()
    {
        var roster = new[]
        {
            new LabelActor(301, "Paired Warrior", Alive: false, Enemy: true, Human: false, DistanceMm: 0),
        };

        Assert.Empty(LabelGroups.Build(roster, bossFrameActorId: null));
    }

    [Fact]
    public void ActorThatRespawnsRegainsItsLabel()
    {
        var dead = new[] { new LabelActor(401, "Hall Sentry", Alive: false, Enemy: true, Human: false, DistanceMm: 0) };
        var alive = new[] { new LabelActor(401, "Hall Sentry", Alive: true, Enemy: true, Human: false, DistanceMm: 0) };

        Assert.Empty(LabelGroups.Build(dead, bossFrameActorId: null));
        var group = Assert.Single(LabelGroups.Build(alive, bossFrameActorId: null));
        Assert.Equal("Hall Sentry", group.Text);
        Assert.Equal(new[] { 401 }, group.ActorIds);
    }

    [Fact]
    public void PartyLabelsUnaffected_HumanHasNoOverheadLabel_LivingPartyMemberKeepsLabelAtAnyDistance()
    {
        var roster = new[]
        {
            new LabelActor(1, "Warden", Alive: true, Enemy: false, Human: true, DistanceMm: 0),
            new LabelActor(2, "Chanter", Alive: true, Enemy: false, Human: false, DistanceMm: Far),
            new LabelActor(3, "Cinder", Alive: false, Enemy: false, Human: false, DistanceMm: 0),
        };

        var groups = LabelGroups.Build(roster, bossFrameActorId: null);

        var group = Assert.Single(groups);
        Assert.Equal("Chanter", group.Text);
        Assert.Equal(new[] { 2 }, group.ActorIds);
    }

    [Fact]
    public void EnemyOutOfPlateRange_AndTheBossFrameEnemy_AreNotCountedOrLabelled()
    {
        var roster = new[]
        {
            new LabelActor(501, "Hall Sentry", Alive: true, Enemy: true, Human: false, DistanceMm: 0),
            new LabelActor(502, "Hall Sentry", Alive: true, Enemy: true, Human: false, DistanceMm: Far),
            new LabelActor(503, "Hall Sentry", Alive: true, Enemy: true, Human: false, DistanceMm: 0),
            new LabelActor(900, "Hollow Warden", Alive: true, Enemy: true, Human: false, DistanceMm: 0),
        };

        var group = Assert.Single(LabelGroups.Build(roster, bossFrameActorId: 900));

        Assert.Equal("Hall Sentry x2", group.Text);
        Assert.Equal(new[] { 501, 503 }, group.ActorIds);
    }
}
