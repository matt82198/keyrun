using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Every id one content kind uses to point at another must resolve in the loaded registry.
/// This walks the registry itself (independently of <see cref="ContentValidator"/>) so a
/// validator that silently checks nothing cannot make these pass.
/// </summary>
public sealed class ReferenceTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private sealed class Refs
    {
        public readonly List<string> Missing = new();
        public int Checked;

        public void Expect(bool resolves, string what)
        {
            Checked++;
            if (!resolves) Missing.Add(what);
        }
    }

    private static void CheckOps(IEnumerable<EffectOp> ops, Refs refs, string where)
    {
        foreach (EffectOp op in ops)
        {
            string? effectId = op switch
            {
                ApplyEffectOp a => a.EffectId,
                RemoveEffectOp r => r.EffectId,
                _ => null,
            };
            if (effectId is null || effectId == "*") continue;
            refs.Expect(Reg.Effects.ContainsKey(effectId), where + " -> effect " + effectId);
        }
    }

    private static string Report(Refs refs) =>
        refs.Missing.Count == 0 ? "(none)" : string.Join(System.Environment.NewLine, refs.Missing);

    [Fact]
    public void Abilities_reference_only_known_effects()
    {
        var refs = new Refs();
        foreach (AbilityDef a in Reg.Abilities.Values)
        {
            CheckOps(a.Effects, refs, a.Id);
            foreach (var variant in a.Variants ?? new ValueMap<ValueList<EffectOp>>())
                CheckOps(variant.Value, refs, a.Id + " variant " + variant.Key);
        }
        foreach (EffectDef e in Reg.Effects.Values)
            if (e.Periodic is not null) CheckOps(new[] { e.Periodic.Op }, refs, "effect " + e.Id);

        Assert.True(refs.Missing.Count == 0, Report(refs));
        Assert.Equal(31, refs.Checked); // vacuity guard: exact ref count from the JSON (+6: the resonance, overheatedCore, slagBurn, boilingBlood, bronzeWard and lockShield applyEffects of abilities/bellfoundry.json, D66; was 25;+4: the searingHex, cinders, stoked and rooted applyEffects of abilities/vault.json, VAULT-KITS S1; was 21)
    }

    [Fact]
    public void Classes_reference_only_known_abilities()
    {
        var refs = new Refs();
        foreach (ClassDef c in Reg.Classes.Values)
        {
            ClassAbilitySlots s = c.Abilities;
            foreach (string id in new[] { s.Rot1, s.Rot2, s.Rot3, s.Rot4, s.Rot5, s.Interrupt, s.Defensive, s.Utility, s.Movement })
                refs.Expect(Reg.Abilities.ContainsKey(id), c.Id + " slot -> ability " + id);
            foreach (TalentFork fork in c.Talents)
            {
                refs.Expect(Reg.Abilities.ContainsKey(fork.AbilityId), c.Id + " talent fork -> ability " + fork.AbilityId);
                foreach (TalentOption opt in fork.Options)
                    if (opt.Patch.Effects is not null) CheckOps(opt.Patch.Effects, refs, c.Id + " talent " + opt.Id);
            }
        }

        Assert.True(refs.Missing.Count == 0, Report(refs));
        Assert.Equal(52, refs.Checked); // vacuity guard: exact ref count from the JSON (Overflow keeps Cold Snap stunned: +1; Flashpoint's burning, R-FORK-RETUNE-1: +1; Fortress's hasted, R-FORK-RETUNE-1: +1)
    }

    [Fact]
    public void Combos_reference_only_known_classes_and_effects()
    {
        var refs = new Refs();
        foreach (ComboEntry combo in Reg.Combos.Values)
        {
            refs.Expect(combo.SetupClass == "boss" || Reg.Classes.ContainsKey(combo.SetupClass), combo.Id + " setupClass " + combo.SetupClass);
            refs.Expect(combo.PayoffClass == "*" || Reg.Classes.ContainsKey(combo.PayoffClass), combo.Id + " payoffClass " + combo.PayoffClass);
            refs.Expect(combo.PayoffClass != combo.SetupClass, combo.Id + " setupClass equals payoffClass");
            CheckOps(combo.Effects, refs, "combo " + combo.Id);
        }

        Assert.True(refs.Missing.Count == 0, Report(refs));
        Assert.Equal(45, refs.Checked); // vacuity guard: exact ref count from the JSON
    }

    [Fact]
    public void Enemies_reference_only_known_abilities_effects_and_break_bars()
    {
        var refs = new Refs();
        var bars = Reg.Enemies.Values.SelectMany(e => e.BreakBars ?? new ValueList<BreakBarDef>()).Select(b => b.Id).ToHashSet();
        foreach (EnemyDef e in Reg.Enemies.Values)
        {
            foreach (string id in e.Abilities) refs.Expect(Reg.Abilities.ContainsKey(id), e.Id + " -> ability " + id);
            foreach (ScriptStep step in e.Script) refs.Expect(Reg.Abilities.ContainsKey(step.Cast), e.Id + " script -> ability " + step.Cast);
            foreach (BreakBarDef bar in e.BreakBars ?? new ValueList<BreakBarDef>()) CheckOps(bar.OnEmpty, refs, e.Id + " bar " + bar.Id);
            if (e.BreakBarId is not null) refs.Expect(bars.Contains(e.BreakBarId), e.Id + " -> breakBar " + e.BreakBarId);
        }

        Assert.True(refs.Missing.Count == 0, Report(refs));
        Assert.Equal(91, refs.Checked); // vacuity guard: exact ref count from the JSON (+4: vault.patroller 2 abilities and 2 script steps, R-PATROL-4; was 87;+39: the ten enemies/bellfoundry.json entries, 20 abilities, 16 script steps, the clapperBreak onEmpty and the two Clappers' breakBarId, D66; was 48;+2: seed.lash ability and script step, KF3; +2: seed.lock ability and script step, SEED-LOCK-TEACHES; +2: hall.sentry ability and script step, PATH-PACKS, STATE.md D49; +14: the five enemies/vault.json kits, 7 abilities and 7 script steps, VAULT-KITS S1)
    }

    [Fact]
    public void Dungeons_and_facets_reference_only_known_enemies_and_abilities()
    {
        var refs = new Refs();
        foreach (DungeonDef d in Reg.Dungeons.Values)
        {
            foreach (PullDef pull in d.Pulls)
                foreach (PullEnemy pe in pull.Enemies)
                    refs.Expect(Reg.Enemies.ContainsKey(pe.EnemyId), d.Id + " " + pull.Id + " -> enemy " + pe.EnemyId);
            refs.Expect(Reg.Enemies.ContainsKey(d.Boss.EnemyId), d.Id + " boss -> enemy " + d.Boss.EnemyId);
            foreach (DungeonSegment s in d.Segments)
                refs.Expect(Reg.Enemies.ContainsKey(s.Boss.EnemyId), d.Id + " " + s.Id + " boss -> enemy " + s.Boss.EnemyId);
        }
        foreach (RosterPack p in Reg.Roster.Values)
            foreach (PullEnemy pe in p.Enemies)
                refs.Expect(Reg.Enemies.ContainsKey(pe.EnemyId), "roster " + p.Id + " -> enemy " + pe.EnemyId);
        foreach (FacetDef f in Reg.Facets.Values)
        {
            refs.Expect(Reg.Abilities.ContainsKey(f.AbilityId), f.Id + " -> ability " + f.AbilityId);
            if (f.Patch.EffectsAppend is not null) CheckOps(f.Patch.EffectsAppend, refs, "facet " + f.Id);
            if (f.Patch.Effects is not null) CheckOps(f.Patch.Effects, refs, "facet " + f.Id);
        }

        Assert.True(refs.Missing.Count == 0, Report(refs));
        Assert.Equal(119, refs.Checked); // vacuity guard: exact ref count from the JSON (+7: one Scald Hound member group in each of the seven roster packs grown to 5-8 mobs, D66 big pulls; +1: the Sunken Bellfoundry's boss; +4: the four segment bosses, Hollow Vault 1 and Bellfoundry 3; +28: the 28 member groups of its 11 roster packs, D66; was 79)
    }
}
