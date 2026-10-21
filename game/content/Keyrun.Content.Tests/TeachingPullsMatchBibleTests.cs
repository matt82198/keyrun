using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The three teaching pulls and the boss, pack by pack, as design/BIBLE.md section 9 states them (finding f-82d9390e,
/// Matt 2026-10-20: "room design is lame ... not following the bible"). Each assertion quotes the bible line it pins:
/// 9.2's point table (members and counts), 9.3's kickable-caster table (Choral Mending), and 9.6's lessons (the Choir
/// teaches the kick, the Pair teaches spread then stack, the Seed teaches the boss's core rule at no stakes). Counts the
/// bible does not state are not asserted here. Drives the real loader (<see cref="ContentLoader.Load()"/>), so a drift in
/// content/data/dungeons/hollow-vault.json, the enemy files or the ability table turns this red.
/// </summary>
public sealed class TeachingPullsMatchBibleTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static DungeonDef Vault => Reg.Dungeons["hollow-vault"];

    private static PullDef Pull(string id) => Vault.Pulls.Single(p => p.Id == id);

    /// <summary>Enemy id to how many the pull spawns, summed over its spawn groups.</summary>
    private static Dictionary<string, int> Members(PullDef pull) =>
        pull.Enemies.GroupBy(e => e.EnemyId).ToDictionary(g => g.Key, g => g.Sum(e => e.Count));

    private static IEnumerable<AbilityDef> ScriptedAbilities(string enemyId) =>
        Reg.Enemies[enemyId].Script.Select(s => Reg.Abilities[s.Cast]);

    /// <summary>The tick a script step first fires: a loop first fires at <c>every</c>, an integer step at its tick.</summary>
    private static int FirstFire(ScriptStep step) => step.At.Kind == ScriptAtKind.Loop ? step.Every!.Value : step.At.Tick;

    [Fact]
    public void The_dungeon_has_exactly_the_three_teaching_pulls_in_bible_order()
    {
        // BIBLE 9.1: "Entrance -> Choir room (pull 1) -> Pair hall (pull 2) -> ... -> Seed chamber (pull 3)".
        Assert.Equal(new[] { "choir", "pair", "seed" }, Vault.Pulls.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void The_Choir_is_two_Choir_Acolytes_and_one_Choir_Cantor()
    {
        // BIBLE 9.2: "Choir (pull 1) | critical path | 2 Choir Acolyte (40), 1 Choir Cantor (80) | 160 (16%)".
        // BIBLE 9.6: "two melee and a caster lieutenant".
        var members = Members(Pull("choir"));
        Assert.Equal(new Dictionary<string, int> { ["choir.melee"] = 2, ["choir.lieutenant"] = 1 }, members);
        Assert.Equal("Choir Acolyte", Reg.Enemies["choir.melee"].Name);
        Assert.Equal(EnemyRole.Melee, Reg.Enemies["choir.melee"].Role);
        Assert.Equal("Choir Cantor", Reg.Enemies["choir.lieutenant"].Name);
        Assert.Equal(EnemyRole.Lieutenant, Reg.Enemies["choir.lieutenant"].Role);
        Assert.Equal(160, RouteForces.PullPoints(Reg.Forces, Pull("choir")));
    }

    [Fact]
    public void The_Choir_Cantor_casts_a_4_second_kickable_pack_heal_and_it_is_the_Choirs_only_kick()
    {
        // BIBLE 9.6: "a caster lieutenant whose 4-second heal cast must be kicked; teaches the kick ledger".
        // BIBLE 9.3: "Choir Cantor | Choral Mending | 80 ticks (4 s) | 200 ticks | ... | Heals its pack." and "One caster
        // casting every 9-12 s can be kicked every time, which is the Choir's lesson."
        var kickable = Members(Pull("choir")).Keys
            .SelectMany(id => ScriptedAbilities(id).Where(a => a.Interruptible).Select(a => (Enemy: id, Ability: a)))
            .Distinct()
            .ToList();
        var (caster, mending) = Assert.Single(kickable);
        Assert.Equal("choir.lieutenant", caster);
        Assert.Equal("Choral Mending", mending.Name);
        Assert.Equal(80, mending.CastTicks);
        Assert.Equal(200, mending.CooldownTicks);
        Assert.Contains(mending.Effects, op => op is HealOp);
        Assert.Equal(ShapeKind.Circle, mending.Shape.Kind); // an area around the Cantor: it heals its pack

        ScriptStep step = Reg.Enemies["choir.lieutenant"].Script.Single(s => s.Cast == mending.Id);
        Assert.InRange(step.Every!.Value, 180, 240); // every 9-12 s at 20 Hz
    }

    [Fact]
    public void The_Pair_is_exactly_five_Paired_Warriors_with_no_kickable_cast()
    {
        // BIBLE 9.2: "Pair (pull 2) | critical path | 5 Paired Warrior (52) | 260 (26%)". BIBLE 9.6: "five melee".
        var members = Members(Pull("pair"));
        Assert.Equal(new Dictionary<string, int> { ["pair.melee"] = 5 }, members);
        EnemyDef warrior = Reg.Enemies["pair.melee"];
        Assert.Equal("Paired Warrior", warrior.Name);
        Assert.Equal(EnemyRole.Melee, warrior.Role);
        Assert.DoesNotContain(ScriptedAbilities("pair.melee"), a => a.Interruptible); // the Pair teaches shape, not kicks
        Assert.Equal(260, RouteForces.PullPoints(Reg.Forces, Pull("pair")));
    }

    [Fact]
    public void The_Pair_marks_one_player_to_spread_every_20_s_then_calls_a_stack_circle_ten_seconds_later()
    {
        // BIBLE 9.6: "every 20 s one player is marked to spread (a ring: stand apart), ten seconds later the pack calls a
        // stack (a circle: stand together, damage split). Teaches reading shape."
        EnemyDef warrior = Reg.Enemies["pair.melee"];
        ScriptStep spread = warrior.Script.Single(s => s.Cast == "pair.spreadMark");
        ScriptStep stack = warrior.Script.Single(s => s.Cast == "pair.stackCall");

        Assert.Equal(400, spread.Every); // every 20 s
        Assert.Equal(400, stack.Every);
        Assert.Equal(200, FirstFire(stack) - FirstFire(spread)); // stack ten seconds after the spread

        AbilityDef spreadMark = Reg.Abilities["pair.spreadMark"];
        AbilityDef stackCall = Reg.Abilities["pair.stackCall"];
        Assert.Equal(ShapeKind.Target, spreadMark.Shape.Kind); // one player is marked
        Assert.Equal(ShapeKind.Circle, stackCall.Shape.Kind); // the pack calls the stack
        Assert.Contains(spreadMark.Effects, op => op is ApplyEffectOp { EffectId: "spreadMarked" });
        Assert.Contains(stackCall.Effects, op => op is ApplyEffectOp { EffectId: "stackMarked" });

        PairMarksConfig marks = Reg.Run.PairMarks;
        Assert.Equal("spreadMarked", marks.SpreadEffectId);
        Assert.Equal("stackMarked", marks.StackEffectId);
        Assert.True(marks.SpreadRadiusMm > marks.StackRadiusMm, "spread asks the party apart, stack asks it together");
    }

    [Fact]
    public void The_Seed_is_one_elite_that_drops_a_floor_Window_on_death()
    {
        // BIBLE 9.2: "Seed (pull 3) | critical path | 1 Seed elite (180) | 180 (18%)". BIBLE 9.6: "an elite that on death
        // drops a Window on the floor".
        var members = Members(Pull("seed"));
        Assert.Equal(new Dictionary<string, int> { ["seed.elite"] = 1 }, members);
        EnemyDef seed = Reg.Enemies["seed.elite"];
        Assert.Equal(EnemyRole.Elite, seed.Role);
        Assert.Equal(180, RouteForces.PullPoints(Reg.Forces, Pull("seed")));

        ScriptStep onDeath = Assert.Single(seed.Script, s => s.At.Kind == ScriptAtKind.OnDeath);
        WindowOp bloom = Assert.Single(Reg.Abilities[onDeath.Cast].Effects.OfType<WindowOp>());
        Assert.False(bloom.OnTarget); // on the floor, not on an actor
    }

    [Fact]
    public void The_Seed_demonstrates_the_Wardens_core_rule_with_the_same_alternating_self_Window()
    {
        // BIBLE 9.6: the Seed "teaches the boss's core rule with no stakes". The boss's core rule is the Lock: "a Window on
        // itself ... Only a Combo closes it ... the party must alternate who sets and who pays".
        WindowOp BossWindow(string enemyId) =>
            Reg.Enemies[enemyId].Script.SelectMany(s => Reg.Abilities[s.Cast].Effects.OfType<WindowOp>())
                .Single(w => w.Self == true);

        WindowOp boss = BossWindow("hollowWarden.boss");
        WindowOp seed = BossWindow("seed.elite");
        Assert.True(boss.Alternate, "the Warden's Lock alternates who sets and who pays");
        Assert.Equal(boss.OnTarget, seed.OnTarget);
        Assert.Equal(boss.Self, seed.Self);
        Assert.Equal(boss.Alternate, seed.Alternate);
        Assert.Equal(boss.SetupClass, seed.SetupClass);
        Assert.Equal(boss.DurationTicks, seed.DurationTicks); // the same 3 s Window
    }

    [Fact]
    public void No_teaching_pull_fields_a_boss_and_the_boss_is_the_Hollow_Warden_with_its_three_mechanics()
    {
        // BIBLE 9.6: "Boss: the Hollow Warden. Three mechanics, sequenced, never layered": Cleave, the Sundered Twins,
        // the Lock. BIBLE 9.2: "The boss, the Sundered Twins and Echo-spawned adds are worth 0".
        Assert.DoesNotContain(Vault.Pulls.SelectMany(p => p.Enemies), e => Reg.Enemies[e.EnemyId].Role == EnemyRole.Boss);
        Assert.Equal("hollowWarden.boss", Vault.Boss.EnemyId);
        Assert.Equal(EnemyRole.Boss, Reg.Enemies[Vault.Boss.EnemyId].Role);
        Assert.Equal(new[] { "cleave", "sundered-twins", "the-lock" }, Vault.Boss.Mechanics.ToArray());

        // The Twins share one Break bar, defined on the boss.
        var bar = Assert.Single(Reg.Enemies["hollowWarden.boss"].BreakBars!);
        Assert.Equal(bar.Id, Reg.Enemies["hollowWarden.twinA"].BreakBarId);
        Assert.Equal(bar.Id, Reg.Enemies["hollowWarden.twinB"].BreakBarId);

        foreach (var id in new[] { "hollowWarden.boss", "hollowWarden.twinA", "hollowWarden.twinB" })
        {
            Assert.Equal(0, Reg.Forces.PointsOf(id));
        }
    }

    [Fact]
    public void The_three_teaching_pulls_give_60_percent_so_no_critical_path_alone_fills_the_bar()
    {
        // BIBLE 9.2: "The three teaching pulls give 60%, so no run reaches 100% on the critical path alone".
        Assert.Equal(1000, Reg.Forces.Required);
        Assert.Equal(600, RouteForces.CriticalPathPoints(Reg.Forces, Vault));
    }
}
