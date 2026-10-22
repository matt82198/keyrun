using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

public partial class Screen
{
    /// <summary>
    /// Plays <paramref name="classId"/> on the next Queue (the Character tab). Refused while queued; the build becomes
    /// that class's saved build (<see cref="Lobby.PickClass"/>).
    /// </summary>
    /// <param name="classId">The class.</param>
    public void PickClass(string classId)
    {
        if (!IsOpen || _lobby is not { } lobby || _driver?.Registry is not { } reg)
        {
            return;
        }
        var next = lobby.PickClass(reg, _progression, classId);
        _status = ReferenceEquals(next, lobby) && lobby.Locked ? "The class is locked in." : string.Empty;
        if (ReferenceEquals(next, lobby))
        {
            return;
        }
        _lobby = next;
        TalentFork = 0;
        Proof(FormattableString.Invariant(
            $"SCREEN class pick class={next.ClassId} name=\"{ClassName()}\" picks={next.Talents.Count} forks={next.Talents.ForkCount}"));
        foreach (var (keyLabel, name) in Abilities())
        {
            Proof($"SCREEN class ability key={keyLabel} name=\"{name}\"");
        }
        ProofSheet();
        ProofSeats();
    }

    // The Character tab's sheet (f-08279d82): class, key, Ascension and the five slots, from the pure CharacterSheet.
    private CharacterSheet? Sheet() =>
        _driver?.Registry is { } reg && _lobby is { } lobby ? CharacterSheet.Of(reg, _progression, lobby) : null;

    private void ProofSheet()
    {
        if (Sheet() is not { } sheet)
        {
            return;
        }
        Proof(FormattableString.Invariant(
            $"SCREEN sheet class={sheet.ClassId} key={sheet.KeyLevel} marks={sheet.Ascension.Marks} ascended={(sheet.Ascension.Ascended ? 1 : 0)}"));
        foreach (var g in sheet.Gear)
        {
            Proof(g.Equipped
                ? FormattableString.Invariant(
                    $"SCREEN sheet slot={g.Slot} state=equipped tier={g.Tier} name=\"{g.Name}\" facet={g.FacetId} active={(g.FacetActive ? 1 : 0)}")
                : FormattableString.Invariant($"SCREEN sheet slot={g.Slot} state=empty"));
        }
    }

    // Character tab, middle: who this character is before the queue: the class, the key it queues at, the Ascension
    // marks, and the five equipped slots by name with tier and Facet (the full detail and the reroll are on Gear).
    private void DrawSheet(Rect2 r)
    {
        Band(r, "Character");
        if (Sheet() is not { } sheet)
        {
            return;
        }
        var x = r.Position.X + 14;
        var inner = r.Size.X - 28;
        var y = r.Position.Y + 60;
        HudNodes.Text(this, new Vector2(x, y), sheet.ClassName, 22, Parchment);
        var keyText = FormattableString.Invariant($"Key +{sheet.KeyLevel}");
        HudNodes.Text(this, new Vector2(r.End.X - 14 - HudNodes.Width(keyText, 18), y), keyText, 18, Gold);
        y += 22;
        var marks = sheet.Ascension.Ascended || sheet.Ascension.Marks > 0
            ? FormattableString.Invariant($"Ascension marks {sheet.Ascension.Marks}{(sheet.Ascension.Ascended ? "  (ascended)" : string.Empty)}")
            : FormattableString.Invariant($"Slots at tier {Progression.MaxTier}: {sheet.Ascension.SlotsAtCap} of {Rewards.GearSlots.Count}");
        HudNodes.Text(this, new Vector2(x, y), Clip(marks, inner, SmallFont), SmallFont, sheet.Ascension.Ascended ? Gold : Dim);
        y += 16;
        var rowHeight = Math.Min(64f, (r.End.Y - y - 8) / sheet.Gear.Count);
        foreach (var g in sheet.Gear)
        {
            var box = new Rect2(x, y, inner, rowHeight - 6);
            DrawRect(box, CardBg);
            DrawRect(box, BronzeDim, filled: false, width: 1f);
            HudNodes.Text(this, box.Position + new Vector2(10, 18), g.Slot.ToUpperInvariant(), SmallFont, Dim);
            if (!g.Equipped)
            {
                HudNodes.Text(this, box.Position + new Vector2(96, 18), "Empty", BodyFont, new Color(Parchment, 0.4f));
            }
            else
            {
                var tier = FormattableString.Invariant($"Tier {g.Tier}");
                HudNodes.Text(this, box.Position + new Vector2(box.Size.X - 10 - HudNodes.Width(tier, BodyFont), 18), tier, BodyFont, TierColor(g.Tier));
                HudNodes.Text(this, box.Position + new Vector2(96, 18), Clip(g.Name, box.Size.X - 180, BodyFont), BodyFont, Parchment);
                HudNodes.Text(this, box.Position + new Vector2(10, 38), Clip(g.FacetText, box.Size.X - 20, SmallFont), SmallFont, g.FacetActive ? Calm : Dim);
            }
            y += rowHeight;
        }
    }

    // Up/Down on the Character tab: the next or previous class in roster order.
    private void CycleClass(int delta)
    {
        var ids = ClassIds();
        if (ids.Count == 0)
        {
            return;
        }
        var i = Math.Max(0, ids.ToList().IndexOf(ClassId));
        PickClass(ids[(((i + delta) % ids.Count) + ids.Count) % ids.Count]);
    }

    private IReadOnlyList<string> ClassIds() =>
        _driver?.Registry is { } reg ? Lobby.ClassIds(reg) : Array.Empty<string>();

    private string ClassName() => ClassName(ClassId);

    private string ClassName(string classId) =>
        _driver?.Registry is { } reg && reg.Classes.TryGetValue(classId, out var c) ? c.Name : classId;

    // The chosen class's keybind slots (all nine, 1-5 then Q E R F), from content: the class picked on the Character
    // tab, not the last run's setup.
    private IReadOnlyList<AbilitySlot> ClassSlots() =>
        _driver?.Registry is { } reg && reg.Classes.TryGetValue(ClassId, out var cls)
            ? HostRunSetup.ResolveSlots(cls)
            : Array.Empty<AbilitySlot>();

    private IEnumerable<(string Key, string Name)> Abilities() =>
        ClassSlots().Take(5).Select(s => (s.Key, _driver!.AbilityName(s.AbilityId)));

    // Character tab, left: one card per class (name, role, resource), the chosen one framed in gold.
    private void DrawClasses(ScreenLayout l)
    {
        Band(l.Classes, "Class");
        var ids = ClassIds();
        for (var i = 0; i < ids.Count && i < l.ClassCards.Count; i++)
        {
            var r = l.ClassCards[i];
            var on = ids[i] == ClassId;
            DrawRect(r, on ? TabOn : CardBg);
            DrawRect(r, on ? Gold : BronzeDim, filled: false, width: on ? 2f : 1f);
            HudNodes.Text(this, r.Position + new Vector2(12, 26), ClassName(ids[i]), 18, on ? Gold : Parchment);
            var role = HostRunSetup.Roster.FirstOrDefault(s => s.ClassId == ids[i])?.Role.ToString() ?? string.Empty;
            var loop = _driver?.Registry is { } reg && reg.Classes.TryGetValue(ids[i], out var cls) ? cls.Loop.Resource : string.Empty;
            HudNodes.Text(this, r.Position + new Vector2(12, 48), Clip($"{role}  -  {loop}", r.Size.X - 24, SmallFont), SmallFont, Dim);
            if (on)
            {
                HudNodes.Text(this, r.Position + new Vector2(12, 70), Locked() ? "Locked in for this queue" : "Playing", SmallFont, Good);
            }
        }
        var since = _progression.LastRun?.Text() ?? "First run";
        HudNodes.Text(this, new Vector2(l.Classes.Position.X + 14, l.Classes.End.Y - 14),
            Clip("Since last run: " + since, l.Classes.Size.X - 28, SmallFont), SmallFont, Calm);
        DrawAscension(l.Classes);
    }

    // Character tab, under the class cards and above "Since last run": the private Ascension line (BIBLE s7, f-275d47c9),
    // read from this screen's own progression (the one the run banks into and the save holds), never another seat's.
    private void DrawAscension(Rect2 classes)
    {
        var state = Ascension.Of(_progression);
        var lines = Wrap(AscensionReadout.CharacterLine(state), classes.Size.X - 28, SmallFont, 3);
        var y = classes.End.Y - 14 - 22 - ((lines.Count - 1) * 16);
        foreach (var line in lines)
        {
            HudNodes.Text(this, new Vector2(classes.Position.X + 14, y), line, SmallFont, state.Ascended ? Gold : Dim);
            y += 16;
        }
    }

    // Character tab, right: the chosen class's nine keybinds (1-5, Q interrupt, E defensive, R utility, F movement).
    private void DrawKit(Rect2 r)
    {
        Band(r, "Kit");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 58;
        foreach (var slot in ClassSlots())
        {
            DrawRect(new Rect2(x, y - 15, 22, 20), CardBg);
            DrawRect(new Rect2(x, y - 15, 22, 20), BronzeDim, filled: false, width: 1f);
            HudNodes.Text(this, new Vector2(x + 6, y), slot.Key, BodyFont, Gold);
            HudNodes.Text(this, new Vector2(x + 32, y), Clip(_driver!.AbilityName(slot.AbilityId), r.Size.X - 60, BodyFont), BodyFont, Parchment);
            y += 26;
        }
    }
}
