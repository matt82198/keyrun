using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Hush silences the party only (design/BIBLE.md section 8: "Hush (silence pulses every 45 s; save the kicks)";
/// content/data/clauses.json: "A silence pulse hits the party"). The step rules (<see cref="Rules.Make"/>) must not
/// hand the silence to an enemy's scripted cast (Encounter/Scripts.cs makes those plain CastCommands), while a
/// player's cast inside the same pulse is still refused.
/// </summary>
public sealed class HushPartyOnlyTests
{
    /// <summary>A Hush stand-in: one silence pulse on [100, 140), no Pact weighting.</summary>
    private sealed class PulseClause : ICombatClause
    {
        public bool IsSilenced(int tick) => tick >= 100 && tick < 140;

        public long WeightDamage(long rawAmount, bool wasCombo) => rawAmount;
    }

    private const int EnemyId = 1;
    private const int PlayerId = 2;

    private static readonly AbilityDef Slam = Def("slam", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "physical") }, castTicks: 20);
    private static readonly AbilityDef Bolt = Def("bolt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "physical") }, castTicks: 20);

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> SlamScript =
        new Dictionary<int, IReadOnlyList<ScriptStep>> { [EnemyId] = new[] { ScriptStep.Loop(100, "slam") } };

    private static (SimState State, ActorState Enemy, ActorState Player, StepRules Rules) Arena()
    {
        var enemy = Make(EnemyId, ActorKind.Enemy, "boss", maxHealth: 1000);
        var player = Make(PlayerId, ActorKind.Player, "cinder", ActorRole.Damage, x: 1000, maxHealth: 1000, maxResource: 100);
        var state = At(100, enemy, player);
        var rules = Rules.Make(Config(AbilityMap(Slam, Bolt), clause: new PulseClause()));
        return (state, enemy, player, rules);
    }

    [Fact]
    public void Hush_pulse_does_not_silence_enemy_scripted_casts()
    {
        var (state, enemy, _, rules) = Arena();
        var cmds = Scripts.StepEnemyScripts(state, SlamScript).Commands;
        Assert.Equal(new Command[] { new CastCommand(100, EnemyId, "slam", PlayerId) }, cmds.ToArray());

        rules(state, cmds, _ => { });

        Assert.Equal("slam", enemy.CastId);
        Assert.Equal(120, enemy.CastEndsTick);
    }

    [Fact]
    public void Hush_pulse_still_silences_a_party_cast_on_the_same_tick()
    {
        var (state, _, player, rules) = Arena();

        rules(state, Cmds(Cast(PlayerId, "bolt", EnemyId)), _ => { });

        Assert.Null(player.CastId);
    }

    [Fact]
    public void Outside_the_pulse_the_party_casts_normally()
    {
        var (state, _, player, rules) = Arena();
        state.Tick = 140;

        rules(state, Cmds(Cast(PlayerId, "bolt", EnemyId)), _ => { });

        Assert.Equal("bolt", player.CastId);
    }
}
