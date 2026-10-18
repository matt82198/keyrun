using System;

namespace Keyrun.Sim.Combat;

/// <summary>
/// What combat needs from the run's active Clause (design/BIBLE.md section 8; sim/encounter/clauses.ts
/// EncounterRules): the Hush silence predicate that startCast reads (casting.ts:62-63, clauses.ts:134
/// shouldSilence) and the Pact damage weighting that applyDamage reads (damage.ts:54-55, clauses.ts:142
/// applyPactDamageBp). The encounter lane (2C) owns EncounterRules; the run root (2D) adapts it to this
/// interface, so combat never references the encounter types.
/// </summary>
public interface ICombatClause
{
    /// <summary>clauses.ts shouldSilence(tick, rules): true inside a Hush silence pulse.</summary>
    /// <param name="tick">The tick a cast would start on.</param>
    /// <returns>True when casting is silenced.</returns>
    bool IsSilenced(int tick);

    /// <summary>clauses.ts applyPactDamageBp(raw, wasCombo, rules): the raw hit, Pact-weighted (raw when Pact is not active).</summary>
    /// <param name="rawAmount">The raw hit.</param>
    /// <param name="wasCombo">True while a Payoff's combo effects apply.</param>
    /// <returns>The weighted amount.</returns>
    long WeightDamage(long rawAmount, bool wasCombo);
}

/// <summary>
/// Port of sim/combat/clause-hooks.ts as an explicit per-step context instead of module state. The TS installs the
/// run's EncounterRules process-wide for one synchronous stepRun span (withEncounterRules) and counts a combo depth
/// (runAsCombo) so applyDamage can tell a Pact combo hit from a solo one. Here <see cref="Rules.Make"/> builds one
/// ClauseHooks per step from <see cref="RulesConfig.Clause"/> and threads it through <see cref="CastContext"/> and
/// <see cref="WindowsContext"/>; two runs can never see each other's Clause.
/// </summary>
public sealed class ClauseHooks
{
    private readonly DamageWeighting? _weighting;
    private int _comboDepth;

    /// <summary>Hooks for one step of a run.</summary>
    /// <param name="clause">The active Clause, or null when the run has none (the TS <c>undefined</c> rules).</param>
    public ClauseHooks(ICombatClause? clause)
    {
        Clause = clause;
        _weighting = clause is null ? null : raw => clause.WeightDamage(raw, IsComboActive);
    }

    /// <summary>The active Clause, or null.</summary>
    public ICombatClause? Clause { get; }

    /// <summary>clause-hooks.ts:56-58 isComboActive: true while <see cref="RunAsCombo"/> brackets a combo's effects.</summary>
    public bool IsComboActive => _comboDepth > 0;

    /// <summary>
    /// The Pact weighting for <see cref="Damage.ApplyDamage"/> / <see cref="Mitigation.DealDamage"/>: null when there is
    /// no Clause (damage.ts:55 passes raw through), else one that reads <see cref="IsComboActive"/> at hit time.
    /// </summary>
    public DamageWeighting? Weighting => _weighting;

    /// <summary>casting.ts:62-63: <c>rules &amp;&amp; shouldSilence(now, rules)</c>.</summary>
    /// <param name="tick">The tick a cast would start on.</param>
    /// <returns>True when casting is silenced.</returns>
    public bool IsSilenced(int tick) => Clause is not null && Clause.IsSilenced(tick);

    /// <summary>clause-hooks.ts:60-67 runAsCombo: depth-counted, restored even on a throw.</summary>
    /// <param name="fn">The combo-effect span.</param>
    public void RunAsCombo(Action fn)
    {
        ArgumentNullException.ThrowIfNull(fn);
        _comboDepth += 1;
        try
        {
            fn();
        }
        finally
        {
            _comboDepth -= 1;
        }
    }
}
