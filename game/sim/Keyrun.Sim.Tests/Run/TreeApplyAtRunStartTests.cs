// G2.2 design/ABILITY-TREES.md s5.2 (How the sim applies a node: reuse the patch mechanism) and s5.5 test 3 (composition).
// S1 seam under test: RunStart.Of (game/sim/Keyrun.Sim/Run/RunStart.cs) is the run-start path that turns a seat's
// Progression into its RunOptions loadout. It must also compile each unlocked tree node's edit (TreeComposer.ToGearPatch)
// into that seat's ability before the run starts. Today RunStart.Of reads only Progression.Equipped (Facets), never
// Progression.Trees, so these tests are red on the trees-unlocked seats.
// Expected values are computed with the existing composer (TreeComposer.ToGearPatch + GearPatch.ApplyTo), so the file
// compiles against the current public API and fails on assertions, not on missing symbols.
// Content: the node edit is a literal (zap.rank2 and zap.rank3 on the RunStartGearTests base kit). The sim may not read
// Keyrun.Content (R-TREES-1), so game/content/data/trees/warden.json is not loaded here.
using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// ABILITY-TREES s5.2 and s5.5: a seat's unlocked tree nodes are applied to its abilities when the run starts, through
/// <see cref="RunStart.Of"/>. The base kit is the RunStartGearTests zap (one damage op of 1); the node edits are a rank2
/// delta of +1 and a rank3 delta of +3, so an unlocked seat starts with 2 and 4 damage and an untouched seat with 1.
/// </summary>
public sealed class TreeApplyAtRunStartTests
{
    private const int HumanId = 1;
    private const int SecondSeatId = 3;
    private const string ClassId = "warrior";

    private static readonly AbilityDef BaseZap =
        RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);

    private static readonly IReadOnlyDictionary<string, AbilityDef> Abilities =
        new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = BaseZap };

    private static readonly IReadOnlyDictionary<string, GearPatch> NoFacetPatches =
        new Dictionary<string, GearPatch>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, TreeNodeEdit> NodeEdits =
        new Dictionary<string, TreeNodeEdit>(StringComparer.Ordinal)
        {
            ["zap.rank2"] = new TreeNodeEdit(Delta: new EffectDelta("damage", "amount", 1)),
            ["zap.rank3"] = new TreeNodeEdit(Delta: new EffectDelta("damage", "amount", 3)),
        };

    [Fact(DisplayName = "Tree apply: a seat with one unlocked rank node starts the run with that ability carrying the edit")]
    public void A_seat_with_an_unlocked_rank_node_starts_with_the_edit()
    {
        var unlocked = WithTree(Progression.Start(1), new TreeState(1, 1, new[] { "zap.rank2" }));
        long expected = ExpectedDamage(new TreeState(1, 1, new[] { "zap.rank2" }));

        var start = RunStart.Of(null, unlocked, HumanId, Abilities, NoFacetPatches, NodeEdits);
        var run = RunRoot.CreateRun(42, Party(), Dungeon(), RunTestKit.Content(Abilities), start.Options);

        // Red today: RunStart.Of never reads Progression.Trees, so the run starts with the base damage of 1.
        Assert.Equal(expected, DamageOf(run.Content.Abilities["zap"]));
    }

    [Fact(DisplayName = "Tree apply: a seat with no unlocked nodes starts the run with its abilities unchanged (control)")]
    public void A_seat_with_no_unlocked_nodes_starts_unchanged()
    {
        var untouched = WithTree(Progression.Start(1), TreeState.Empty);
        var bare = Progression.Start(1);

        var fromUntouched = RunStart.Of(null, untouched, HumanId, Abilities, NoFacetPatches, NodeEdits);
        var fromBare = RunStart.Of(null, bare, HumanId, Abilities, NoFacetPatches, NodeEdits);
        var run = RunRoot.CreateRun(42, Party(), Dungeon(), RunTestKit.Content(Abilities), fromUntouched.Options);

        Assert.Equal(DamageOf(BaseZap), DamageOf(run.Content.Abilities["zap"]));
        Assert.Equal(DamageOf(BaseZap), SeatDamage(fromBare.Options, HumanId));
        Assert.Equal(DamageOf(BaseZap), SeatDamage(fromUntouched.Options, HumanId));
    }

    [Fact(DisplayName = "Tree apply: two seats of one class with different unlocks start with different abilities in one run")]
    public void Two_seats_of_one_class_with_different_unlocks_get_different_abilities_in_one_run()
    {
        var seatOne = WithTree(Progression.Start(1), new TreeState(1, 1, new[] { "zap.rank2" }));
        var seatTwo = WithTree(Progression.Start(2), new TreeState(1, 1, new[] { "zap.rank3" }));
        long expectedOne = ExpectedDamage(new TreeState(1, 1, new[] { "zap.rank2" }));
        long expectedTwo = ExpectedDamage(new TreeState(1, 1, new[] { "zap.rank3" }));

        // Each seat is started through the production path; the merge into one RunOptions is test-side because
        // RunStart.Of takes a single human actor id.
        var startOne = RunStart.Of(null, seatOne, HumanId, Abilities, NoFacetPatches, NodeEdits);
        var startTwo = RunStart.Of(null, seatTwo, SecondSeatId, Abilities, NoFacetPatches, NodeEdits);
        var merged = MergeLoadouts(startOne.Options, startTwo.Options);
        var run = RunRoot.CreateRun(42, Party(), Dungeon(), RunTestKit.Content(Abilities), merged);

        // Red today: both seats start with the base damage of 1, so each assertion reports Expected/Actual 1.
        Assert.Equal(expectedOne, SeatDamage(merged, HumanId));
        Assert.Equal(expectedTwo, SeatDamage(merged, SecondSeatId));
        Assert.NotEqual(SeatDamage(merged, HumanId), SeatDamage(merged, SecondSeatId));
        Assert.NotNull(run);
    }

    // The composer's result for one tree state, computed with the existing public API (the reference the start must match).
    private static long ExpectedDamage(TreeState state) =>
        DamageOf(TreeComposer.ToGearPatch(state, BaseZap, NodeEdits).ApplyTo(BaseZap));

    private static Progression WithTree(Progression progression, TreeState state) =>
        progression with { Trees = new Dictionary<string, TreeState>(StringComparer.Ordinal) { [ClassId] = state } };

    private static long DamageOf(AbilityDef ability) =>
        ability.Effects.OfType<DamageOp>().Single().Amount;

    // The damage the seat's ability starts with: its loadout entry when the options carry one, else the base kit.
    private static long SeatDamage(RunOptions? options, int seat) =>
        options?.Loadouts is { } loadouts && loadouts.TryGetValue(seat, out var kit) && kit.TryGetValue("zap", out var zap)
            ? DamageOf(zap)
            : DamageOf(BaseZap);

    private static RunOptions? MergeLoadouts(RunOptions? first, RunOptions? second)
    {
        if (first?.Loadouts is null && second?.Loadouts is null)
        {
            return first ?? second;
        }
        var merged = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();
        foreach (var source in new[] { first?.Loadouts, second?.Loadouts })
        {
            if (source is null)
            {
                continue;
            }
            foreach (var kv in source)
            {
                merged[kv.Key] = kv.Value;
            }
        }
        return (first ?? second ?? new RunOptions()) with { Loadouts = merged };
    }

    private static PartyMemberSpec[] Party() => new[]
    {
        RunTestKit.Member(HumanId, ClassId, ActorRole.Damage, -2000, 0, 200, 100),
        RunTestKit.Member(2, "target-dummy", ActorRole.Damage, 3000, 0, 300, 0),
        RunTestKit.Member(SecondSeatId, ClassId, ActorRole.Damage, -2000, 100, 200, 100),
    };

    private static DungeonDef Dungeon() => RunTestKit.OnePull("empty", "p", RunTestKit.Spawn(11, "boss", 0, 0, 1));
}
