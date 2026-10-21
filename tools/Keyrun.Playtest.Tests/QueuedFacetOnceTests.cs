using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-494ea8ef (BIBLE s7: a Facet changes what its ability does, once). A queued run goes Screen.Queue
/// (<see cref="QueuedKit.Queue"/>, the request) then SimDriver.StartQueuedRun (<see cref="HostRunSetup.StartFor"/>, which
/// stacks every equipped Facet's patch on the request's kit through <see cref="RunStart.Of"/>) then
/// <see cref="RunRoot.CreateRun"/>. Before the fix the request already carried the Facet-patched kit, so RunStart.Of
/// patched it again and an appended op ran twice: Chilling Bash put two "slowed" applications on Shield Bash.
/// </summary>
public sealed class QueuedFacetOnceTests
{
    private const string ClassId = "warden";
    private const string FacetId = "warden.armor.chillingBash";
    private const string AbilityId = "warden.shieldBash";

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    [Fact(DisplayName = "Queued run: an equipped Facet's appended op is in the created run exactly once")]
    public void A_queued_run_applies_each_equipped_facet_once()
    {
        var facet = Registry.Facets[FacetId];
        var progression = Progression.Start(1).Take(new Item("reward-armor-t1-0", "armor", 1, new Facet(facet.Id, facet.Text, facet.AbilityId), Rewards.StartingCharges));
        var picks = TalentPicks.Start(Registry.Classes[ClassId]).Pick(0, 0);
        var state = ScreenState.Open("hollow-vault", Registry.Clauses.Keys.ToList(), 2);

        var (_, request) = QueuedKit.Queue(state, Registry, picks, progression, ClassId);
        var setup = HostRunSetup.Build(Registry, request!.DungeonId, request.KeyLevel, ClassId, withBots: false);
        var start = setup.StartFor(request, progression, Registry, ClassId);
        var run = RunRoot.CreateRun(42, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);

        static int Slows(S.AbilityDef d) => d.Effects.Count(op => op is S.ApplyEffectOp { EffectId: "slowed" });
        var baseSlows = Slows(setup.Content.Abilities[AbilityId]);
        Assert.Equal(new[] { FacetId }, start.Gear!.Facets.Select(f => f.Id));
        Assert.Empty(start.Gear.UnappliedFacetIds);
        Assert.Equal(baseSlows + 1, Slows(run.Content.Abilities[AbilityId]));
        Assert.Equal(setup.Content.Abilities[AbilityId].Effects.Count + 1, run.Content.Abilities[AbilityId].Effects.Count);

        // The talent layer still reaches the run under the Facet layer: every talented ability the request carries is
        // what the run plays (no talent touches Shield Bash, so its Facet is the only change there).
        var talented = request.Loadouts![setup.HumanActorId];
        Assert.NotEmpty(talented);
        Assert.DoesNotContain(AbilityId, talented.Keys);
        foreach (var (id, def) in talented)
        {
            Assert.Equal(def.Effects.Count, run.Content.Abilities[id].Effects.Count);
        }
    }
}
