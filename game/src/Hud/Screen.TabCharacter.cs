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
