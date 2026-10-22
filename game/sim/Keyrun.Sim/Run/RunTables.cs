using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The derived tables a <see cref="RunRoot.StepRun"/> reads off its <see cref="RunContent"/>, built once per content
/// instance instead of once per tick: the ability tag map (<see cref="RunWiring.AbilityTagsOf"/>), the encounter's
/// script view of each ability (<see cref="RunWiring.ScriptAbilitiesOf"/>), and the combat <see cref="StepRules"/>
/// (<see cref="Rules.Make"/> over <see cref="RunWiring.RulesConfigOf"/>, whose op validation used to run every tick).
/// Every table is a pure function of its inputs, so caching it changes no result: the rules are memoised by the
/// identity of the encounter rules (and, armed, the boss config) they were made from, and rebuilt when either changes.
/// </summary>
public sealed class RunTables
{
    // Keyed on the content instance itself (reference identity, never record equality), as NavGrid caches its grids.
    private static readonly ConditionalWeakTable<RunContent, RunTables> Cache = new();

    private readonly RunContent _content;
    private RulesMemo? _rules;
    private RulesMemo? _armedRules;
    private int _rulesBuilds;

    private RunTables(RunContent content)
    {
        _content = content;
        Tags = RunWiring.AbilityTagsOf(content.Abilities);
        ScriptAbilities = RunWiring.ScriptAbilitiesOf(content.Abilities);
    }

    /// <summary>Ability id to its tags (null = untagged), for break-bar drains.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>?> Tags { get; }

    /// <summary>Ability id to the encounter's script view of it.</summary>
    public IReadOnlyDictionary<string, ScriptAbility> ScriptAbilities { get; }

    /// <summary>How many times <see cref="Rules.Make"/> has run for this content (a cache miss), for the cache tests.</summary>
    public int RulesBuilds => Volatile.Read(ref _rulesBuilds);

    /// <summary>The tables of <paramref name="content"/>, built on first use and shared by every later step.</summary>
    /// <param name="content">The run content.</param>
    /// <returns>The tables.</returns>
    public static RunTables Of(RunContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Cache.GetValue(content, static c => new RunTables(c));
    }

    /// <summary>The unarmed step rules: the content's rules with <paramref name="encounterRules"/> as the Clause.</summary>
    /// <param name="encounterRules">The run's current encounter rules.</param>
    /// <returns>The step rules.</returns>
    public StepRules Rules(EncounterRules encounterRules)
    {
        var memo = _rules;
        if (memo is not null && ReferenceEquals(memo.EncounterRules, encounterRules))
        {
            return memo.Rules;
        }
        memo = new RulesMemo(encounterRules, null, Build(encounterRules, null));
        _rules = memo;
        return memo.Rules;
    }

    /// <summary>The armed step rules: <see cref="Rules(EncounterRules)"/> with the boss's combat abilities.</summary>
    /// <param name="encounterRules">The run's current encounter rules.</param>
    /// <param name="boss">The armed boss's config.</param>
    /// <returns>The step rules.</returns>
    public StepRules ArmedRules(EncounterRules encounterRules, BossConfig boss)
    {
        ArgumentNullException.ThrowIfNull(boss);
        var memo = _armedRules;
        if (memo is not null && ReferenceEquals(memo.EncounterRules, encounterRules) && ReferenceEquals(memo.Boss, boss))
        {
            return memo.Rules;
        }
        memo = new RulesMemo(encounterRules, boss, Build(encounterRules, boss));
        _armedRules = memo;
        return memo.Rules;
    }

    private StepRules Build(EncounterRules encounterRules, BossConfig? boss)
    {
        // The run's Clause rides in its rules for exactly this step (clause-hooks.ts withEncounterRules).
        var rulesConfig = RunWiring.RulesConfigOf(_content, encounterRules);
        if (boss is { } armedBoss)
        {
            // LOCK-TELEGRAPH: an armed boss's Lock cast is a cast bar only; the boss rules own its shield and Window.
            rulesConfig = rulesConfig with { Abilities = BossRules.CombatAbilities(armedBoss, rulesConfig.Abilities) };
        }
        var rules = Combat.Rules.Make(rulesConfig);
        Interlocked.Increment(ref _rulesBuilds);
        return rules;
    }

    private sealed record RulesMemo(EncounterRules EncounterRules, BossConfig? Boss, StepRules Rules);
}
