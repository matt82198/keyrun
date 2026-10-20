using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Cold Snap clears the Cinder's own Chill (design/BIBLE.md section 3 Cinder: Overheat "applies Chill (slower casts
/// for 3 s)" to the Cinder, and Cold Snap "dumps Heat, clears Chill, freezes the target briefly"). Driven through the
/// real cast pipeline with the shipped cinder.coldSnap and chill definitions from the hollow-vault k2 oracle setup.
/// </summary>
public sealed class ColdSnapChillTargetTests
{
    private const int CinderId = 3;
    private const int EnemyId = 900;

    private static bool Chilled(ActorState a) => a.Effects.Any(e => e.EffectId == "chill");

    [Fact]
    public void Cold_Snap_removes_Chill_from_the_casting_Cinder()
    {
        var content = OracleSetupContent.Load("hollow-vault-k2.setup.json");
        var cinder = Make(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, maxHealth: 250, maxResource: 100);
        var enemy = Make(EnemyId, ActorKind.Enemy, "boss", x: 1000, maxHealth: 5000);
        cinder.Resource = 100;
        cinder.TargetId = EnemyId;
        var state = At(0, cinder, enemy);
        Effects.ApplyEffectInstance(cinder, content.Effects["chill"], CinderId, 60, 1, 0);
        Effects.ApplyEffectInstance(enemy, content.Effects["chill"], CinderId, 60, 1, 0);

        var rules = Rules.Make(Config(content.Abilities, content.Effects));
        Tick(state, rules, Cmds(Cast(CinderId, "cinder.coldSnap", EnemyId)), _ => { });

        Assert.Equal(0, cinder.Resource); // the cast landed: Heat dumped
        Assert.False(Chilled(cinder), "Cold Snap must clear the caster's Chill");
    }
}
