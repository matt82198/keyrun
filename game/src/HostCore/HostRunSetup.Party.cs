using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

public sealed partial record HostRunSetup
{
    /// <summary>
    /// Each bot seat's gear layer (BIBLE s6 parity, D32 gap audit): bot actor id to its <see cref="C.BotGear.Resolve"/>
    /// result, the class's default loadout through the same <see cref="C.GearFacets.Resolve"/> the human's gear uses.
    /// Empty for a solo run.
    /// </summary>
    public IReadOnlyDictionary<int, C.FacetResolution> BotGear { get; init; } = new Dictionary<int, C.FacetResolution>();

    /// <summary>
    /// <see cref="BotGear"/> as <see cref="RunOptions.Loadouts"/> entries: bot actor id to its Facet-patched abilities
    /// converted to sim AbilityDefs (the conversion the run's base abilities go through). SimDriver merges these with the
    /// queued request's (human) loadouts for every run it creates.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>> BotLoadouts { get; init; } =
        new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();

    /// <summary>
    /// <paramref name="options"/> (the queued request's, or null) with every bot seat's <see cref="BotLoadouts"/> entry
    /// added for the seats in <paramref name="party"/>. A loadout the options already carry for an actor wins.
    /// </summary>
    /// <param name="options">The run options, or null.</param>
    /// <param name="party">The run's party.</param>
    /// <returns>The options to create the run with (unchanged when no bot seat has gear).</returns>
    public RunOptions? WithBotLoadouts(RunOptions? options, IReadOnlyList<PartyMemberSpec> party)
    {
        ArgumentNullException.ThrowIfNull(party);
        var seats = party.Select(p => p.Id).Where(id => BotLoadouts.TryGetValue(id, out var kit) && kit.Count > 0).ToList();
        if (seats.Count == 0)
        {
            return options;
        }
        var merged = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();
        foreach (var id in seats)
        {
            merged[id] = BotLoadouts[id];
        }
        if (options?.Loadouts is { } carried)
        {
            foreach (var kv in carried)
            {
                merged[kv.Key] = kv.Value;
            }
        }
        return (options ?? new RunOptions()) with { Loadouts = merged };
    }

    // Every bot seat's default gear through C.BotGear.Resolve (GearFacets.Resolve). The refuse check is what the sim
    // conversion cannot play (a line width, an op the sim union lacks): that Facet is skipped and reported, never a
    // refused run, as the human's gear layer does (Screen.RunLoadout).
    private static IReadOnlyDictionary<int, C.FacetResolution> BuildBotGear(C.ContentRegistry registry, IReadOnlyList<PartyMemberSpec> party)
    {
        var gear = new Dictionary<int, C.FacetResolution>();
        foreach (var seat in party.Where(p => p.Id != HumanId).OrderBy(p => p.Id))
        {
            gear[seat.Id] = C.BotGear.Resolve(registry, registry.Classes[seat.ClassId], RefuseUnplayable);
        }
        return gear;
    }

    private static string? RefuseUnplayable(C.AbilityDef a)
    {
        if (a.Shape.Width is not null)
        {
            return "line width is not in the sim shape";
        }
        try
        {
            ToSim(a);
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// The options a run is created with (BIBLE s7, finding f-f9b80112): the request's Clause and talents plus the
    /// human's equipped gear, every slot and the keystone included, each Facet's content patch stacked on the human's
    /// (talented) kit (<see cref="RunStart.Of"/> over <see cref="GearPatches"/>). SimDriver.StartRun passes
    /// <see cref="RunStart.Options"/> to <see cref="RunRoot.CreateRun"/>. With no progression (no screen yet) or nothing
    /// equipped the options are the request's alone, so such a run is created exactly as before.
    /// </summary>
    /// <param name="request">The queued request, or null.</param>
    /// <param name="progression">The screen's progression, or null.</param>
    /// <param name="registry">The content.</param>
    /// <param name="humanClassId">The human's class (scopes the Facets and fills an appended Window's setupClass).</param>
    /// <returns>The run start.</returns>
    public RunStart StartFor(QueueRequest? request, Progression? progression, C.ContentRegistry registry, string humanClassId)
    {
        if (progression is null)
        {
            return RunStart.Of(request, null, HumanActorId, Content.Abilities, new Dictionary<string, GearPatch>(StringComparer.Ordinal));
        }
        // ABILITY-TREES R-TREES-8: an owned enhanced node is refused here (fail closed), then every rank edit is passed.
        RequireOwnedEdits(registry, progression);
        return RunStart.Of(
            request,
            progression,
            HumanActorId,
            Content.Abilities,
            GearPatches(registry, humanClassId),
            TreeEdits(registry));
    }

    /// <summary>
    /// The sim patch of every content Facet on <paramref name="classId"/>'s kit (content/schema/facet.ts FacetPatch to
    /// <see cref="GearPatch"/>, ops through the same <c>ToSim</c> the base abilities use). An appended Window op without
    /// a setupClass is the wearer's class Window (every base Window op of a class carries that class id), so it gets
    /// <paramref name="classId"/>. A line shape's full width becomes the sim line's half-width radius
    /// (<see cref="Abilities.LineShape"/>), so a width Facet (twinStrike, infernoBolt) is applied, not just carried.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="classId">The wearer's class.</param>
    /// <returns>Patches by Facet id.</returns>
    /// <exception cref="InvalidOperationException">The class is unknown, or a Facet op is one the sim does not run.</exception>
    public static IReadOnlyDictionary<string, GearPatch> GearPatches(C.ContentRegistry registry, string classId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!registry.Classes.TryGetValue(classId, out var classDef))
        {
            throw new InvalidOperationException($"run setup: unknown class \"{classId}\"");
        }
        var kit = new HashSet<string>(ResolveSlots(classDef).Select(s => s.AbilityId), StringComparer.Ordinal);
        var patches = new Dictionary<string, GearPatch>(StringComparer.Ordinal);
        foreach (var facet in registry.Facets.Values.Where(f => kit.Contains(f.AbilityId)).OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            var p = facet.Patch;
            var where = $"facet \"{facet.Id}\"";
            IReadOnlyList<EffectOp>? Ops(IEnumerable<C.EffectOp>? ops) => ops?.Select(o => ToSim(
                o is C.WindowOp { SetupClass: null } w ? w with { SetupClass = classId } : o, where)).ToList();
            patches[facet.Id] = new GearPatch
            {
                CastTicks = p.CastTicks,
                GcdTicks = p.GcdTicks,
                CooldownTicks = p.CooldownTicks,
                Charges = p.Charges,
                Cost = p.Cost is { } c ? new AbilityCost(c.Resource, c.Amount) : null,
                Range = p.Range,
                Shape = p.Shape is { } s ? ToSim(s, where) : null,
                Interruptible = p.Interruptible,
                Tags = p.Tags?.ToList(),
                Effects = Ops(p.Effects),
                Variants = p.Variants?.ToDictionary(kv => kv.Key, kv => Ops(kv.Value)!, StringComparer.Ordinal),
                TagAdd = p.TagAdd?.ToList(),
                TagRemove = p.TagRemove?.ToList(),
                EffectsAppend = Ops(p.EffectsAppend),
            };
        }
        return patches;
    }

    /// <summary>
    /// The party for a run over <paramref name="runDungeon"/> (the setup's dungeon, or a <c>--pull N</c> slice of it):
    /// <see cref="Party"/> in the Entrance for a run from the first pull, else the same seats at the start of the first
    /// pull's room, inside its trigger, so the sliced run begins in its fight as the flat arena's did.
    /// </summary>
    /// <param name="runDungeon">The dungeon the run is created with.</param>
    /// <returns>The placed party.</returns>
    public IReadOnlyList<PartyMemberSpec> PartyFor(DungeonDef runDungeon)
    {
        ArgumentNullException.ThrowIfNull(runDungeon);
        if (Layout is null || runDungeon.Pulls.Count == 0 || runDungeon.Pulls[0].Id == Dungeon.Pulls[0].Id)
        {
            return Party;
        }
        return NavLayouts.PlaceParty(Layout, Party, runDungeon.Pulls[0].Id);
    }

    /// <summary>
    /// content-party.ts buildParty over the three shipped classes: the human takes the roster slot of its class (else
    /// the tank slot) as actor <see cref="HumanId"/>, every other slot becomes a bot seat with ids from 2 in roster
    /// order. Spawn x, role and max health come from the slot; max resource from the class's loop. With
    /// <paramref name="withBots"/> false only the human's seat is built (the phase 2 solo run).
    /// </summary>
    /// <param name="registry">The content (each roster class must exist).</param>
    /// <param name="humanClassId">The human's class.</param>
    /// <param name="withBots">True to fill the other roster slots with bot seats.</param>
    /// <returns>The party in actor id order.</returns>
    public static IReadOnlyList<PartyMemberSpec> BuildParty(C.ContentRegistry registry, string humanClassId, bool withBots = true)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var humanSlot = Math.Max(0, Roster.ToList().FindIndex(s => s.ClassId == humanClassId));
        var party = new List<PartyMemberSpec>();
        var nextBotId = HumanId + 1;
        for (var i = 0; i < Roster.Count; i++)
        {
            var slot = Roster[i];
            var isHuman = i == humanSlot;
            if (!isHuman && !withBots)
            {
                continue;
            }
            var classId = isHuman ? humanClassId : slot.ClassId;
            if (!registry.Classes.TryGetValue(classId, out var cls))
            {
                throw new InvalidOperationException($"run setup: party slot {i} references unknown class \"{classId}\"");
            }
            var id = isHuman ? HumanId : nextBotId++;
            party.Add(new PartyMemberSpec(id, cls.Id, slot.Role, new Vec3(slot.SpawnX, 0, WardenSpawn.Z), slot.MaxHealth, cls.Loop.Max)
            {
                Yaw = WardenYaw,
                // design/BIBLE.md section 3.1: the class's passive armor from run.json classArmorBp.
                ArmorBp = registry.Run.ClassArmorBp.TryGetValue(cls.Id, out var armorBp) ? armorBp : 0,
            });
        }
        return party.OrderBy(p => p.Id).ToList();
    }

    /// <summary>
    /// HUMAN-PLAY (D65/D66, "play any class"): when the human (actor <see cref="HumanId"/>) is not the tank, every bot seat
    /// whose class has a travel slot (layout <c>formation</c>) starts at that slot behind the human instead of at its
    /// roster spot. The roster spawn puts the Warden in front (x 0, the Chanter at -2100, the Cinder at -700), so a
    /// Chanter or Cinder human leading from the start (D44: bots follow the lowest living seat) had the Warden walk back
    /// through the human to reach its slot: actor bodies collide (run.json movement radiusMm), the two pushed head-on, and
    /// a headless Cinder-led run stood in the Entrance until the timer cap. A human Warden's party (the tank leads) and a
    /// solo seat are returned unchanged.
    /// </summary>
    /// <param name="layout">The content layout (its formation).</param>
    /// <param name="party">The placed party, in actor id order.</param>
    /// <returns>The party, bot seats moved to their formation slots when the human is not the tank.</returns>
    public static IReadOnlyList<PartyMemberSpec> InFormation(C.LayoutDef layout, IReadOnlyList<PartyMemberSpec> party)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(party);
        if (party.FirstOrDefault(p => p.Id == HumanId) is not { } human || human.Role == ActorRole.Tank || party.Count < 2)
        {
            return party;
        }
        return party.Select(p =>
        {
            var slot = p.Id == HumanId ? null : layout.Formation.FirstOrDefault(s => string.Equals(s.ClassId, p.ClassId, StringComparison.Ordinal));
            return slot is null ? p : p with { Pos = new Vec3(human.Pos.X - slot.BehindMm, human.Pos.Y, human.Pos.Z + slot.SideMm) };
        }).ToList();
    }

    /// <summary>
    /// input/slot-abilities.ts resolveSlotAbilities: each keybind slot to the class kit's ability, in ability-bar order
    /// (1-5 rotational, Q interrupt, E defensive, R utility, F movement). An empty slot throws.
    /// </summary>
    /// <param name="classDef">The class.</param>
    /// <returns>Nine slots.</returns>
    public static IReadOnlyList<AbilitySlot> ResolveSlots(C.ClassDef classDef)
    {
        ArgumentNullException.ThrowIfNull(classDef);
        var a = classDef.Abilities;
        var slots = new[]
        {
            new AbilitySlot(KeyAction.Ability1, "1", a.Rot1),
            new AbilitySlot(KeyAction.Ability2, "2", a.Rot2),
            new AbilitySlot(KeyAction.Ability3, "3", a.Rot3),
            new AbilitySlot(KeyAction.Ability4, "4", a.Rot4),
            new AbilitySlot(KeyAction.Ability5, "5", a.Rot5),
            new AbilitySlot(KeyAction.AbilityQ, "Q", a.Interrupt),
            new AbilitySlot(KeyAction.AbilityE, "E", a.Defensive),
            new AbilitySlot(KeyAction.AbilityR, "R", a.Utility),
            new AbilitySlot(KeyAction.AbilityF, "F", a.Movement),
        };
        foreach (var s in slots)
        {
            if (string.IsNullOrEmpty(s.AbilityId))
            {
                throw new InvalidOperationException($"run setup: class \"{classDef.Id}\" has no ability in slot {s.Key}");
            }
        }
        return slots;
    }
}
