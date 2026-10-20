using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;
using EncounterTelegraph = Keyrun.Sim.Encounter.Telegraph;

namespace Keyrun.Bots.Tests;

/// <summary>
/// DEAD-CASTER-TELEGRAPH (design/BIBLE.md section 3: cast bars are the enemy's language, a dead enemy has no cast).
/// A script telegraph is announced leadTicks BEFORE its cast fires (Scripts.cs: <c>FiresAt(step, tick + LeadTicks)</c>),
/// and a dead actor's timed steps never fire (Scripts.cs: <c>if (!actor.Alive) continue;</c>). A telegraph whose caster
/// died before the cast began therefore warns of nothing, yet the sim keeps it in the registry until its resolvesTick
/// (Pair's spreadMark: leadTicks 100). The bots' reflex dodge (and the floor-escape deferral that yields to it) must
/// drop such a telegraph at once. A dead caster still holding a cast in progress (CastId set) keeps its telegraph:
/// Rules completes that cast regardless of the caster's death, so the threat is real.
/// Driven through the real <see cref="BotViews.ToBotTelegraph"/> mapping and the real <see cref="Bot"/> runner.
/// </summary>
public sealed class DeadCasterTelegraphTests
{
    private const int BotId = 1;
    private const int CasterId = 2;
    private const long StepMm = 300; // config BaseSpeedMmPerTick: a 3000 mm circle takes 10 ticks to leave from its centre
    private const int ResolvesTick = 100; // pair.spreadMark leadTicks 100, announced at tick 0
    private const int DeathTick = 3;

    /// <summary>Pair's spreadMark telegraph (content/data/enemies/pair.json: circle radius 3000, leadTicks 100), cast by <see cref="CasterId"/>.</summary>
    private static BotTelegraph SpreadMark() =>
        BotViews.ToBotTelegraph(new EncounterTelegraph(7, CasterId, "pair.spreadMark", "circle", new Vec3(0, 0, 0), 0, ResolvesTick) { Radius = 3000 });

    private static ActorState Caster() => MakeActor(id: CasterId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 0, z: 0);

    private static void Kill(ActorState a)
    {
        // What Damage.ApplyDamage does on a lethal hit (Health 0, Alive false; the "death" event's state).
        a.Health = 0;
        a.Alive = false;
    }

    /// <summary>Runs the bot for ticks 0..<paramref name="lastTick"/>, killing the caster at <see cref="DeathTick"/> when asked; returns the tick of every dodge move.</summary>
    private static System.Collections.Generic.List<int> DodgeTicks(bool casterDies, string? castInProgress, int lastTick, out bool insideAtDeath)
    {
        var bot = Bot.MakeBot();
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var self = MakeActor(id: BotId, x: 0, z: 0);
        var caster = Caster();
        var telegraph = SpreadMark();
        var dodges = new System.Collections.Generic.List<int>();
        insideAtDeath = false;
        for (var tick = 0; tick <= lastTick; tick++)
        {
            if (casterDies && tick == DeathTick)
            {
                Kill(caster);
                caster.CastId = castInProgress;
                insideAtDeath = Positioning.InTelegraph(PointMm.Of(self.Pos), telegraph);
            }
            var result = bot.Decide(View(MakeState(tick, self, caster), null, telegraph), memory, BotId, prng);
            memory = result.Memory;
            prng = result.Prng;
            var move = FirstMove(result.Commands);
            if (move is not null)
            {
                dodges.Add(tick);
            }
            SetPos(self, ApplyMove(PointMm.Of(self.Pos), move, StepMm));
        }
        return dodges;
    }

    [Fact(DisplayName = "dead-caster telegraph > a bot stops dodging the tick its caster dies, long before the telegraph would resolve")]
    public void StopsDodgingOnCasterDeath()
    {
        var dodges = DodgeTicks(casterDies: true, castInProgress: null, lastTick: 20, out var insideAtDeath);

        Assert.True(insideAtDeath, "the bot is still inside the telegraph when its caster dies");
        // Budget 0 ticks after death: the danger tier runs every tick, skill-dial free (no reaction delay), off the view.
        Assert.Equal(new[] { 0, 1, 2 }, dodges);
    }

    [Fact(DisplayName = "dead-caster telegraph: control > a bot keeps dodging a living caster's telegraph until it is clear")]
    public void KeepsDodgingLivingCaster()
    {
        var dodges = DodgeTicks(casterDies: false, castInProgress: null, lastTick: 20, out _);

        Assert.True(dodges.Count >= 10, $"dodged {dodges.Count} ticks; a 3000 mm circle at 300 mm/tick needs 10");
        Assert.Equal(0, dodges[0]);
        Assert.Contains(DeathTick, dodges);
    }

    [Fact(DisplayName = "dead-caster telegraph: control > a dead caster with a cast still in progress keeps its telegraph dangerous")]
    public void KeepsDodgingDeadCasterMidCast()
    {
        var dodges = DodgeTicks(casterDies: true, castInProgress: "pair.spreadMark", lastTick: 20, out _);

        Assert.True(dodges.Count >= 10, $"dodged {dodges.Count} ticks; Rules still completes a dead caster's cast in progress");
        Assert.Contains(DeathTick, dodges);
    }

    [Fact(DisplayName = "dead-caster telegraph > floor escape no longer defers to a dead caster's telegraph")]
    public void FloorEscapeIgnoresDeadCasterTelegraph()
    {
        var self = MakeActor(id: BotId, x: 0, z: 0);
        var caster = Caster();
        Kill(caster);
        var patch = new BotTelegraph("floor:hostile-patch", TelegraphShape.Circle, new PointMm(0, 0), 300, false) { Radius = 2000 };
        var view = View(MakeState(DeathTick, self, caster), null, SpreadMark(), patch);

        Assert.NotNull(Floor.FloorEscapeCommand(view, self));
    }

    [Fact(DisplayName = "dead-caster telegraph: control > floor escape still defers to a living caster's telegraph")]
    public void FloorEscapeDefersToLivingCasterTelegraph()
    {
        var self = MakeActor(id: BotId, x: 0, z: 0);
        var patch = new BotTelegraph("floor:hostile-patch", TelegraphShape.Circle, new PointMm(0, 0), 300, false) { Radius = 2000 };
        var view = View(MakeState(DeathTick, self, Caster()), null, SpreadMark(), patch);

        Assert.Null(Floor.FloorEscapeCommand(view, self));
    }
}
