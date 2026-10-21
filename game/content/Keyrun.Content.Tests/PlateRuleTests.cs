using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The name plate height rule (<see cref="PlateRule"/>, PLATE-HEIGHT): every plate sits <see cref="PlateRule.ClearanceMm"/>
/// above the higher of its row's target height and its scaled model top (native height x scale permille / 1000, the
/// same truncation as <see cref="ActorBindingRow.StandingHeightMm"/>), and a boss plate never sits above
/// <see cref="PlateRule.BossCapMm"/>. Integer math only.
/// </summary>
public sealed class PlateRuleTests
{
    private static ActorBinding Table => ActorBinding.Default;

    [Theory]
    [InlineData("warden", 2000)]
    [InlineData("cinder", 2050)]
    [InlineData("chanter", 2000)]
    [InlineData("choir.melee", 2050)]
    [InlineData("choir.lieutenant", 2050)]
    [InlineData("pair.melee", 2050)]
    [InlineData("seed.elite", 2590)]
    [InlineData("hollowWarden.twinA", 2320)]
    [InlineData("hollowWarden.twinB", 2320)]
    [InlineData("hollowWarden.boss", 4937)]
    public void Each_row_plate_sits_at_its_rule_height(string classId, int expectedMm)
    {
        Assert.Equal(expectedMm, Table.For(classId).PlateHeightMm);
    }

    [Fact]
    public void Every_row_plate_clears_its_scaled_model_top_by_the_clearance()
    {
        ActorBindingRow[] rows = Table.Rows.Append(ActorBinding.Capsule).ToArray();
        Assert.True(rows.Length >= 11, "vacuity guard: only " + rows.Length + " rows");
        foreach (ActorBindingRow row in rows)
        {
            int top = row.StandingHeightMm;
            Assert.True(row.PlateHeightMm >= top + PlateRule.ClearanceMm,
                $"{row.Key}: plate at {row.PlateHeightMm} mm, scaled top {top} mm, clearance under {PlateRule.ClearanceMm} mm");
            Assert.True(row.PlateHeightMm >= row.TargetHeightMm + PlateRule.ClearanceMm,
                $"{row.Key}: plate at {row.PlateHeightMm} mm is under its {row.TargetHeightMm} mm target plus the clearance");
        }
    }

    [Fact]
    public void No_row_plate_floats_more_than_the_clearance_above_its_body()
    {
        foreach (ActorBindingRow row in Table.Rows.Append(ActorBinding.Capsule))
        {
            int body = System.Math.Max(row.TargetHeightMm, row.StandingHeightMm);
            Assert.True(row.PlateHeightMm <= body + PlateRule.ClearanceMm,
                $"{row.Key}: plate at {row.PlateHeightMm} mm floats over its {body} mm body by more than {PlateRule.ClearanceMm} mm");
        }
    }

    [Fact]
    public void The_mage_plate_sits_just_above_the_hat()
    {
        // Mage.glb native 3003 mm (hat tip) x 599 permille = 1798 mm; target 1800 mm; plate 1800 + 250.
        Assert.Equal(1798, Table.For("cinder").StandingHeightMm);
        Assert.Equal(2050, PlateRule.PlateHeightMm(1800, 3003, 599, boss: false));
        Assert.Equal(2048, PlateRule.PlateHeightMm(0, 3003, 599, boss: false));
    }

    [Fact]
    public void The_boss_plate_sits_above_its_helmet_under_the_cap()
    {
        ActorBindingRow boss = Table.For("hollowWarden.boss");
        Assert.True(boss.Boss);
        Assert.Equal(4937, boss.PlateHeightMm);
        Assert.True(boss.PlateHeightMm <= PlateRule.BossCapMm);
        Assert.False(Table.For("warden").Boss);
    }

    [Fact]
    public void A_boss_plate_never_rises_above_the_cap()
    {
        Assert.Equal(5000, PlateRule.BossCapMm);
        Assert.Equal(5000, PlateRule.PlateHeightMm(1800, 6000, 1000, boss: true));
        Assert.Equal(6250, PlateRule.PlateHeightMm(1800, 6000, 1000, boss: false));
    }

    [Fact]
    public void The_scaled_top_truncates_like_standing_height()
    {
        // 2166 x 831 / 1000 = 1799.946 -> 1799 (not rounded to 1800).
        Assert.Equal(2049, PlateRule.PlateHeightMm(0, 2166, 831, boss: false));
        Assert.Equal(PlateRule.ScaledTopMm(2166, 831), Table.For("pair.melee").StandingHeightMm);
    }

    // f-07f1b477: on the boss pull the Hollow Warden's plate ("The Hollow Warden", capped 5 m up) and the two Sundered
    // Twin plates beside it collided, and HudLayout.Separate stacked all three in a column over the boss model. The boss
    // is named on its unit frame, so its plate does not draw over its head as well (the OverheadCast rule, for plates).
    private const int Boss = 900;
    private const int TwinA = 901;
    private const int TwinB = 902;

    [Fact]
    public void The_enemy_on_the_boss_frame_draws_no_overhead_plate()
    {
        Assert.False(PlateRule.DrawsOverhead(Boss, alive: true, human: false, bossFrameActorId: Boss));
    }

    [Fact]
    public void The_Twins_beside_the_framed_boss_keep_their_overhead_plates()
    {
        Assert.True(PlateRule.DrawsOverhead(TwinA, alive: true, human: false, bossFrameActorId: Boss));
        Assert.True(PlateRule.DrawsOverhead(TwinB, alive: true, human: false, bossFrameActorId: Boss));
    }

    [Fact]
    public void With_no_boss_frame_every_living_non_human_actor_draws_its_plate()
    {
        Assert.True(PlateRule.DrawsOverhead(Boss, alive: true, human: false, bossFrameActorId: null));
        Assert.True(PlateRule.DrawsOverhead(101, alive: true, human: false, bossFrameActorId: null));
    }

    [Fact]
    public void The_human_and_the_dead_never_draw_a_plate()
    {
        Assert.False(PlateRule.DrawsOverhead(1, alive: true, human: true, bossFrameActorId: Boss));
        Assert.False(PlateRule.DrawsOverhead(TwinA, alive: false, human: false, bossFrameActorId: Boss));
    }

    // f-77a68e6b: from Pull 1 the plates of pulls far down the dungeon ("The Seed", the Paired Warrior crowd) drew over
    // the current fight. WoW hides enemy plates past the nameplate max distance; an enemy plate now draws only within
    // PlateRule.PlateDistanceMm of the viewing player. Arena numbers (game/content/data/layout.json and
    // content/data/dungeons/hollow-vault.json, mm on x): each room's party start to its own pull is 7000; the start to
    // the next room's doorway is 17500 (choir), 25500 (pair), 15500 (seed); the start to the pull after that is 29000
    // (pair from choir), 37000 (seed from pair), 27000 (boss from seed).
    private const int Cantor = 201;

    [Fact]
    public void The_plate_distance_is_twenty_six_metres()
    {
        Assert.Equal(26000, PlateRule.PlateDistanceMm);
    }

    [Fact]
    public void An_alive_enemy_in_plate_range_draws_its_plate()
    {
        Assert.True(PlateRule.DrawsOverhead(Cantor, alive: true, human: false, bossFrameActorId: null, enemy: true, distanceMm: 7000));
        Assert.True(PlateRule.DrawsOverhead(TwinA, alive: true, human: false, bossFrameActorId: Boss, enemy: true, distanceMm: 0));
    }

    [Fact]
    public void An_alive_enemy_out_of_plate_range_draws_no_plate()
    {
        // From the Choir party start (14500, 0): the Paired Warriors at (43500, 0) are 29000 away, The Seed 59000.
        Assert.False(PlateRule.DrawsOverhead(Cantor, alive: true, human: false, bossFrameActorId: null, enemy: true, distanceMm: 29000));
        Assert.False(PlateRule.DrawsOverhead(Cantor, alive: true, human: false, bossFrameActorId: null, enemy: true, distanceMm: 59000));
        Assert.False(PlateRule.DrawsOverhead(TwinA, alive: true, human: false, bossFrameActorId: Boss, enemy: true, distanceMm: 27000));
    }

    [Fact]
    public void The_plate_range_boundary_is_inclusive()
    {
        Assert.True(PlateRule.DrawsOverhead(Cantor, alive: true, human: false, bossFrameActorId: null, enemy: true, distanceMm: PlateRule.PlateDistanceMm));
        Assert.False(PlateRule.DrawsOverhead(Cantor, alive: true, human: false, bossFrameActorId: null, enemy: true, distanceMm: PlateRule.PlateDistanceMm + 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7000)]
    [InlineData(26000)]
    [InlineData(59000)]
    public void The_boss_frame_enemy_never_draws_a_plate_at_any_distance(long distanceMm)
    {
        Assert.False(PlateRule.DrawsOverhead(Boss, alive: true, human: false, bossFrameActorId: Boss, enemy: true, distanceMm: distanceMm));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7000)]
    [InlineData(59000)]
    public void The_dead_never_draw_a_plate_at_any_distance(long distanceMm)
    {
        Assert.False(PlateRule.DrawsOverhead(Cantor, alive: false, human: false, bossFrameActorId: null, enemy: true, distanceMm: distanceMm));
    }

    [Fact]
    public void Party_plates_do_not_cull_by_distance()
    {
        // The human party stays as it was: an ally bot's plate draws at any distance, the played character's never.
        Assert.True(PlateRule.DrawsOverhead(2, alive: true, human: false, bossFrameActorId: null, enemy: false, distanceMm: 59000));
        Assert.False(PlateRule.DrawsOverhead(1, alive: true, human: true, bossFrameActorId: null, enemy: false, distanceMm: 0));
    }

    [Fact]
    public void The_arena_puts_the_next_doorway_inside_and_the_pull_after_outside()
    {
        // (party start, next room's doorway, the pull after that), mm on x, from layout.json and hollow-vault.json.
        (int start, int door, int nextPull)[] rooms =
        {
            (14500, 32000, 43500), // choir: pair-hall door, the Paired Warriors (origin 38500 + spawn 5000)
            (36500, 62000, 73500), // pair: seed-chamber door, The Seed (origin 63500 + spawn 10000)
            (66500, 82000, 93500), // seed: warden-arena door, the Hollow Warden (origin 93500)
        };
        foreach (var (start, door, nextPull) in rooms)
        {
            Assert.True(PlateRule.InPlateRange(door - start), $"door {door} from start {start} is out of plate range");
            Assert.False(PlateRule.InPlateRange(nextPull - start), $"pull {nextPull} from start {start} is in plate range");
        }
    }
}
