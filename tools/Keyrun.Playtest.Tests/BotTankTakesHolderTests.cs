using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-2f6982e9 (PR #601, D44 bots follow the lowest living seat; HOLDER-LOCK D30). The human leads the route, so a pack
/// locks the nearest player, the human, as its holder. The bot Warden, walking behind, must take the pack with a taunt:
/// #601 made the Warden's chase fast and the Cinder seat then wiped at the Seed, because the bot never read a locked
/// enemy as loose (its TargetId still named the tank) and so never taunted. A taunt is single-target with a cooldown,
/// so the invariant is per pack, not per enemy: within <see cref="TakeOverTicks"/> ticks of a pack engaging, the Warden
/// (the tank seat) holds at least one living member of it, whatever the seed. Pre-fix the Warden held none.
/// </summary>
public sealed class BotTankTakesHolderTests(ITestOutputHelper output)
{
    // Walk in (the Warden trails the human by about 2400 mm at 180 mm/tick: 14 ticks) plus the taunt cast, with a
    // generous margin; 20 Hz ticks, so 60 is three seconds.
    private const int TakeOverTicks = 60;

    [Theory(DisplayName = "The bot Warden holds a member of every pack within 60 ticks of it engaging, human Cinder (seeds 1..5)")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Bot_warden_takes_a_pack_member_from_the_human(long seed)
    {
        var reg = Keyrun.Content.ContentLoader.Load();
        const int tankSeat = 2;
        var firstEngaged = new Dictionary<int, int>();
        var checkedPacks = new HashSet<int>();
        var violations = new List<string>();
        var packsSeen = 0;
        var opts = new PlaytestOptions(2, seed, null)
        {
            HumanClassId = "cinder",
            Observe = (tick, run, _) =>
            {
                var engaged = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
                foreach (var e in engaged)
                {
                    firstEngaged.TryAdd(e.Id, tick);
                }
                // A pack is the enemies that engaged on the same tick; it is judged once, TakeOverTicks later.
                foreach (var packTick in firstEngaged.Values.Distinct().Where(t => t + TakeOverTicks == tick && checkedPacks.Add(t)))
                {
                    var living = engaged.Where(e => firstEngaged[e.Id] == packTick).ToList();
                    var tankAlive = run.State.Actors[tankSeat].Alive;
                    if (living.Count == 0 || !tankAlive)
                    {
                        continue;
                    }
                    packsSeen++;
                    if (!living.Any(e => e.HolderId == tankSeat))
                    {
                        violations.Add($"pack engaged at tick {packTick}: {string.Join(",", living.Select(e => $"{e.ClassId}#{e.Id}->holder{e.HolderId}"))} at tick {tick}");
                    }
                }
            },
        };
        PlaytestRunner.Play(opts, reg);
        output.WriteLine($"seed {seed}: {packsSeen} packs judged, {violations.Count} with no member held by the Warden");
        Assert.True(packsSeen > 0, "anti-vacuity: no pack was ever judged");
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }
}
